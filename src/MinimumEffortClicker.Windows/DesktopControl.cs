using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Windows;

internal readonly record struct PhysicalMouse(InputControl Control, bool Down, int X, int Y);

/// <summary>Dedicated message thread; no UI dispatcher, key history, injection or input suppression.</summary>
public sealed class DesktopControl : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly object commandGate = new();
    private readonly HashSet<TaskCompletionSource<bool>> pendingCommands = [];
    private bool commandsEnded;
    private readonly object physicalGate = new();
    private readonly HashSet<InputControl> physicalDown = [];
    private readonly DesktopNative.Hook keyboardCallback;
    private readonly DesktopNative.Hook mouseCallback;
    private readonly DesktopNative.WinEvent windowCallback;
    private uint threadId;
    private nint keyboardHook, mouseHook, destroyHook;
    private readonly TriggerLatch trigger = new();
    private readonly Dictionary<int, (MacroBinding Binding, TriggerLatch Latch)> macroTriggers = [];
    private int toggleKey = ShortcutKeys.DefaultStartKey, stopKey = ShortcutKeys.DefaultStopKey;
    private volatile bool disposed;
    public bool Available { get; private set; }
    public string? Error { get; private set; }
    // A Stop All key outside F1–F11 is read by the input hook, which Windows does not call while an administrator
    // window is in front or after it removed the hook. This registered hotkey still reaches the clicker then.
    private const int BackupStopId = 3, BackupStopVirtualKey = 0x78;
    private const uint BackupStopModifiers = 0x4000 | 0x0002 | 0x0001; // MOD_NOREPEAT | MOD_CONTROL | MOD_ALT
    public const string BackupStopLabel = "Ctrl+Alt+F9";
    /// <summary>True while Ctrl+Alt+F9 is registered as the backup Stop All (only when the Stop All key needs the hook).</summary>
    public bool BackupStopActive { get; private set; }
    private const nuint HookWatchTimer = 1;
    private uint lastKeyboardHook, lastMouseHook, lastHookReinstall;
    /// <summary>How often the input hooks were found lost and installed again in this session.</summary>
    public int HookReinstalls { get; private set; }
    public bool HoldTrigger { get; set; }
    private int? recordingKey;
    private readonly TriggerLatch recordingTrigger = new();
    // Screenshot and Pause: global keys beside Start, Stop All and Record, handled alike (Engine GlobalKey).
    private readonly GlobalKey screenshot, pause;
    private GlobalKey[] ExtraKeys => [screenshot, pause];
    public event Action? ScreenshotRequested;
    /// <summary>The Pause key: pauses the running Action List, or goes on with a paused one.</summary>
    public event Action? PauseRequested;
    public bool ScreenshotAvailable => screenshot.Key is not null && Available && !bindingsSuspended;
    private volatile bool bindingsSuspended;
    private volatile int[] registeredKeys = [];
    private volatile int[] controlKeys = [];
    private void RefreshRegisteredKeys()
    {
        controlKeys = macroTriggers.Values.Select(m => m.Binding.Key).Append(toggleKey).Append(stopKey)
        .Concat(recordingKey is { } record ? new[] { record } : [])
        .Concat(ExtraKeys.Where(e => e.Key is not null).Select(e => e.Key!.Value)).Distinct().ToArray();
        registeredKeys = controlKeys.Where(k => !ShortcutKeys.UsesHook(k)).ToArray();
    }
    public bool IsShortcutInput(InputControl input) => controlKeys.Contains(DesktopNative.VirtualKey(input));
    public bool UsesRegisteredInput(InputControl input) => registeredKeys.Contains(DesktopNative.VirtualKey(input));
    public Func<bool>? TriggerAllowed { get; set; }
    private bool RegisterShortcut(int id, int key) => ShortcutKeys.UsesHook(key) || ProbeNative.RegisterHotKey(0, id, 0x4000, (uint)key);
    public Task<bool> SuspendBindingsAsync(bool suspended) => QueueCommand(() =>
    {
        bindingsSuspended = suspended;
        foreach (int id in macroTriggers.Keys.Append(1).Append(2).Append(BackupStopId).Append(40).Concat(ExtraKeys.Select(e => e.Id))) ProbeNative.UnregisterHotKey(0, id);
        BackupStopActive = false;
        trigger.Reset((ProbeNative.GetAsyncKeyState(toggleKey) & 0x8000) != 0); recordingTrigger.Reset();
        foreach (var extra in ExtraKeys) extra.Trigger.Reset(extra.Key is { } key && (ProbeNative.GetAsyncKeyState(key) & 0x8000) != 0);
        foreach (var item in macroTriggers.Values) item.Latch.Reset((ProbeNative.GetAsyncKeyState(item.Binding.Key) & 0x8000) != 0);
        if (suspended) return true;
        bool success = RegisterShortcut(1, stopKey) && RegisterShortcut(2, toggleKey);
        foreach (var pair in macroTriggers) success &= RegisterShortcut(pair.Key, pair.Value.Binding.Key);
        if (recordingKey is { } record) success &= RegisterShortcut(40, record);
        foreach (var extra in ExtraKeys) if (extra.Key is { } extraKey) success &= RegisterShortcut(extra.Id, extraKey);
        RegisterBackupStop();
        Available = success && keyboardHook != 0 && mouseHook != 0;
        if (!Available) { Error = "A shortcut could not be restored. Apply your hotkeys again in Settings."; StopRequested?.Invoke(Error); }
        return Available;
    });
    public event Action? RecordingRequested;
    public Task<bool> ConfigureRecordingAsync(int? key) => QueueCommand(() =>
    {
        ProbeNative.UnregisterHotKey(0, 40); recordingKey = null; RefreshRegisteredKeys();
        if (key is null) return true;
        if (!Available || !ShortcutKeys.Supported(key.Value) || key == toggleKey || key == stopKey || ExtraKeys.Any(e => e.Key == key) || macroTriggers.Values.Any(m => m.Binding.Key == key)) return false;
        if (!RegisterShortcut(40, key.Value)) return false;
        recordingTrigger.Reset((ProbeNative.GetAsyncKeyState(key.Value) & 0x8000) != 0); recordingKey = key; RefreshRegisteredKeys(); return true;
    });
    public Task<bool> ConfigureScreenshotAsync(int? key) => ConfigureExtra(screenshot, key);
    public Task<bool> ConfigurePauseAsync(int? key) => ConfigureExtra(pause, key);
    /// <summary>A keyboard key (not Esc or a modifier) that no other control or toggle uses; null clears it.</summary>
    private Task<bool> ConfigureExtra(GlobalKey slot, int? key) => QueueCommand(() =>
    {
        ProbeNative.UnregisterHotKey(0, slot.Id); slot.Key = null; slot.Trigger.Reset(); RefreshRegisteredKeys();
        if (key is null) return true;
        if (!Available || !GlobalKey.Allowed(key.Value) || key == toggleKey || key == stopKey || key == recordingKey
            || ExtraKeys.Any(e => e != slot && e.Key == key) || macroTriggers.Values.Any(m => m.Binding.Key == key)) return false;
        if (!RegisterShortcut(slot.Id, key.Value)) return false;
        slot.Trigger.Reset((ProbeNative.GetAsyncKeyState(key.Value) & 0x8000) != 0);
        slot.Key = key; RefreshRegisteredKeys(); return true;
    });
    public event Action? ToggleRequested;
    public event Action<Guid, TriggerCommand, bool>? MacroTriggered;
    public event Action<string>? StopRequested;
    public event Action? ActionStopRequested;
    public event Action<nint, uint>? WindowChanged;
    public event Action<InputControl, bool>? PhysicalChanged;
    internal event Action<PhysicalMouse>? MouseObserved;

    public DesktopControl()
    {
        screenshot = new(41, () => ScreenshotRequested?.Invoke());
        pause = new(42, () => PauseRequested?.Invoke());
        keyboardCallback = Keyboard;
        mouseCallback = Mouse;
        windowCallback = (_, evt, hwnd, obj, child, _, _) =>
        {
            try { if (obj == 0 && child == 0 && Available) WindowChanged?.Invoke(hwnd, evt); }
            catch (Exception ex) { FailControl(ex); }
        };
        thread = new Thread(MessageLoop) { IsBackground = true, Name = "MEC control" };
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        { Dispose(); throw new TimeoutException("Control thread did not initialize."); }
    }

    public bool IsPhysicalDown(InputControl input) { lock (physicalGate) return physicalDown.Contains(input); }
    public bool ModifiersDown => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(vk => (ProbeNative.GetAsyncKeyState(vk) & 0x8000) != 0);
    private static readonly (int Vk, int Scan)[] ModifierKeys = [
        (0xA0, 0x2A), (0xA1, 0x36), (0xA2, 0x1D), (0xA3, 0x11D), (0xA4, 0x38), (0xA5, 0x138), (0x5B, 0x15B), (0x5C, 0x15C)
    ];
    internal bool UnownedModifiersDown(Func<InputControl, bool> owns)
    {
        foreach (var key in ModifierKeys)
            if ((ProbeNative.GetAsyncKeyState(key.Vk) & 0x8000) != 0 && !owns(InputControl.Key(key.Scan))) return true;
        return false;
    }
    public void SampleBeforeRun(InputControl input)
    {
        lock (physicalGate)
        {
            int vk = DesktopNative.VirtualKey(input);
            if (vk != 0 && (ProbeNative.GetAsyncKeyState(vk) & 0x8000) != 0) physicalDown.Add(input);
            else physicalDown.Remove(input);
        }
    }

    /// <summary>Watch only the selected target's process for window destruction. A session-wide destroy hook
    /// delivers every destroyed control in every app to this thread, which must also service the input hooks in time.</summary>
    public Task<bool> WatchTargetAsync(uint processId) => QueueCommand(() =>
    {
        if (destroyHook != 0) { DesktopNative.UnhookWinEvent(destroyHook); destroyHook = 0; }
        if (processId == 0) return true;
        destroyHook = DesktopNative.SetWinEventHook(0x8001, 0x8001, 0, windowCallback, processId, 0, 0);
        return destroyHook != 0;
    });

    public Task<bool> ConfigureAsync(int toggle, int stop)
    {
        if (disposed) throw new ObjectDisposedException(nameof(DesktopControl));
        if (!ShortcutKeys.Supported(toggle) || !ShortcutKeys.Supported(stop) || toggle == stop)
            throw new ArgumentException("Choose different keys or mouse buttons for Start and Stop All.");
        return QueueCommand(() => Register(toggle, stop), invalidate: true);
    }

    private bool Register(int toggle, int stop)
    {
        ClearMacroBindings();
        ProbeNative.UnregisterHotKey(0, 40); recordingKey = null;
        foreach (var extra in ExtraKeys) { ProbeNative.UnregisterHotKey(0, extra.Id); extra.Key = null; extra.Trigger.Reset(); }
        ProbeNative.UnregisterHotKey(0, 1); ProbeNative.UnregisterHotKey(0, 2);
        toggleKey = toggle; stopKey = stop;
        trigger.Reset((ProbeNative.GetAsyncKeyState(toggleKey) & 0x8000) != 0);
        bool stopOk = RegisterShortcut(1, stop);
        bool toggleOk = stopOk && RegisterShortcut(2, toggle);
        RegisterBackupStop();
        Available = stopOk && toggleOk && keyboardHook != 0 && mouseHook != 0;
        Error = Available ? null : "A global hotkey or input safety hook is unavailable. Choose different keys and apply.";
        if (!Available) { ProbeNative.UnregisterHotKey(0, 1); ProbeNative.UnregisterHotKey(0, 2); }
        RefreshRegisteredKeys();
        return Available;
    }

    /// <summary>Another app holding Ctrl+Alt+F9 only costs the backup, never the shortcuts the user chose.</summary>
    private void RegisterBackupStop()
    {
        ProbeNative.UnregisterHotKey(0, BackupStopId);
        BackupStopActive = ShortcutKeys.UsesHook(stopKey) && ProbeNative.RegisterHotKey(0, BackupStopId, BackupStopModifiers, BackupStopVirtualKey);
    }

    /// <summary>Runs every second on the control thread: input the hooks never saw means Windows removed them.</summary>
    private void WatchHooks()
    {
        var info = new DesktopNative.LastInputInfo { Size = (uint)Marshal.SizeOf<DesktopNative.LastInputInfo>() };
        if (keyboardHook == 0 || mouseHook == 0 || !DesktopNative.GetLastInputInfo(ref info)) return;
        uint now = unchecked((uint)Environment.TickCount);
        if (!HookWatch.Missed(now, info.Time, lastKeyboardHook, lastMouseHook, lastHookReinstall)) return;
        lastHookReinstall = now;
        var module = DesktopNative.GetModuleHandle(null);
        nint keyboard = DesktopNative.SetWindowsHookExW(13, keyboardCallback, module, 0), mouse = DesktopNative.SetWindowsHookExW(14, mouseCallback, module, 0);
        if (keyboard == 0 || mouse == 0)
        {
            if (keyboard != 0) DesktopNative.UnhookWindowsHookEx(keyboard);
            if (mouse != 0) DesktopNative.UnhookWindowsHookEx(mouse);
            FailControl(new Win32Exception(Marshal.GetLastWin32Error(), "The input hooks could not be installed again."));
            return;
        }
        DesktopNative.UnhookWindowsHookEx(keyboardHook); DesktopNative.UnhookWindowsHookEx(mouseHook);
        keyboardHook = keyboard; mouseHook = mouse;
        lastKeyboardHook = lastMouseHook = now;
        HookReinstalls++;
        // Releases that happened while no hook listened were never seen; trust the key state Windows reports now.
        bool swapped = DesktopNative.GetSystemMetrics(23) != 0; // SM_SWAPBUTTON: the hook reports the buttons as used, the key state as pressed
        InputControl[] held; lock (physicalGate) held = [.. physicalDown];
        foreach (var input in held)
        {
            int vk = DesktopNative.VirtualKey(input);
            if (swapped && vk is 1 or 2) vk = 3 - vk;
            if (vk != 0 && (ProbeNative.GetAsyncKeyState(vk) & 0x8000) == 0) Observe(input, false);
        }
    }

    public Task<bool> ConfigureBindingsAsync(IReadOnlyList<MacroBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var copy = bindings.ToArray();
        MacroBindings.Validate(copy, toggleKey, stopKey);
        return QueueCommand(() =>
        {
            MacroBindings.Validate(copy, toggleKey, stopKey);
            ClearMacroBindings();
            bool success = Available;
            for (int i = 0; success && i < copy.Length; i++)
            {
                int id = i + 100; // Keep macro IDs separate from control/record/screenshot/pause IDs 1,2,40,41,42.
                // A toggle with only a controller button has no key to register; the controller route triggers it.
                if (!copy[i].HasKey) continue;
                success = recordingKey != copy[i].Key && !ExtraKeys.Any(e => e.Key == copy[i].Key) && RegisterShortcut(id, copy[i].Key);
                if (!success) break;
                var latch = new TriggerLatch();
                latch.Reset((ProbeNative.GetAsyncKeyState(copy[i].Key) & 0x8000) != 0);
                macroTriggers.Add(id, (copy[i], latch));
            }
            if (!success) ClearMacroBindings();
            RefreshRegisteredKeys();
            return success;
        });
    }
    private Task<bool> QueueCommand(Func<bool> apply, bool invalidate = false)
    {
        lock (commandGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (commandsEnded) return Task.FromException<bool>(new InvalidOperationException("Control thread is no longer available. Restart the application."));
            if (pendingCommands.Count >= 16) throw new InvalidOperationException("Hotkey settings are busy.");
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingCommands.Add(completion);
            if (invalidate) Available = false;
            commands.Enqueue(() =>
            {
                lock (commandGate)
                {
                    if (completion.Task.IsCompleted) return;
                    // The caller receives the failure through its task; rethrowing here would end the whole
                    // control thread and take the hooks and Stop All key with it.
                    try { completion.TrySetResult(apply()); }
                    catch (Exception ex) { completion.TrySetException(ex); }
                    finally { pendingCommands.Remove(completion); }
                }
            });
            if (!ProbeNative.PostThreadMessage(threadId, 0x8001, 0, 0)) EndCommands(new Win32Exception());
            return completion.Task;
        }
    }
    private void EndCommands(Exception error)
    {
        lock (commandGate)
        {
            commandsEnded = true;
            foreach (var pending in pendingCommands) pending.TrySetException(error);
            pendingCommands.Clear();
            commands.Clear();
        }
    }
    private void ClearMacroBindings()
    {
        foreach (int id in macroTriggers.Keys) ProbeNative.UnregisterHotKey(0, id);
        macroTriggers.Clear();
    }

    private void MessageLoop()
    {
        var hooks = new List<nint>();
        try
        {
            threadId = ProbeNative.GetCurrentThreadId();
            ProbeNative.PeekMessage(out _, 0, 0, 0, 0);
            if (disposed) return;
            var module = DesktopNative.GetModuleHandle(null);
            keyboardHook = DesktopNative.SetWindowsHookExW(13, keyboardCallback, module, 0);
            mouseHook = DesktopNative.SetWindowsHookExW(14, mouseCallback, module, 0);
            // Foreground and minimize events are global; the destroy event is scoped per target by WatchTargetAsync.
            foreach (uint evt in new uint[] { 3, 0x16 })
            {
                var hook = DesktopNative.SetWinEventHook(evt, evt, 0, windowCallback, 0, 0, 0);
                if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Foreground safety notifications unavailable.");
                hooks.Add(hook);
            }
            Register(toggleKey, stopKey);
            lastKeyboardHook = lastMouseHook = unchecked((uint)Environment.TickCount);
            DesktopNative.SetTimer(0, HookWatchTimer, 1000, 0);
            ready.Set();
            if (disposed) return;
            int status;
            while ((status = ProbeNative.GetMessage(out var message, 0, 0, 0)) > 0)
            {
                if (message.Id == 0x8001) while (commands.TryDequeue(out var command)) command();
                if (message.Id == 0x113) WatchHooks(); // WM_TIMER; a thread timer has no window, so it arrives here
                if (message.Id == 0x00FF) { ReadRawMouse(message.LParam); DesktopNative.DispatchMessageW(ref message); continue; } // WM_INPUT; the window's default handling frees it
                if (message.Id != 0x312 || bindingsSuspended) continue;
                if (message.WParam is 1 or BackupStopId)
                {
                    trigger.CancelPending();
                    foreach (var extra in ExtraKeys) extra.Trigger.CancelPending();
                    foreach (var macro in macroTriggers.Values) macro.Latch.CancelPending();
                    StopRequested?.Invoke(message.WParam == 1 ? "Stopped with global hotkey." : $"Stopped with {BackupStopLabel}.");
                }
                if (message.WParam == 40 && Available && recordingKey is not null) RecordingRequested?.Invoke();
                if (Available && ExtraKeys.FirstOrDefault(e => (nuint)e.Id == message.WParam && e.Key is not null) is { } pressed) pressed.Fire();
                if (message.WParam == 2 && Available)
                {
                    DispatchTrigger(trigger.RegisteredHotkey(HoldTrigger));
                }
                if (Available && macroTriggers.TryGetValue((int)message.WParam, out var item))
                    MacroTriggered?.Invoke(item.Binding.MacroId, item.Latch.RegisteredHotkey(item.Binding.HoldTrigger), item.Binding.HoldTrigger);
            }
            if (status < 0) StopRequested?.Invoke("Control message loop failed.");
        }
        catch (Exception ex) { FailControl(ex); }
        finally
        {
            EndCommands(new InvalidOperationException("Control thread ended before applying hotkey settings."));
            Available = false; ready.Set();
            ClearMacroBindings();
            ProbeNative.UnregisterHotKey(0, 40);
            ProbeNative.UnregisterHotKey(0, 41);
            ProbeNative.UnregisterHotKey(0, 1); ProbeNative.UnregisterHotKey(0, 2); ProbeNative.UnregisterHotKey(0, BackupStopId);
            DesktopNative.KillTimer(0, HookWatchTimer);
            foreach (var hook in hooks) DesktopNative.UnhookWinEvent(hook);
            if (destroyHook != 0) DesktopNative.UnhookWinEvent(destroyHook);
            if (keyboardHook != 0) DesktopNative.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != 0) DesktopNative.UnhookWindowsHookEx(mouseHook);
            if (rawWindow != 0) DesktopNative.DestroyWindow(rawWindow);
        }
    }

    // Look recording: raw mouse movement, the counts games read, goes to a message-only window while a recording runs.
    private nint rawWindow;
    private readonly byte[] rawBuffer = new byte[64];
    internal event Action<int, int>? RawMouseMoved;
    internal Task<bool> WatchRawMouseAsync(bool on) => QueueCommand(() =>
    {
        if (on && rawWindow == 0) rawWindow = DesktopNative.CreateWindowExW(0, "Message", null, 0, 0, 0, 0, 0, -3, 0, 0, 0); // HWND_MESSAGE
        if (rawWindow == 0) return false;
        // RIDEV_INPUTSINK: also while another app is in front; RIDEV_REMOVE when the recording ends.
        var device = new DesktopNative.RawInputDevice { UsagePage = 1, Usage = 2, Flags = on ? 0x100u : 0x1u, Target = on ? rawWindow : 0 };
        return DesktopNative.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<DesktopNative.RawInputDevice>());
    });
    private void ReadRawMouse(nint handle)
    {
        const int header = 24; // RAWINPUTHEADER on x64: type, size, device, wParam
        uint size = (uint)rawBuffer.Length;
        if (DesktopNative.GetRawInputData(handle, 0x10000003, rawBuffer, ref size, header) is uint.MaxValue || size < header + 24) return;
        // Only a real mouse: injected movement (this app's playback, other tools) has no device, absolute movement
        // (tablets, remote desktop) is not a camera turn.
        if (BitConverter.ToUInt32(rawBuffer, 0) != 0 || BitConverter.ToInt64(rawBuffer, 8) == 0 || (BitConverter.ToUInt16(rawBuffer, header) & 1) != 0) return;
        int dx = BitConverter.ToInt32(rawBuffer, header + 12), dy = BitConverter.ToInt32(rawBuffer, header + 16);
        if (dx == 0 && dy == 0) return;
        // A recording problem never takes the hotkeys down with it.
        try { RawMouseMoved?.Invoke(dx, dy); } catch (Exception) { }
    }

    private void Observe(InputControl input, bool down)
    {
        bool changed;
        lock (physicalGate) changed = down ? physicalDown.Add(input) : physicalDown.Remove(input);
        if (changed) PhysicalChanged?.Invoke(input, down); // Ignore OS typematic duplicates.
    }
    private nint Keyboard(int code, nuint message, nint data)
    {
        try { return KeyboardCore(code, message, data); }
        catch (Exception ex) { FailControl(ex); return DesktopNative.CallNextHookEx(keyboardHook, code, message, data); }
    }
    private nint KeyboardCore(int code, nuint message, nint data)
    {
        lastKeyboardHook = unchecked((uint)Environment.TickCount);
        if (code >= 0)
        {
            var evt = Marshal.PtrToStructure<DesktopNative.KeyboardEvent>(data);
            // The emergency route must also work while a recorded chord owns modifiers.
            // RegisterHotKey with no modifiers alone would miss Shift+F9 / Ctrl+F9.
            if (!bindingsSuspended && evt.Extra != ProbeNative.InputTag && evt.Key == stopKey && (evt.Flags & 0x80) == 0)
            {
                trigger.CancelPending();
                foreach (var extra in ExtraKeys) extra.Trigger.CancelPending();
                foreach (var macro in macroTriggers.Values) macro.Latch.CancelPending();
                StopRequested?.Invoke("Stopped with global hotkey.");
            }
            if ((evt.Flags & 0x10) == 0)
            {
                bool down = (evt.Flags & 0x80) == 0;
                Observe(InputControl.Key((int)evt.Scan | ((evt.Flags & 1) != 0 ? 0x100 : 0)), down);
            }
            if (!bindingsSuspended && evt.Extra != ProbeNative.InputTag)
                ShortcutEdge((int)evt.Key, (evt.Flags & 0x80) == 0);
        }
        return DesktopNative.CallNextHookEx(keyboardHook, code, message, data);
    }
    private TriggerCommand ShortcutEdge(TriggerLatch latch, int key, bool down, bool hold, bool targetRequired = true)
    {
        if (targetRequired && !down && !hold && ShortcutKeys.UsesHook(key) && TriggerAllowed?.Invoke() == false)
        { latch.KeyEdge(false, false); latch.CancelPending(); return TriggerCommand.None; }
        var command = latch.KeyEdge(down, hold);
        if (down && ShortcutKeys.UsesHook(key))
        {
            if (targetRequired && TriggerAllowed?.Invoke() == false) { latch.CancelPending(); return TriggerCommand.None; }
            command = latch.RegisteredHotkey(hold);
        }
        return command;
    }
    private void ShortcutEdge(int key, bool down)
    {
        if (key == toggleKey) DispatchTrigger(ShortcutEdge(trigger, key, down, HoldTrigger));
        if (recordingKey == key && ShortcutKeys.UsesHook(key) && ShortcutEdge(recordingTrigger, key, down, false) == TriggerCommand.Toggle) RecordingRequested?.Invoke();
        if (Available)
            foreach (var extra in ExtraKeys)
                if (extra.Key == key && ShortcutKeys.UsesHook(key) && ShortcutEdge(extra.Trigger, key, down, false, targetRequired: false) == TriggerCommand.Toggle) extra.Fire();
        if (Available)
                foreach (var item in macroTriggers.Values)
                    if (key == item.Binding.Key)
                        MacroTriggered?.Invoke(item.Binding.MacroId, ShortcutEdge(item.Latch, key, down, item.Binding.HoldTrigger), item.Binding.HoldTrigger);
    }
    /// <summary>
    /// A controller button bound to a global control does what that control's F-key does, through
    /// the same latch on the control thread. Stop All works even while shortcuts are being chosen, like F9.
    /// </summary>
    public void RunControllerCommand(ControllerCommand command)
    {
        try
        {
            _ = QueueCommand(() =>
            {
                if (command == ControllerCommand.StopAll)
                {
                    trigger.CancelPending(); foreach (var extra in ExtraKeys) extra.Trigger.CancelPending();
                    foreach (var macro in macroTriggers.Values) macro.Latch.CancelPending();
                    StopRequested?.Invoke("Stopped with the controller.");
                    return true;
                }
                if (!Available || bindingsSuspended) return false;
                if (command == ControllerCommand.StartStop) DispatchTrigger(trigger.RegisteredHotkey(HoldTrigger));
                else if (command == ControllerCommand.Record) RecordingRequested?.Invoke();
                else if (command == ControllerCommand.Screenshot) ScreenshotRequested?.Invoke();
                else if (command == ControllerCommand.Pause) PauseRequested?.Invoke();
                return true;
            });
        }
        // The control thread is gone or busy; the keyboard shortcuts report that themselves.
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
    }
    private void DispatchTrigger(TriggerCommand command)
    {
        if (command == TriggerCommand.Toggle) ToggleRequested?.Invoke();
        else if (command == TriggerCommand.Stop) ActionStopRequested?.Invoke();
    }
    private nint Mouse(int code, nuint message, nint data)
    {
        try { return MouseCore(code, message, data); }
        catch (Exception ex) { FailControl(ex); return DesktopNative.CallNextHookEx(mouseHook, code, message, data); }
    }
    private nint MouseCore(int code, nuint message, nint data)
    {
        lastMouseHook = unchecked((uint)Environment.TickCount);
        if (code >= 0)
        {
            var evt = Marshal.PtrToStructure<DesktopNative.MouseEvent>(data);
            PointerButton? shortcutButton = message switch { 0x201 or 0x202 => PointerButton.Left, 0x204 or 0x205 => PointerButton.Right,
                0x207 or 0x208 => PointerButton.Middle, 0x20B or 0x20C => (evt.Data >> 16) == 1 ? PointerButton.XButton1 : PointerButton.XButton2, _ => null };
            if (!bindingsSuspended && evt.Extra != ProbeNative.InputTag && shortcutButton is { } shortcut)
            {
                int key = ShortcutKeys.MouseKey(shortcut);
                bool down = message is 0x201 or 0x204 or 0x207 or 0x20B;
                if (key == stopKey && down)
                { trigger.CancelPending(); foreach (var extra in ExtraKeys) extra.Trigger.CancelPending(); foreach (var item in macroTriggers.Values) item.Latch.CancelPending(); StopRequested?.Invoke("Stopped with mouse shortcut."); }
                ShortcutEdge(key, down);
            }
            if ((evt.Flags & 1) == 0)
            {
                PointerButton? button = message switch { 0x201 or 0x202 => PointerButton.Left, 0x204 or 0x205 => PointerButton.Right,
                    0x207 or 0x208 => PointerButton.Middle, 0x20B or 0x20C => (evt.Data >> 16) == 1 ? PointerButton.XButton1 : PointerButton.XButton2, _ => null };
                if (button is { } value)
                {
                    bool down = message is 0x201 or 0x204 or 0x207 or 0x20B;
                    var input = InputControl.Mouse(value);
                    Observe(input, down);
                    MouseObserved?.Invoke(new(input, down, evt.Point.X, evt.Point.Y));
                }
                else if (message is 0x20A or 0x20E) MouseObserved?.Invoke(new(new(message == 0x20A ? InputKind.Scroll : InputKind.HorizontalScroll, unchecked((short)(evt.Data >> 16))), true, evt.Point.X, evt.Point.Y));
                else if (message == 0x200) MouseObserved?.Invoke(new(new(InputKind.PointerMove, 0), true, evt.Point.X, evt.Point.Y));
            }
        }
        return DesktopNative.CallNextHookEx(mouseHook, code, message, data);
    }
    // Never unwind a managed subscriber exception through a native callback boundary.
    // Notify every stop subscriber even if another subscriber fails, then retire the route.
    private void FailControl(Exception error)
    {
        Available = false;
        Error = "Input controls failed (" + error.GetType().Name + "). Restart the application.";
        foreach (var handler in StopRequested?.GetInvocationList() ?? [])
        {
            try { ((Action<string>)handler)("Control thread failed. Playback stopped; restart the application."); }
            catch (Exception) { /* One observer cannot prevent the remaining cleanup observers. */ }
        }
        ProbeNative.PostThreadMessage(threadId, 0x12, 0, 0);
    }
    public void Dispose()
    {
        lock (commandGate)
        {
            if (disposed) return;
            disposed = true;
            EndCommands(new ObjectDisposedException(nameof(DesktopControl)));
        }
        ProbeNative.PostThreadMessage(threadId, 0x12, 0, 0);
        if (thread.Join(TimeSpan.FromSeconds(3))) ready.Dispose();
    }
}
