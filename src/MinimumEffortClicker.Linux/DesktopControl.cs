using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Linux;

internal readonly record struct PhysicalMouse(InputControl Control, bool Down, int X, int Y);

/// <summary>
/// Global controls for Linux. Physical keys and buttons come from evdev when this account may read
/// them (full press/release, mouse shortcuts, physical-input safety and recording). Without evdev,
/// keyboard shortcuts fall back to GNOME accelerators through the helper. No key is ever suppressed
/// except the F1–F11 shortcuts GNOME grabs for us, matching the Windows registered-hotkey route.
/// </summary>
public sealed class DesktopControl : IDisposable
{
    private readonly object gate = new();
    private readonly TriggerLatch trigger = new();
    private readonly TriggerLatch recordingTrigger = new();
    private readonly Dictionary<int, (MacroBinding Binding, TriggerLatch Latch)> macroTriggers = [];
    private readonly Dictionary<uint, int> grabbed = [];
    private int toggleKey = ShortcutKeys.DefaultStartKey, stopKey = ShortcutKeys.DefaultStopKey;
    private int? recordingKey;
    // Screenshot and Pause: global keys beside Start, Stop All and Record, handled alike (Engine GlobalKey).
    private readonly GlobalKey screenshot, pause;
    private GlobalKey[] ExtraKeys => [screenshot, pause];
    private volatile bool bindingsSuspended, disposed;
    private volatile int[] controlKeys = [];
    private EvdevMonitor? evdev;
    public IDesktopHelper? Helper { get; private set; }
    public string? HelperError { get; private set; }
    public bool HelperAccessDenied { get; private set; }
    public bool PhysicalInputAvailable => evdev is not null;
    public bool Available { get; private set; }
    public string? Error { get; private set; }
    public bool HoldTrigger { get; set; }
    public bool ScreenshotAvailable => screenshot.Key is not null && Available && !bindingsSuspended;
    public Func<bool>? TriggerAllowed { get; set; }

    public event Action? ToggleRequested;
    public event Action<Guid, TriggerCommand, bool>? MacroTriggered;
    public event Action<string>? StopRequested;
    public event Action? ActionStopRequested;
    public event Action? RecordingRequested;
    public event Action? ScreenshotRequested;
    /// <summary>The Pause key: pauses the running Action List, or goes on with a paused one.</summary>
    public event Action? PauseRequested;
    public event Action<ulong, uint>? WindowChanged;
    public event Action<InputControl, bool>? PhysicalChanged;
    internal event Action<PhysicalMouse>? MouseObserved;
    /// <summary>A mouse's relative counts (horizontal, vertical), for Look recording.</summary>
    internal event Action<int, int>? RawMouseMoved;
    /// <summary>The pointer position as last asked from the desktop helper, at most every 8 ms.</summary>
    internal (int X, int Y) PointerNow() => Pointer();
    public event Action? PlatformChanged;

    public DesktopControl()
    {
        screenshot = new(0, () => ScreenshotRequested?.Invoke());
        pause = new(0, () => PauseRequested?.Invoke());
        ConnectPlatform();
        Register(toggleKey, stopKey);
    }

    /// <summary>Retries the helper and evdev, for example after running the input-access setup or removing it.</summary>
    public Task<bool> RefreshPlatformAsync() => Task.Run(() =>
    {
        lock (gate) { if (disposed) return false; }
        ConnectPlatform();
        bool result;
        lock (gate) result = Reapply();
        PlatformChanged?.Invoke();
        return result;
    });

    private void ConnectPlatform()
    {
        if (Helper is null)
        {
            try
            {
                var helper = DesktopHelper.Connect();
                helper.FocusChanged += id => { if (Available) WindowChanged?.Invoke(id, 3); };
                helper.WindowClosed += id => WindowChanged?.Invoke(id, 0x8001);
                helper.AcceleratorChanged += OnAccelerator;
                Helper = helper; HelperError = null; HelperAccessDenied = false;
            }
            catch (HelperException ex) { HelperError = ex.Message; HelperAccessDenied = ex.AccessDenied; }
        }
        // Access was removed (setup-input-access.sh --remove): devices already open would keep working until a restart.
        // Let them go, so the app does what the user chose and Settings shows it.
        if (evdev is { } current && !EvdevMonitor.KeyboardAccessible()) { evdev = null; current.Dispose(); }
        if (evdev is null && EvdevMonitor.KeyboardAccessible())
        {
            try
            {
                var monitor = new EvdevMonitor();
                monitor.Observed += OnPhysical;
                monitor.Failed += error => Fail(error);
                evdev = monitor;
            }
            catch (IOException) { evdev = null; }
        }
    }

    public string PlatformSummary => (Helper, evdev) switch
    {
        (not null, not null) => LinuxSetup.HelperName + " and physical input access are ready.",
        (not null, null) => LinuxSetup.HelperName + " ready. Physical input access is off, so recording, mouse-button shortcuts and the physical-input safety stop are unavailable.",
        (null, not null) => "Physical input access is ready, but the " + LinuxSetup.HelperName + " is missing: " + HelperError,
        _ => "The " + LinuxSetup.HelperName + " is missing (" + HelperError + ") and physical input access is off."
    };

    private int[] Pressed => evdev?.Down ?? [];
    public bool IsPhysicalDown(InputControl input)
    {
        var monitor = evdev;
        if (monitor is null) return false;
        int code = input.Kind switch
        {
            InputKind.ScanCode => LinuxKeys.EvdevFromScanCode(input.Code),
            InputKind.MouseButton => LinuxKeys.EvdevButton((PointerButton)input.Code),
            _ => 0
        };
        return code != 0 && monitor.IsDown(code);
    }
    public bool ModifiersDown => Pressed.Any(LinuxKeys.IsModifier);
    internal bool UnownedModifiersDown(Func<InputControl, bool> owns)
        => Pressed.Where(LinuxKeys.IsModifier).Any(code => LinuxKeys.ScanCodeFromEvdev(code) is not { } scan || !owns(InputControl.Key(scan)));
    /// <summary>Evdev state is live, so there is nothing to sample; kept for runtime parity with Windows.</summary>
    public void SampleBeforeRun(InputControl input) { }

    internal static int VirtualKey(InputControl input) => input.Kind switch
    {
        InputKind.MouseButton => ShortcutKeys.MouseKey((PointerButton)input.Code),
        InputKind.ScanCode => LinuxKeys.EvdevFromScanCode(input.Code) is int code and not 0 ? LinuxKeys.VirtualKeyFromEvdev(code) ?? 0 : 0,
        _ => 0
    };
    public bool IsShortcutInput(InputControl input) => controlKeys.Contains(VirtualKey(input));
    /// <summary>Keys GNOME consumes for us (F1–F11) cannot also be pressed by playback.</summary>
    public bool UsesRegisteredInput(InputControl input) { int key = VirtualKey(input); return controlKeys.Contains(key) && !ShortcutKeys.UsesHook(key); }

    public Task<bool> ConfigureAsync(int toggle, int stop)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ShortcutKeys.Supported(toggle) || !ShortcutKeys.Supported(stop) || toggle == stop)
            throw new ArgumentException("Choose different keys or mouse buttons for Start and Stop All.");
        lock (gate) return Task.FromResult(Register(toggle, stop));
    }
    public Task<bool> ConfigureBindingsAsync(IReadOnlyList<MacroBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var copy = bindings.ToArray();
        lock (gate)
        {
            MacroBindings.Validate(copy, toggleKey, stopKey);
            macroTriggers.Clear();
            bool success = Available;
            for (int i = 0; success && i < copy.Length; i++)
            {
                // A toggle with only a controller button has no key to grab; the controller route triggers it.
                if (!copy[i].HasKey) continue;
                success = recordingKey != copy[i].Key && !ExtraKeys.Any(e => e.Key == copy[i].Key) && CanUse(copy[i].Key);
                if (!success) break;
                var latch = new TriggerLatch(); latch.Reset(IsKeyDown(copy[i].Key));
                macroTriggers.Add(i + 100, (copy[i], latch));
            }
            if (!success) macroTriggers.Clear();
            Regrab();
            // Without evdev, a GNOME accelerator is the only route: a key GNOME already uses cannot work.
            if (success && evdev is null && copy.Any(b => b.HasKey && !grabbed.ContainsValue(b.Key)))
            { macroTriggers.Clear(); Regrab(); success = false; }
            return Task.FromResult(success);
        }
    }
    public Task<bool> ConfigureRecordingAsync(int? key)
    {
        lock (gate)
        {
            recordingKey = null;
            if (key is null) { Regrab(); return Task.FromResult(true); }
            if (!Available || !ShortcutKeys.Supported(key.Value) || key == toggleKey || key == stopKey || ExtraKeys.Any(e => e.Key == key)
                || macroTriggers.Values.Any(m => m.Binding.Key == key) || !CanUse(key.Value)) { Regrab(); return Task.FromResult(false); }
            recordingKey = key; recordingTrigger.Reset(IsKeyDown(key.Value));
            Regrab();
            if (evdev is null && !grabbed.ContainsValue(key.Value)) { recordingKey = null; Regrab(); return Task.FromResult(false); }
            return Task.FromResult(true);
        }
    }
    public Task<bool> ConfigureScreenshotAsync(int? key) => ConfigureExtra(screenshot, key);
    public Task<bool> ConfigurePauseAsync(int? key) => ConfigureExtra(pause, key);
    /// <summary>A keyboard key (not Esc or a modifier) that no other control or toggle uses; null clears it.</summary>
    private Task<bool> ConfigureExtra(GlobalKey slot, int? key)
    {
        lock (gate)
        {
            slot.Key = null; slot.Trigger.Reset();
            if (key is null) { Regrab(); return Task.FromResult(true); }
            if (!Available || !GlobalKey.Allowed(key.Value) || key == toggleKey || key == stopKey || key == recordingKey
                || ExtraKeys.Any(e => e != slot && e.Key == key)
                || macroTriggers.Values.Any(m => m.Binding.Key == key) || !CanUse(key.Value)) { Regrab(); return Task.FromResult(false); }
            slot.Key = key; slot.Trigger.Reset(IsKeyDown(key.Value));
            Regrab();
            if (evdev is null && !grabbed.ContainsValue(key.Value)) { slot.Key = null; Regrab(); return Task.FromResult(false); }
            return Task.FromResult(true);
        }
    }
    public Task<bool> SuspendBindingsAsync(bool suspended)
    {
        lock (gate)
        {
            bindingsSuspended = suspended;
            trigger.Reset(IsKeyDown(toggleKey)); recordingTrigger.Reset();
            foreach (var extra in ExtraKeys) extra.Trigger.Reset(extra.Key is { } key && IsKeyDown(key));
            foreach (var item in macroTriggers.Values) item.Latch.Reset(IsKeyDown(item.Binding.Key));
            if (suspended) { Ungrab(); return Task.FromResult(true); }
            return Task.FromResult(Reapply());
        }
    }

    private bool IsKeyDown(int virtualKey) => LinuxKeys.EvdevFromVirtualKey(virtualKey) is int code and not 0 && evdev?.IsDown(code) == true;
    /// <summary>Evdev hears every key and button; the GNOME route can only grab keyboard accelerators.</summary>
    private bool CanUse(int key) => evdev is not null || Helper is not null && LinuxKeys.Accelerator(key) is not null;

    private bool Register(int toggle, int stop)
    {
        macroTriggers.Clear(); recordingKey = null;
        foreach (var extra in ExtraKeys) { extra.Key = null; extra.Trigger.Reset(); }
        toggleKey = toggle; stopKey = stop;
        trigger.Reset(IsKeyDown(toggleKey));
        return Reapply();
    }
    private bool Reapply()
    {
        Regrab();
        bool routes = evdev is not null || Helper is not null && grabbed.ContainsValue(stopKey) && grabbed.ContainsValue(toggleKey);
        Available = routes && CanUse(stopKey) && CanUse(toggleKey) && Helper is not null;
        Error = Available ? null
            : Helper is null ? HelperError ?? "The ME Clicker " + LinuxSetup.HelperName + " is not available."
            : evdev is null && (!CanUse(stopKey) || !CanUse(toggleKey)) ? "Mouse-button shortcuts need physical input access. Choose keyboard keys, or enable input access in Settings."
            : "A global shortcut is already used by the desktop or another app. Choose different keys and apply.";
        return Available;
    }

    private void RefreshControlKeys()
    {
        controlKeys = macroTriggers.Values.Select(m => m.Binding.Key).Append(toggleKey).Append(stopKey)
            .Concat(recordingKey is { } record ? new[] { record } : [])
            .Concat(ExtraKeys.Where(e => e.Key is not null).Select(e => e.Key!.Value)).Distinct().ToArray();
    }

    /// <summary>
    /// Grab F1–F11 shortcuts so they do not also reach the focused app (Windows parity). Without evdev,
    /// grab every keyboard shortcut because the GNOME accelerator is then the only route.
    /// </summary>
    private void Regrab()
    {
        RefreshControlKeys();
        Ungrab();
        var helper = Helper;
        if (helper is null || bindingsSuspended) return;
        foreach (int key in controlKeys)
        {
            if (evdev is not null && ShortcutKeys.UsesHook(key)) continue;
            if (LinuxKeys.Accelerator(key) is not { } accelerator) continue;
            try { grabbed[helper.GrabAccelerator(accelerator)] = key; }
            catch (HelperException) { /* Already used elsewhere; Reapply reports a missing control route. */ }
        }
    }
    private void Ungrab()
    {
        var helper = Helper;
        foreach (uint action in grabbed.Keys)
        {
            try { helper?.UngrabAccelerator(action); } catch (HelperException) { }
        }
        grabbed.Clear();
    }

    private void OnAccelerator(uint action, bool pressed)
    {
        if (evdev is not null) return; // Evdev already reported this physical key.
        int key;
        lock (gate) { if (!grabbed.TryGetValue(action, out key)) return; }
        ShortcutEdge(key, pressed);
    }

    private void OnPhysical(EvdevEvent sample)
    {
        try
        {
            switch (sample.Kind)
            {
                case EvdevEventKind.Key:
                    bool down = sample.Value != 0;
                    if (LinuxKeys.ButtonFromEvdev(sample.Code) is { } button)
                    {
                        var input = InputControl.Mouse(button);
                        PhysicalChanged?.Invoke(input, down);
                        var (x, y) = Pointer(exact: true);
                        MouseObserved?.Invoke(new(input, down, x, y));
                    }
                    else if (LinuxKeys.ScanCodeFromEvdev(sample.Code) is { } scan) PhysicalChanged?.Invoke(InputControl.Key(scan), down);
                    if (LinuxKeys.VirtualKeyFromEvdev(sample.Code) is { } key && !bindingsSuspended)
                    {
                        if (key == stopKey && down) RequestStop("Stopped with global hotkey.");
                        ShortcutEdge(key, down);
                    }
                    break;
                case EvdevEventKind.Motion or EvdevEventKind.RelativeMotion:
                    // A mouse's own counts also go to a Look recording; a touchpad's positions are no camera turn.
                    if (sample.Kind == EvdevEventKind.RelativeMotion && RawMouseMoved is { } look) look(sample.Code == 0 ? sample.Value : 0, sample.Code == 0 ? 0 : sample.Value);
                    if (MouseObserved is { } moved) { var (x, y) = Pointer(); moved(new(new(InputKind.PointerMove, 0), true, x, y)); }
                    break;
                case EvdevEventKind.Wheel or EvdevEventKind.HorizontalWheel:
                    if (MouseObserved is { } wheel)
                    {
                        var (x, y) = Pointer();
                        wheel(new(new(sample.Kind == EvdevEventKind.Wheel ? InputKind.Scroll : InputKind.HorizontalScroll, sample.Value * 120), true, x, y));
                    }
                    break;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    private long lastPointerQuery;
    private (int X, int Y) lastPointer;
    private (int X, int Y) Pointer(bool exact = false)
    {
        // Physical motion can arrive at 1 kHz; one compositor query per 8 ms is plenty for safety and drags.
        // Button edges always ask, so a recorded click keeps its exact position.
        long now = Environment.TickCount64;
        if (!exact && now - Interlocked.Read(ref lastPointerQuery) < 8) return lastPointer;
        try { lastPointer = Helper?.GetPointer() ?? lastPointer; } catch (HelperException) { }
        Interlocked.Exchange(ref lastPointerQuery, now);
        return lastPointer;
    }

    private void RequestStop(string reason)
    {
        lock (gate)
        {
            trigger.CancelPending();
            foreach (var extra in ExtraKeys) extra.Trigger.CancelPending();
            foreach (var macro in macroTriggers.Values) macro.Latch.CancelPending();
        }
        StopRequested?.Invoke(reason);
    }

    private TriggerCommand Edge(TriggerLatch latch, bool down, bool hold, bool targetRequired = true)
    {
        if (targetRequired && !down && !hold && TriggerAllowed?.Invoke() == false)
        { latch.KeyEdge(false, false); latch.CancelPending(); return TriggerCommand.None; }
        var command = latch.KeyEdge(down, hold);
        if (down)
        {
            if (targetRequired && TriggerAllowed?.Invoke() == false) { latch.CancelPending(); return TriggerCommand.None; }
            command = latch.RegisteredHotkey(hold);
        }
        return command;
    }
    /// <summary>
    /// A controller button bound to a global control does what that control's F-key does, through
    /// the same latch. Stop All works even while shortcuts are being chosen.
    /// </summary>
    public void RunControllerCommand(ControllerCommand command)
    {
        if (command == ControllerCommand.StopAll) { RequestStop("Stopped with the controller."); return; }
        TriggerCommand toggle = TriggerCommand.None;
        lock (gate)
        {
            if (!Available || bindingsSuspended) return;
            // A controller press is a whole tap. The key latch only fires after a physical key-down of the Start key, so it
            // would drop every controller press (L4 on a Steam Deck did nothing, 2026-10-19).
            if (command == ControllerCommand.StartStop) { trigger.CancelPending(); toggle = TriggerCommand.Toggle; }
        }
        if (toggle == TriggerCommand.Toggle) ToggleRequested?.Invoke();
        else if (toggle == TriggerCommand.Stop) ActionStopRequested?.Invoke();
        if (command == ControllerCommand.Record) RecordingRequested?.Invoke();
        if (command == ControllerCommand.Screenshot) ScreenshotRequested?.Invoke();
        if (command == ControllerCommand.Pause) PauseRequested?.Invoke();
    }
    private void ShortcutEdge(int key, bool down)
    {
        if (bindingsSuspended) return;
        TriggerCommand toggle = TriggerCommand.None;
        bool record = false;
        var extras = new List<GlobalKey>();
        var macros = new List<(Guid Id, TriggerCommand Command, bool Hold)>();
        bool available;
        lock (gate)
        {
            available = Available;
            if (!available) return;
            // F1–F11 act like Windows registered hotkeys: they work whichever window is in front.
            // Other keys follow the Windows hook route and only trigger while the target is.
            bool targetRequired = ShortcutKeys.UsesHook(key);
            if (key == toggleKey) toggle = Edge(trigger, down, HoldTrigger, targetRequired);
            if (recordingKey == key) record = Edge(recordingTrigger, down, false, targetRequired) == TriggerCommand.Toggle;
            foreach (var extra in ExtraKeys)
                if (extra.Key == key && Edge(extra.Trigger, down, false, targetRequired: false) == TriggerCommand.Toggle) extras.Add(extra);
            foreach (var item in macroTriggers.Values)
                if (key == item.Binding.Key) macros.Add((item.Binding.MacroId, Edge(item.Latch, down, item.Binding.HoldTrigger, targetRequired), item.Binding.HoldTrigger));
        }
        if (evdev is null && key == stopKey && down) RequestStop("Stopped with global hotkey.");
        if (toggle == TriggerCommand.Toggle) ToggleRequested?.Invoke();
        else if (toggle == TriggerCommand.Stop) ActionStopRequested?.Invoke();
        if (record) RecordingRequested?.Invoke();
        foreach (var extra in extras) extra.Fire();
        foreach (var (id, command, hold) in macros) MacroTriggered?.Invoke(id, command, hold);
    }

    private void Fail(Exception error)
    {
        Available = false;
        Error = "Input controls failed (" + error.GetType().Name + "). Restart the application.";
        foreach (var handler in StopRequested?.GetInvocationList() ?? [])
        {
            try { ((Action<string>)handler)("Control route failed. Playback stopped; restart the application."); }
            catch (Exception) { /* One observer cannot prevent the remaining cleanup observers. */ }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            Ungrab();
        }
        evdev?.Dispose();
        try { Helper?.ReleaseAll(); } catch (HelperException) { }
        Helper?.Dispose();
    }
}
