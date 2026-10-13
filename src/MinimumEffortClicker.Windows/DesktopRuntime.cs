using System.Diagnostics;
using System.Text;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Windows;

public sealed record TargetWindow(nint Handle, int ProcessId, string Title)
{
    public override string ToString() => Title;
}
public sealed record RuntimeSnapshot(string State, string Reason, long InputsSent, int CompletedCycles,
    TimeSpan Elapsed, bool Busy, bool CleanupPending, string CurrentAction = "", string NextAction = "", string HeldInputs = "None", string Activity = "Minimum Effort", long TotalCompletedLoops = 0, TimeSpan TotalActiveTime = default, bool PlaybackActive = false, IReadOnlyList<Guid>? ActiveHelperIds = null, bool ActionsActive = false, long TotalCompletedClicks = 0);
public sealed record TargetClientFrame(int X, int Y, int Width, int Height, int Dpi);
/// <summary>A saved toggle armed on its shortcut. Arming never starts anything; the shortcut press does.</summary>
public sealed record ArmedHelper(MacroBinding Binding, HelperPlan Plan, string DisplayName);

/// <summary>One scheduler worker and one independent control thread. Starts only by explicit command.</summary>
public sealed partial class DesktopRuntime : IInputSink, IForegroundTargetProvider, IDisposable
{
    private readonly object gate = new();
    private readonly AutoResetEvent workReady = new(false);
    private readonly Thread worker;
    private readonly ClickExecutor executor;
    private readonly WindowsDeadlineWait schedulerWait;
    private readonly Func<InputControl, bool> ownsInput;
    private readonly DeferredCleanupRetry cleanupRetry;
    private readonly StopwatchClock clock = new();
    private CancellationTokenSource? cancellation;
    private Task cancellationCallbacks = Task.CompletedTask;
    private sealed record RunRequest(HelperPlan? Helper, SequencePlan? Sequence, string? Label = null, NamedHelper? ToggleHold = null)
    { public bool LeaveCursorFree => Helper?.LeaveCursorFree == true || Sequence?.LeaveCursorFree == true; }
    private RunRequest? pending;
    private RunRequest? runningRequest;
    private PlaybackControl? playbackControl;
    public bool CanResume { get { lock (gate) return playbackControl?.CanResume == true; } }
    private bool PlaybackIsPaused { get { lock (gate) return playbackControl?.IsPaused == true; } }
    private string activity = "Minimum Effort";
    private string? inputRejection;
    private bool RejectInput(string message) { inputRejection = message; return false; }
    private ActionToggleSession? actionToggleSession;
    public bool CanStartActions { get { lock (gate) return !recording && (!busy ? !executor.HasOwnedInput : actionToggleSession is { ActionBusy: false } && playbackControl?.IsPaused != true); } }
    public bool ActionsBusy { get { lock (gate) return actionToggleSession?.ActionBusy ?? busy; } }
    private void ValidateJoiningSequence(SequencePlan plan)
    {
        if (plan.LeaveCursorFree || CursorFreeActive) throw new ArgumentException("Stop active toggles before starting cursor-free Actions.");
        if (!Control.Available || ShutdownPending || !CoordinatesAvailable(plan)) throw new ArgumentException("Restore the target and its saved size before starting Actions.");
        if (plan.KeyboardLayout is { } layout && !string.Equals(layout, TargetKeyboardLayout, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The target keyboard layout differs from this recording.");
        if (Control.UnownedModifiersDown(ownsInput)) throw new ArgumentException("Release physical modifiers before starting Actions.");
        foreach (var control in plan.Inputs.Select(i => i.Control).Distinct())
        { if (Control.UsesRegisteredInput(control)) throw new ArgumentException("An action presses a registered function-key shortcut. Change that shortcut or action first."); Control.SampleBeforeRun(control); if (Control.IsPhysicalDown(control)) throw new ArgumentException("Release the action keys/buttons before starting."); }
    }
    private InputControl[] activeControls = [];
    private volatile string? activeLayout;
    private volatile TargetWindow? target;
    private volatile bool targetInvalidated;
    private Process? targetProcess;
    private Guid targetId = Guid.NewGuid();
    private bool busy, disposed;
    private bool recording;
    private bool previewing;
    private Action<string>? captureStop;
    private Action<string>? capturePause;
    private string state = "Stopped", reason = "Desktop mode. Choose an action or select a Click target. Nothing is running.";
    private long inputsSent;
    private long nativeDispatchTicks, nativeDispatchCount, longestNativeDispatchTicks;
    public (long Count, double TotalMilliseconds, double MaximumMilliseconds) NativeDispatchTiming =>
        (Interlocked.Read(ref nativeDispatchCount), Interlocked.Read(ref nativeDispatchTicks) * 1000d / Stopwatch.Frequency,
            Interlocked.Read(ref longestNativeDispatchTicks) * 1000d / Stopwatch.Frequency);
    private int completed;
    private long started;
    private TimeSpan elapsed;
    private TimeSpan activeAtStart;
    private TimeSpan startDelay;
    private volatile bool countingDown;
    public DesktopControl Control { get; }
    public nint OverlayHandle { get; set; }
    public event Action? Changed;
    public event Action? StopIssued;
    public event Action? QuickStartRequested;
    private long stopVersion;
    public long StopVersion => Interlocked.Read(ref stopVersion);
    private long notificationFailures;
    public long NotificationFailures => Interlocked.Read(ref notificationFailures);
    private void NotifyChanged()
    {
        foreach (var observer in Changed?.GetInvocationList() ?? [])
        {
            try { ((Action)observer)(); }
            catch (Exception) { Interlocked.Increment(ref notificationFailures); }
        }
    }
    public bool ShutdownPending { get; set; }
    public Func<CancellationToken, Task>? CaptureScreenshot { set => executor.CaptureScreenshot = value; }
    public long ClickCompletionLimit { get => executor.ClickCompletionLimit; set => executor.ClickCompletionLimit = value; }
    public long CompletionLimit { get => executor.CompletionLimit; set => executor.CompletionLimit = value; }
    public TimeSpan ActiveTimeLimit { get => executor.ActiveTimeLimit; set => executor.ActiveTimeLimit = value; }
    public bool KeepAwakeWhileRunning { get; set; }
    private ArmedHelper[] armedHelpers = [];
    private Guid runningMacro;
    private volatile bool quickSequenceArmed;
    public bool QuickSequenceArmed { get => quickSequenceArmed; set => quickSequenceArmed = value; }

    public DesktopRuntime()
    {
        schedulerWait = new(clock);
        executor = new(this, clock, schedulerWait, this);
        executor.EvaluateCondition = (condition, stop) => EvaluateCondition(condition, stop);
        executor.PrepareClickPoint = (point, motion, random) =>
        {
            var frame = GetTargetFrame();
            var resolved = point.InFrame(frame.Width, frame.Height, frame.Dpi);
            return motion.Offset(resolved, random, resolved.Space == CoordinateSpace.Desktop ? frame.X : 0,
                resolved.Space == CoordinateSpace.Desktop ? frame.Y : 0);
        };
        executor.CursorPosition = point =>
        {
            var frame = GetTargetFrame();
            if (!ProbeNative.GetCursorPos(out var pointer)) throw new InvalidOperationException("Cursor position is unavailable.");
            // Movement stays inside the target frame. Entering from outside uses the usual move.
            if (pointer.X < frame.X || pointer.Y < frame.Y || pointer.X >= frame.X + frame.Width || pointer.Y >= frame.Y + frame.Height) return null;
            return point with { X = point.Space == CoordinateSpace.Desktop ? pointer.X : pointer.X - frame.X,
                Y = point.Space == CoordinateSpace.Desktop ? pointer.Y : pointer.Y - frame.Y };
        };
        ownsInput = executor.Owns;
        cleanupRetry = new(action => ThreadPool.QueueUserWorkItem(_ => action()), RecoverDeferredCleanup);
        try { Control = new(); }
        catch { schedulerWait.Dispose(); throw; }
        Control.StopRequested += Stop;
        Control.ActionStopRequested += StopActions;
        Control.TriggerAllowed = () => TargetAvailable;
        Control.ToggleRequested += Toggle;
        Control.MacroTriggered += MacroTriggered;
        Control.WindowChanged += OnWindowChanged;
        Control.PhysicalChanged += OnPhysical;
        Control.MouseObserved += sample =>
        {
            if (!observingWindowActive && Volatile.Read(ref activeControls).Any(c => c.Kind == InputKind.PointerMove)) Stop("Stopped: physical pointer input takes priority.");
        };
        worker = new Thread(Worker) { IsBackground = true, Name = "MEC scheduler" };
        worker.Start();
    }

    public static IReadOnlyList<TargetWindow> ListTargets()
    {
        var result = new List<TargetWindow>();
        DesktopNative.EnumWindows((hwnd, _) =>
        {
            if (!DesktopNative.IsWindowVisible(hwnd)) return true;
            ProbeNative.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId || pid == 0) return true;
            var title = new StringBuilder(512);
            DesktopNative.GetWindowText(hwnd, title, title.Capacity);
            if (title.Length > 0) result.Add(new(hwnd, (int)pid, title.ToString()));
            return true;
        }, 0);
        return result.OrderBy(x => x.Title).ToArray();
    }

    public void SelectTarget(TargetWindow selected)
    {
        lock (gate)
        {
            if (busy || recording || executor.HasOwnedInput) throw new InvalidOperationException("Stop and release owned input before changing the target.");
            if (!ProbeNative.IsWindow(selected.Handle)
                || ProbeNative.GetWindowThreadProcessId(selected.Handle, out uint pid) == 0 || pid != selected.ProcessId)
                throw new InvalidOperationException("The window closed. Open the list and choose again.");
            var process = Process.GetProcessById(selected.ProcessId);
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => { if (ReferenceEquals(targetProcess, process)) InvalidateTarget(); };
                if (process.HasExited) throw new InvalidOperationException("The window closed. Open the list and choose again.");
            }
            catch { process.Dispose(); throw; }
            targetProcess?.Dispose();
            targetProcess = process;
            target = selected;
            targetInvalidated = false;
            targetId = Guid.NewGuid();
            state = "Ready"; reason = "Click target selected. Press Start, or use the start / stop hotkey while that window is active.";
        }
        NotifyChanged();
    }

    public bool HasSelectedTarget => target is not null && !targetInvalidated;
    public TargetWindow? SelectedWindow => target;
    public bool IsDesktopMode => target is null && !targetInvalidated;
    public void UseDesktop()
    {
        lock (gate)
        {
            if (busy || recording || executor.HasOwnedInput) throw new InvalidOperationException("Stop and release input before changing the Click target.");
            targetProcess?.Dispose(); targetProcess = null; target = null; targetInvalidated = false;
            targetId = Guid.NewGuid(); state = "Ready";
            reason = "Desktop mode. Start minimises the clicker; clicks use physical screen coordinates. Stop All cancels.";
        }
        NotifyChanged();
    }
    public string? SelectedTargetTitle
    {
        get
        {
            var selected = target;
            if (selected is null || targetInvalidated || !ProbeNative.IsWindow(selected.Handle)
                || ProbeNative.GetWindowThreadProcessId(selected.Handle, out uint pid) == 0 || pid != selected.ProcessId) return null;
            var title = new StringBuilder(512);
            DesktopNative.GetWindowText(selected.Handle, title, title.Capacity);
            return title.Length == 0 ? selected.Title : title.ToString();
        }
    }
    public static bool InputMatchesKey(InputControl input, int key) => DesktopNative.VirtualKey(input) == key;
    public static InputControl InputFromVirtualKey(int key)
    {
        if (ShortcutKeys.IsMouse(key)) return ShortcutKeys.MouseInput(key);
        uint mapped = DesktopNative.MapVirtualKeyExW((uint)key, 4, DesktopNative.GetKeyboardLayout(0));
        int scan = (int)(mapped & 0xFF) | ((mapped & 0xFF00) == 0xE000 ? 0x100 : 0);
        if (!SequenceKeys.Supported.Values.Contains(scan)) throw new ArgumentException("That key cannot be held. Choose a standard keyboard key or mouse button.");
        return InputControl.Key(scan);
    }
    public bool BringTargetForward(bool resume = false)
    {
        if (resume && CursorFreeActive) return BackgroundTargetAvailable;
        if (IsDesktopMode) return true;
        var selected = target;
        string? problem = null;
        if (selected is null || targetInvalidated || !ProbeNative.IsWindow(selected.Handle)
            || ProbeNative.GetWindowThreadProcessId(selected.Handle, out uint pid) == 0 || pid != selected.ProcessId)
            problem = "Choose an open Click target first.";
        else
        {
            if (ProbeNative.IsIconic(selected.Handle)) DesktopNative.ShowWindow(selected.Handle, 9);
            if (ProbeNative.GetForegroundWindow() != selected.Handle && !ProbeNative.SetForegroundWindow(selected.Handle))
                problem = "Windows could not bring the Click target forward. Select its window, then press the start hotkey.";
        }
        if (problem is null) return true;
        lock (gate) { state = "Not started"; reason = problem; }
        NotifyChanged(); return false;
    }

    public TargetClientFrame GetTargetFrame()
    {
        if (IsDesktopMode) return new(DesktopNative.GetSystemMetrics(76), DesktopNative.GetSystemMetrics(77), DesktopNative.GetSystemMetrics(78), DesktopNative.GetSystemMetrics(79), 96);
        var selected = target;
        if (selected is null || targetInvalidated || !ProbeNative.IsWindow(selected.Handle) || ProbeNative.IsIconic(selected.Handle)
            || ProbeNative.GetWindowThreadProcessId(selected.Handle, out uint pid) == 0 || pid != selected.ProcessId
            || !DesktopNative.GetClientRect(selected.Handle, out var rect)) throw new InvalidOperationException("Choose an open, restored target window first.");
        var origin = new ProbeNative.Point();
        int dpi = (int)DesktopNative.GetDpiForWindow(selected.Handle);
        if (!DesktopNative.ClientToScreen(selected.Handle, ref origin) || rect.Right <= 0 || rect.Bottom <= 0 || dpi <= 0)
            throw new InvalidOperationException("The selected window's client geometry is unavailable.");
        return new(origin.X, origin.Y, rect.Right, rect.Bottom, dpi);
    }

    private bool TargetAvailable
    {
        get
        {
            // Desktop coordinates have no bound foreground window. Explorer focus transitions may report HWND 0.
            if (IsDesktopMode) return Control.Available;
            var selected = target;
            try
            {
                return !targetInvalidated && selected is not null && targetProcess is { HasExited: false }
                    && ProbeNative.IsWindow(selected.Handle) && !ProbeNative.IsIconic(selected.Handle)
                    && ProbeNative.GetForegroundWindow() == selected.Handle
                    && ProbeNative.GetWindowThreadProcessId(selected.Handle, out uint pid) != 0 && pid == selected.ProcessId;
            }
            catch (InvalidOperationException) { return false; }
        }
    }
    public ForegroundTarget? Current => new(targetId, CursorFreeActive ? BackgroundTargetAvailable : TargetAvailable);
    public void SetClickRateLimit(int clicksPerSecond)
    {
        lock (gate)
        {
            if (busy || recording) throw new InvalidOperationException("Stop playback before changing the click limit.");
            executor.ClickRateLimit = new(clicksPerSecond);
        }
    }

    public RuntimeSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                var progress = executor.Progress;
                string liveReason = countingDown ? $"Starts in {Math.Max(0, Math.Ceiling((startDelay - Stopwatch.GetElapsedTime(started)).TotalSeconds)):0}s. {(CursorFreeActive ? "Your cursor stays free." : runningRequest?.Helper?.Position is not null ? "Clicks use your saved point." : "Put the pointer inside the Click target.")} Stop All cancels." : reason;
                return new(state, liveReason, Interlocked.Read(ref inputsSent), busy && !countingDown ? progress.CompletedCycles : completed,
                    busy ? executor.TotalActiveTime - activeAtStart : recording && !previewing ? Stopwatch.GetElapsedTime(started) : elapsed, busy || recording, !busy && executor.HasOwnedInput,
                    busy && !countingDown ? progress.CurrentAction : state, busy && !countingDown ? progress.NextAction : "None",
                    progress.Held.Count == 0 ? "None" : string.Join(", ", progress.Held.Select(RecordingNormalizer.Label)), activity, executor.TotalCompletedLoops,
                    executor.TotalActiveTime, busy && !countingDown && playbackControl?.IsPaused != true && cancellation?.IsCancellationRequested == false,
                    busy ? actionToggleSession?.ActiveIds ?? (runningMacro == Guid.Empty ? [] : [runningMacro]) : [],
                    actionToggleSession?.ActionBusy ?? busy && runningRequest?.Sequence is not null, executor.TotalCompletedClicks);
            }
        }
    }

    public bool TryStart(HelperPlan plan, TimeSpan? countdown = null, Guid macroId = default)
        => TryStart(new RunRequest(plan, null), countdown, macroId);
    public bool TryStart(SequencePlan plan, TimeSpan? countdown = null, Guid macroId = default)
        => TryStart(new RunRequest(null, plan), countdown, macroId);
    public bool TryStartQuick(SequencePlan plan, int actions, TimeSpan? countdown = null, bool scaleClickPositions = true)
    {
        var frame = GetTargetFrame();
        if (IsDesktopMode) plan.ValidateDesktopMode(frame.Width, frame.Height, frame.Dpi);
        return TryStart(new RunRequest(null, plan.AdaptClickPoints(frame.Width, frame.Height, frame.Dpi, scaleClickPositions), Label: $"Actions · {actions} action{(actions == 1 ? "" : "s")}"), countdown, default);
    }
    private bool TryStart(RunRequest request, TimeSpan? countdown, Guid macroId)
    {
        bool accepted = false;
        lock (gate)
        {
            if (disposed || recording || ShutdownPending || executor.TotalCompletedLoops >= executor.CompletionLimit || executor.TotalCompletedClicks >= executor.ClickCompletionLimit || executor.TotalActiveTime >= executor.ActiveTimeLimit) return false;
            if (busy)
            {
                if (request.Sequence is not { } joined || actionToggleSession is not { } session || playbackControl?.IsPaused == true || !TargetAvailable) return false;
                try
                {
                    ValidateJoiningSequence(joined);
                    bool queued = session.Start(joined, (request.Label ?? "Actions") + " · " + joined.Repetition, countdown ?? TimeSpan.Zero, macroId);
                    if (queued) { reason = "Actions will run alongside the active toggles."; NotifyChanged(); }
                    return queued;
                }
                catch (ArgumentException ex) { reason = ex.Message; NotifyChanged(); return false; }
            }
            var errors = request.Helper?.Validate() ?? Array.Empty<string>();
            var controls = request.ToggleHold is { } toggle ? new[] { toggle.Plan.Control } : request.Helper is { } helper ? new[] { helper.Control }
                : request.Sequence!.Inputs.Select(i => i.Control).Distinct().ToArray();
            string? problem = errors.FirstOrDefault();
            if (!Control.Available) problem = Control.Error ?? "Stop route unavailable.";
            else if (executor.HasOwnedInput) problem = "Cleanup is pending. Release physical input, then retry cleanup.";
            else if (target is null && !IsDesktopMode || countdown is null && !(request.LeaveCursorFree ? BackgroundTargetAvailable : TargetAvailable) && !StartsWithWindowWait(request.Sequence)) problem = "The selected target must be open, restored and in the foreground.";
            if (IsDesktopMode)
            {
                try
                {
                    if (request.ToggleHold is not null || request.Helper is not null)
                        throw new ArgumentException("Select a Click target before using toggles or saved helpers.");
                    request.Sequence!.ValidateDesktopMode(GetTargetFrame().Width, GetTargetFrame().Height, 96);
                }
                catch (ArgumentException ex) { problem = ex.Message; }
            }
            if (!request.LeaveCursorFree && Control.ModifiersDown) problem = "Release Ctrl, Alt, Shift and Windows keys before starting.";
            foreach (var control in controls)
            {
                if (Control.UsesRegisteredInput(control)) problem = "An action presses a registered function-key shortcut. Change that shortcut or action first.";
                Control.SampleBeforeRun(control);
                if (!request.LeaveCursorFree && Control.IsPhysicalDown(control)) problem = "Release the selected input before starting.";
            }
            if (problem is not null) { state = "Not started"; reason = problem; }
            else
            {
                inputRejection = null;
                cursorFreeActive = request.LeaveCursorFree;
                if (request.Sequence?.Inputs.Any(i => (i.Motion?.MoveDuration > TimeSpan.Zero || i.Motion?.UsePreviousPause == true)) == true)
                    controls = controls.Append(new InputControl(InputKind.PointerMove, 0)).Distinct().ToArray();
                cancellation = new();
                playbackControl = new(); executor.Playback = playbackControl; runningRequest = request;
                cancellationCallbacks = Task.CompletedTask;
                pending = request; Volatile.Write(ref activeControls, controls);
                activeLayout = request.Sequence is { } keyboardSequence && keyboardSequence.Inputs.Any(i => i.Control.Kind == InputKind.ScanCode) ? keyboardSequence.KeyboardLayout : null;
                observingWindowActive = StartsWithWindowWait(request.Sequence);
                runningMacro = macroId;
                activity = request.Label ?? armedHelpers.FirstOrDefault(h => h.Binding.MacroId == macroId)?.DisplayName
                    ?? (request.Helper is { } quick ? "Quick · " + HelperLabel(quick) : "Sequence preview");
                if (request.Helper is { Count: 0, ActiveDuration: null }) activity += " · until stopped";
                if (request.Sequence is { } sequence) activity += " · " + sequence.Repetition;
                if (request.Sequence is not null || request.ToggleHold is not null)
                {
                    actionToggleSession = new();
                    if (request.ToggleHold is { } hold) { actionToggleSession.Toggle(hold); activity = "Toggles"; }
                    if (request.Sequence is { } actions) actionToggleSession.Start(actions, activity, TimeSpan.Zero, macroId);
                }
                startDelay = countdown ?? TimeSpan.Zero; countingDown = startDelay > TimeSpan.Zero;
                busy = true; state = countingDown ? "Starting" : "Running";
                reason = countingDown ? $"Starts in {startDelay.TotalSeconds:0.#} seconds. Put the pointer inside the Click target. Stop All cancels." : "Foreground target bound. Stop All is always available.";
                inputsSent = 0; completed = 0; elapsed = TimeSpan.Zero; started = Stopwatch.GetTimestamp();
                activeAtStart = executor.TotalActiveTime;
                workReady.Set();
                accepted = true;
            }
        }
        NotifyChanged();
        return accepted;
    }

    private void Toggle()
    {
        lock (gate)
        {
            if (actionToggleSession is { } session)
            {
                if (session.ActionBusy) { StopActions(); return; }
                if (playbackControl?.IsPaused != true && QuickSequenceArmed) { QuickStartRequested?.Invoke(); return; }
            }
        }
        if (CanResume) { TryResume(); return; }
        if (Snapshot.Busy) { if (!Control.HoldTrigger) StopPlayback("Stopped with toggle hotkey."); return; }
        if (QuickSequenceArmed && QuickStartRequested is { } start) start();
    }

    public async Task<bool> ConfigureHelpersAsync(IReadOnlyList<ArmedHelper> helpers)
    {
        var copy = helpers.ToArray();
        foreach (var item in copy)
            if (item.Binding.HoldTrigger || item.Plan.Validate().Count > 0
                || item.Plan is not { Mode: HelperMode.Hold or HelperMode.Repeat, Control.Kind: InputKind.ScanCode or InputKind.MouseButton, Count: 0, ActiveDuration: null }
                || item.Plan.Position is not null || item.Plan.LeaveCursorFree)
                throw new ArgumentException("A toggle needs an until-stopped key/button hold or repeat and a tap shortcut.");
        lock (gate) armedHelpers = [];
        if (actionToggleSession is { } session) session.Retain(copy.Select(h => h.Binding.MacroId));
        else Stop("Stopped while toggle availability changed.");
        bool success = await Control.ConfigureBindingsAsync(copy.Select(x => x.Binding).ToArray()).ConfigureAwait(false);
        lock (gate) if (success && !disposed) armedHelpers = copy;
        return success;
    }
    private void MacroTriggered(Guid id, TriggerCommand command, bool holdTrigger)
    {
        if (command != TriggerCommand.Toggle) return;
        lock (gate)
        {
            var helper = armedHelpers.FirstOrDefault(x => x.Binding.MacroId == id);
            if (helper is null) return;
            if (!IsDesktopMode && !HasSelectedTarget) { reason = "Select a Click target before using a toggle."; NotifyChanged(); return; }
            var toggleHold = helper.Plan;
            try
            {
                if (actionToggleSession is { } session)
                {
                    bool alreadyOn = session.ActiveIds.Contains(id);
                    if (!alreadyOn && (!TargetAvailable || playbackControl?.IsPaused == true || countingDown || ShutdownPending)) return;
                    if (!alreadyOn) { if (Control.UsesRegisteredInput(toggleHold.Control)) throw new ArgumentException("The held key is also a registered shortcut. Change that shortcut first."); Control.SampleBeforeRun(toggleHold.Control); if (Control.IsPhysicalDown(toggleHold.Control) || Control.ModifiersDown) throw new ArgumentException("Release physical keys/buttons before enabling this hold."); }
                    if (!session.Toggle(new(id, helper.DisplayName, toggleHold))) reason = "The session is finishing. Press the shortcut again once stopped.";
                    NotifyChanged(); return;
                }
                if (!busy) TryStart(new RunRequest(null, null, Label: "Toggles", ToggleHold: new(id, helper.DisplayName, toggleHold)), null, default);
                else { reason = "Stop the current run before using this toggle."; NotifyChanged(); }
            }
            catch (ArgumentException ex) { reason = ex.Message; NotifyChanged(); }
        }
    }

    public void Stop(string message = "Stopped by you.")
    {
        // An observer (for example the shutdown notice) must never block cancellation.
        try
        {
            foreach (var observer in StopIssued?.GetInvocationList() ?? [])
            {
                try { ((Action)observer)(); }
                catch (Exception) { Interlocked.Increment(ref notificationFailures); }
            }
        }
        finally { StopPlayback(message); QueueCleanupRetry(); }
    }

    public void StopForShutdown() => StopPlayback("Stopped for automatic shutdown. Cancel shutdown to keep working.");
    public void StopActions()
    {
        foreach (var observer in StopIssued?.GetInvocationList() ?? [])
        { try { ((Action)observer)(); } catch (Exception) { Interlocked.Increment(ref notificationFailures); } }
        lock (gate)
        {
            if (actionToggleSession is { } session && session.ActiveIds.Length > 0)
            { session.StopAction(); reason = "Actions stopped; unrelated toggles continue."; NotifyChanged(); }
            else StopPlayback("Actions stopped.");
        }
    }
    public bool Pause(string message = "Paused by you. Resume explicitly after reviewing the target.")
    {
        lock (gate)
        {
            if (!busy || countingDown || cancellation?.IsCancellationRequested != false || playbackControl?.RequestPause() != true) return false;
            state = "Pausing"; reason = message;
        }
        NotifyChanged(); return true;
    }
    public bool TryResume()
    {
        lock (gate)
        {
            if (disposed || cancellation?.IsCancellationRequested != false || playbackControl?.CanResume != true) return false;
            if (!(CursorFreeActive ? BackgroundTargetAvailable : TargetAvailable) || !Control.Available || ShutdownPending || !CursorFreeActive && Control.ModifiersDown
                || !CoordinatesAvailable(runningRequest?.Sequence)
                || activeLayout is { } layout && !string.Equals(layout, TargetKeyboardLayout, StringComparison.OrdinalIgnoreCase))
            { reason = "Resume refused. Restore the selected target, calibration and layout, and release physical modifiers."; NotifyChanged(); return false; }
            foreach (var input in actionToggleSession?.ActiveControls ?? activeControls)
            {
                Control.SampleBeforeRun(input);
                if (!CursorFreeActive && Control.IsPhysicalDown(input)) { reason = "Release physical inputs before resuming."; NotifyChanged(); return false; }
            }
            if (!playbackControl.RequestResume()) return false;
            state = "Resuming"; reason = "Resuming the remaining timing after explicit review.";
        }
        NotifyChanged(); return true;
    }
    private void StopPlayback(string message)
    {
        Interlocked.Increment(ref stopVersion);
        Action<string>? finishCapture;
        lock (gate)
        {
            finishCapture = captureStop;
            if (finishCapture is null && (!busy || cancellation is null)) return;
            if (finishCapture is null)
            {
            state = "Stopping"; reason = message;
            // CancelAsync sets the signal immediately, without blocking the low-level hook callback
            // behind an in-flight SendInput call that may itself be waiting for hook delivery.
            cancellationCallbacks = cancellation!.CancelAsync();
            }
        }
        finishCapture?.Invoke(message);
        NotifyChanged();
    }

    internal bool CaptureTargetAvailable => TargetAvailable;
    public void BeginTargetPreview(Action<string> finish)
    {
        BeginCapture(finish, finish);
        lock (gate) { previewing = true; activity = "Named points"; state = "Target preview"; reason = "Input-free target preview. Stop All cancels."; }
        NotifyChanged();
    }
    public void EndTargetPreview(string message)
    {
        lock (gate)
        {
            if (!previewing) return;
            previewing = false; recording = false; captureStop = null; capturePause = null;
            elapsed = TimeSpan.Zero; state = "Stopped"; reason = message;
        }
        NotifyChanged();
    }
    internal string? TargetKeyboardLayout
    {
        get
        {
            var selected = target;
            uint thread = ProbeNative.GetWindowThreadProcessId(selected?.Handle ?? ProbeNative.GetForegroundWindow(), out _);
            return thread == 0 ? null : unchecked((uint)DesktopNative.GetKeyboardLayout(thread).ToInt64()).ToString("X8");
        }
    }
    internal void BeginCapture(Action<string> stopCapture, Action<string> pauseCapture)
    {
        lock (gate)
        {
            if (disposed || busy || recording || executor.HasOwnedInput || !Control.Available || !TargetAvailable)
                throw new InvalidOperationException("Capture requires stopped playback, finished cleanup, an available Stop All key and the selected target in front. Bring the target forward before the countdown ends, then retry.");
            ValidateCaptureResume();
            recording = true; captureStop = stopCapture; capturePause = pauseCapture; started = Stopwatch.GetTimestamp();
            activity = "Recording draft";
            inputsSent = 0; completed = 0; elapsed = TimeSpan.Zero; state = "Recording"; reason = "Capture in the selected foreground window. Stop All ends capture.";
        }
        NotifyChanged();
    }
    internal void ValidateCaptureResume()
    {
        lock (gate)
        {
            if (disposed || busy || !Control.Available || !TargetAvailable) throw new InvalidOperationException("Bring the selected target forward before resuming capture.");
            if (Control.ModifiersDown) throw new InvalidOperationException("Release modifiers before recording.");
            foreach (int code in SequenceKeys.Supported.Values)
            {
                var input = InputControl.Key(code); Control.SampleBeforeRun(input);
                if (Control.IsPhysicalDown(input)) throw new InvalidOperationException("Release held keys before recording.");
            }
            foreach (var button in Enum.GetValues<PointerButton>())
            {
                var input = InputControl.Mouse(button); Control.SampleBeforeRun(input);
                if (Control.IsPhysicalDown(input)) throw new InvalidOperationException("Release mouse buttons before recording.");
            }
        }
    }
    internal void CapturePaused(string message) { lock (gate) { state = "Recording paused"; reason = message; } NotifyChanged(); }
    internal void CaptureResumed() { lock (gate) { state = "Recording"; reason = "Capture resumed explicitly. Stop All ends capture."; } NotifyChanged(); }
    internal void EndCapture(string message)
    {
        lock (gate)
        {
            recording = false; captureStop = null; capturePause = null; elapsed = Stopwatch.GetElapsedTime(started);
            state = "Recording stopped"; reason = message;
        }
        NotifyChanged();
    }

    private void OnWindowChanged(nint hwnd, uint evt)
    {
        var selected = target;
        if (selected is null) { if (evt == 3) NotifyChanged(); return; }
        if (hwnd == selected.Handle && evt == 0x8001) { InvalidateTarget(); return; }
        lock (gate) if (!busy && !recording) return;
        if ((observingWindowActive || CursorFreeActive) && evt == 3) return;
        if (!countingDown && evt == 3 && hwnd != selected.Handle || hwnd == selected.Handle && evt is 0x16 or 0x8001)
        {
            Action<string>? pause;
            lock (gate) pause = capturePause;
            if (pause is not null) pause("Target is no longer in the foreground. Resume capture explicitly.");
            else if (!Pause("Paused: target focus lost. Returning never resumes playback; resume explicitly."))
            { if (!PlaybackIsPaused) Stop("Stopped: the target is no longer in the foreground."); }
        }
    }
    private void InvalidateTarget()
    {
        targetInvalidated = true;
        Stop("Target closed. Select its new window before starting again.");
        lock (gate) if (!busy) { state = "Target unavailable"; reason = "The bound window closed. Select a new target."; }
        NotifyChanged();
    }
    private void OnPhysical(InputControl input, bool down)
    {
        // A physical press can interrupt an owned hold. Retry its deferred up after
        // release, off the hook thread; SendInput must never block that callback.
        if (!down) QueueCleanupRetry();
        if (countingDown || observingWindowActive || CursorFreeActive) return;
        var controls = actionToggleSession?.ActiveControls ?? Volatile.Read(ref activeControls);
        if (controls.Length == 0) return;
        bool modifier = input.Kind == InputKind.ScanCode && (input.Code & 0xFF) is 0x1D or 0x2A or 0x36 or 0x38 or 0x5B or 0x5C;
        if (controls.Contains(input) || modifier && controls.Any(c => c.Kind == InputKind.ScanCode))
            Stop("Stopped: physical input takes priority.");
    }

    public bool RetryCleanup()
    {
        bool success;
        lock (gate)
        {
            if (disposed || busy || recording) return false;
            success = executor.RetryCleanup();
            cleanupPendingRecovery = !success;
            if (success) cursorFreeActive = false;
            state = success ? "Stopped" : "Cleanup pending";
            reason = success ? "Owned input released. Start again explicitly when ready." : "Release the physical key or button, then retry cleanup.";
        }
        NotifyChanged(); return success;
    }

    private volatile bool cleanupPendingRecovery;
    private void QueueCleanupRetry()
    {
        if (!cleanupPendingRecovery) return;
        cleanupRetry.Request();
    }
    private void RecoverDeferredCleanup()
    {
        try
        {
            lock (gate)
            {
                if (disposed || busy || recording || !executor.HasOwnedInput) return;
            }
            RetryCleanup(); // Rechecks under gate and never starts playback.
        }
        catch (Exception)
        {
            lock (gate)
            {
                if (disposed) return;
                cleanupPendingRecovery = executor.HasOwnedInput;
                state = "Cleanup pending";
                reason = "Input release could not finish. Release physical input and retry cleanup.";
            }
            NotifyChanged();
        }
    }

    public bool TrySend(InputTransition transition)
    {
        if (CursorFreeActive)
        {
            bool accepted = SendBackgroundClick(transition);
            if (accepted) Interlocked.Increment(ref inputsSent);
            return accepted;
        }
        ProbeNative.Point? destination = null;
        if (transition.Edge == InputEdge.Down || transition.Position is not null)
        {
            if (!TargetAvailable) return RejectInput("The Click target is unavailable or no longer in front. Bring it forward and start again.");
            if (cancellation?.IsCancellationRequested != false || !Control.Available) return RejectInput("Playback was stopped or its input controls became unavailable.");
            if (Control.IsPhysicalDown(transition.Control) || Control.UnownedModifiersDown(ownsInput)) return RejectInput("Release physical keys/buttons before starting. Physical input takes priority.");
            if (activeLayout is { } layout && !string.Equals(layout, TargetKeyboardLayout, StringComparison.OrdinalIgnoreCase)) return RejectInput("The foreground keyboard layout differs from this recording.");
            if (transition.Control.Kind != InputKind.ScanCode)
            {
                TargetClientFrame frame;
                try { frame = GetTargetFrame(); } catch (InvalidOperationException) { return false; }
                ProbeNative.Point pointer;
                if (transition.Position is { } position)
                {
                    if (IsDesktopMode && position.Space != CoordinateSpace.Desktop || !position.Matches(frame.Width, frame.Height, frame.Dpi)) return RejectInput("This click's saved location no longer matches the target size or desktop layout. Set its location again.");
                    var resolved = position.Resolve(frame.X, frame.Y, frame.Width, frame.Height, frame.Dpi);
                    pointer = new() { X = resolved.X, Y = resolved.Y }; destination = pointer;
                }
                else if (!ProbeNative.GetCursorPos(out pointer)) return false;
                if (pointer.X < frame.X || pointer.Y < frame.Y
                    || pointer.X >= frame.X + frame.Width || pointer.Y >= frame.Y + frame.Height) return false;
                var underPointer = DesktopNative.GetAncestor(DesktopNative.WindowFromPoint(pointer), 2);
                if (IsDesktopMode)
                {
                    if (ProbeNative.GetWindowThreadProcessId(underPointer, out uint pid) == 0) return RejectInput("No receiving window is available at this desktop click location.");
                    if (pid == Environment.ProcessId && underPointer != OverlayHandle) return RejectInput("This action points at ME Clicker itself. Set its location in the app you want to click.");
                }
                else if (underPointer != target!.Handle && underPointer != OverlayHandle) return RejectInput("This click is covered by another window or falls outside the Click target. Bring the target forward or set a new location.");
            }
            else if (IsDesktopMode && (ProbeNative.GetWindowThreadProcessId(ProbeNative.GetForegroundWindow(), out uint pid) == 0 || pid == Environment.ProcessId)) return RejectInput("Desktop key presses need another app in front. ME Clicker cannot play keys into itself.");
        }
        else if (Control.IsPhysicalDown(transition.Control)) return false; // Never release a user's physical hold.
        long dispatchStarted = Stopwatch.GetTimestamp();
        bool sent = DesktopNative.Send(transition, destination);
        long dispatchTicks = Stopwatch.GetTimestamp() - dispatchStarted;
        Interlocked.Add(ref nativeDispatchTicks, dispatchTicks);
        Interlocked.Increment(ref nativeDispatchCount);
        long maximum;
        do { maximum = Interlocked.Read(ref longestNativeDispatchTicks); }
        while (dispatchTicks > maximum && Interlocked.CompareExchange(ref longestNativeDispatchTicks, dispatchTicks, maximum) != maximum);
        if (sent) Interlocked.Increment(ref inputsSent);
        return sent;
    }

    public bool IsOwnPoint(int screenX, int screenY, bool includeOverlay = true)
    {
        var window = DesktopNative.GetAncestor(DesktopNative.WindowFromPoint(new() { X = screenX, Y = screenY }), 2);
        if (!includeOverlay && window == OverlayHandle) return false;
        return ProbeNative.GetWindowThreadProcessId(window, out uint pid) != 0 && pid == Environment.ProcessId;
    }
    internal ClientPoint? CapturePoint(int screenX, int screenY)
    {
        if (IsDesktopMode)
        {
            var frame = GetTargetFrame();
            return screenX >= frame.X && screenY >= frame.Y && screenX < frame.X + frame.Width && screenY < frame.Y + frame.Height
                ? new(screenX, screenY, frame.Width, frame.Height, frame.Dpi, CoordinateSpace.Desktop) : null;
        }
        var selected = target;
        if (selected is null || !TargetAvailable || !DesktopNative.GetClientRect(selected.Handle, out var rect)) return null;
        var origin = new ProbeNative.Point();
        if (!DesktopNative.ClientToScreen(selected.Handle, ref origin)) return null;
        var point = new ClientPoint(screenX - origin.X, screenY - origin.Y, rect.Right, rect.Bottom, (int)DesktopNative.GetDpiForWindow(selected.Handle));
        var under = DesktopNative.GetAncestor(DesktopNative.WindowFromPoint(new() { X = screenX, Y = screenY }), 2);
        return point.IsValid && (under == selected.Handle || under == OverlayHandle) ? point : null;
    }

    private bool CoordinatesAvailable(SequencePlan? plan)
    {
        if (IsDesktopMode && plan is not null)
        {
            try { var frame = GetTargetFrame(); plan.ValidateDesktopMode(frame.Width, frame.Height, frame.Dpi); return true; }
            catch (ArgumentException) { return false; }
        }
        if (plan is null || !plan.Inputs.Any(i => i.Position is not null) && !plan.Conditions.Any(c => c.Definition.Region is not null)) return true;
        var selected = target;
        if (selected is null || !DesktopNative.GetClientRect(selected.Handle, out var client)) return false;
        int dpi = (int)DesktopNative.GetDpiForWindow(selected.Handle);
        var origin = new ProbeNative.Point();
        if (!DesktopNative.ClientToScreen(selected.Handle, ref origin)) return false;
        return plan.Inputs.All(i => i.Position is null || i.Position.Matches(client.Right, client.Bottom, dpi)
            && (i.Position.Space != CoordinateSpace.Desktop || i.Position.X >= origin.X && i.Position.Y >= origin.Y
                && i.Position.X < origin.X + client.Right && i.Position.Y < origin.Y + client.Bottom))
            && plan.Conditions.All(c => c.Definition.Region is null || c.Definition.Region.Origin.Matches(client.Right, client.Bottom, dpi));
    }

    private static string HelperLabel(HelperPlan plan) => plan.Control.Kind switch
    {
        InputKind.MouseButton => ((PointerButton)plan.Control.Code) + " click",
        InputKind.ScanCode => (plan.Mode == HelperMode.Hold ? "Hold " : "Press ")
            + RecordingNormalizer.Label(plan.Control),
        InputKind.Scroll => plan.Control.Code > 0 ? "Scroll up" : "Scroll down",
        InputKind.HorizontalScroll => plan.Control.Code > 0 ? "Scroll right" : "Scroll left",
        _ => RecordingNormalizer.Label(plan.Control)
    };

    private void Worker()
    {
        while (true)
        {
            workReady.WaitOne();
            RunRequest? plan; CancellationTokenSource? stop; Guid instance;
            lock (gate) { if (disposed && pending is null) return; plan = pending; pending = null; stop = cancellation; instance = targetId; }
            if (plan is null || stop is null) continue;
            if (startDelay > TimeSpan.Zero) stop.Token.WaitHandle.WaitOne(startDelay);
            foreach (var input in Volatile.Read(ref activeControls)) Control.SampleBeforeRun(input);
            countingDown = false;
            lock (gate)
            {
                if (!stop.IsCancellationRequested)
                { state = "Running"; reason = "Foreground target bound. Stop All is always available."; }
            }
            NotifyChanged();
            ClickRunResult result;
            try
            {
            using var power = new PowerSession(KeepAwakeWhileRunning && !stop.IsCancellationRequested);
            executor.PlaybackPaused = paused =>
            {
                power.SetPaused(paused);
                lock (gate) { state = paused ? "Paused" : "Running"; reason = paused
                    ? "Paused with input released. Resume rechecks the target and continues remaining timing or retries this condition."
                    : "Resumed explicitly after target and physical-input checks."; }
                NotifyChanged();
            };
            executor.SessionHoldsChanged = NotifyChanged;
            executor.SessionActionChanged = (action, started) =>
            {
                lock (gate)
                {
                    // A finishing action must not replace the explicit Stop All reason or state.
                    if (stop.IsCancellationRequested) return;
                    activeLayout = started && action.Plan.Inputs.Any(i => i.Control.Kind == InputKind.ScanCode) ? action.Plan.KeyboardLayout : null;
                    Volatile.Write(ref activeControls, started && action.Plan.Inputs.Any(i => i.Control.Kind == InputKind.PointerMove || (i.Motion?.MoveDuration > TimeSpan.Zero || i.Motion?.UsePreviousPause == true))
                        ? [new(InputKind.PointerMove, 0)] : []);
                    if (started && !CoordinatesAvailable(action.Plan)) throw new InvalidOperationException("Recalibrate the action positions before playback.");
                    activity = started ? action.Name : "Toggles";
                    reason = started ? "Actions running alongside any enabled holds." : actionToggleSession?.LastActionCancelled == true
                        ? "Actions stopped. Any active toggles remain held." : "Actions finished. Any active toggles remain held.";
                    state = "Running";
                }
                NotifyChanged();
            };
            result = !power.Succeeded
                ? new ClickRunResult(ClickRunOutcome.Faulted, 0, 0, true, "Windows rejected the keep-awake request. Review power settings before starting again.")
                : !CoordinatesAvailable(plan.Sequence)
                ? new ClickRunResult(ClickRunOutcome.InputRejected, 0, 0, true, "Target size or DPI changed. Recalibrate recorded positions before playback.")
                : plan.Sequence is { KeyboardLayout: { } layout } && plan.Sequence.Inputs.Any(i => i.Control.Kind == InputKind.ScanCode) && !string.Equals(layout, TargetKeyboardLayout, StringComparison.OrdinalIgnoreCase)
                ? new ClickRunResult(ClickRunOutcome.InputRejected, 0, 0, true, "Target keyboard layout differs from this recording. Review under the recorded layout before playback.")
                : actionToggleSession is { } session ? executor.Execute(session, instance, stop.Token)
                : plan.Helper is { } helper ? executor.Execute(helper, instance, stop.Token)
                : executor.Execute(plan.Sequence!, instance, stop.Token);
            power.Dispose();
            }
            catch (Exception)
            {
                // Adapter/preflight failures must not terminate the scheduler thread or strand a busy UI.
                bool clean;
                try { clean = executor.RetryCleanup(); } catch (Exception) { clean = false; }
                result = new(ClickRunOutcome.Faulted, 0, 0, clean, "Playback adapter failed. Review the target and retry explicitly; restart if the problem persists.");
            }
            Task callbacks;
            lock (gate) callbacks = cancellationCallbacks;
            callbacks.GetAwaiter().GetResult();
            lock (gate)
            {
                completed = result.CompletedCycles; elapsed = executor.TotalActiveTime - activeAtStart;
                if (!result.CleanupSucceeded) { state = "Cleanup pending"; reason = "Release physical input, then choose Retry cleanup."; }
                else if (result.Outcome == ClickRunOutcome.Completed) { state = "Completed"; reason = "Finished. Start again explicitly when ready."; }
                else if (result.Outcome is ClickRunOutcome.Cancelled or ClickRunOutcome.Paused)
                {
                    state = result.Outcome == ClickRunOutcome.Paused ? "Paused" : "Stopped";
                    if (result.Error is not null) reason = result.Error;
                    else if (!stop.IsCancellationRequested)
                    {
                        var duration = plan.Helper?.ActiveDuration ?? plan.Sequence?.Repetition.ActiveDuration;
                        reason = result.Outcome == ClickRunOutcome.Paused ? "Playback paused with input released."
                            : executor.TotalCompletedClicks >= executor.ClickCompletionLimit ? "Click limit reached. Playback is stopped."
                            : executor.TotalCompletedLoops >= executor.CompletionLimit ? "Completed-loop limit reached. Playback is stopped."
                            : executor.TotalActiveTime >= executor.ActiveTimeLimit || duration is { } limit && elapsed >= limit ? "Active-time limit reached. Playback is stopped."
                            : "Playback stopped. Start again explicitly when ready.";
                    }
                }
                else { state = "Could not continue"; reason = inputRejection is { } rejection ? $"{result.Error}: {rejection}" : result.Error ?? (plan.LeaveCursorFree ? "The cursor-free click was rejected. Keep the target open at its original size; try normal clicks if this app does not accept window messages." : TargetAvailable ? "Put the pointer inside the Click target and release held buttons or keys, then press Start again. The target may also reject Windows input." : "The Click target is not the active window. Select it, then press Start again."); }
                if (result.CleanupSucceeded) cursorFreeActive = false;
                cleanupPendingRecovery = !result.CleanupSucceeded;
                busy = false; cancellation = null; stop.Dispose();
                playbackControl?.Dispose(); playbackControl = null; executor.Playback = null; executor.PlaybackPaused = null; runningRequest = null;
                actionToggleSession?.Dispose(); actionToggleSession = null; executor.SessionActionChanged = null; executor.SessionHoldsChanged = null;
                Volatile.Write(ref activeControls, []);
                activeLayout = null;
                observingWindowActive = false;
            }
            if (!result.CleanupSucceeded) QueueCleanupRetry();
            NotifyChanged();
        }
    }

    public void Dispose()
    {
        Stop("Application closing.");
        lock (gate) disposed = true;
        workReady.Set();
        if (worker.Join(TimeSpan.FromSeconds(3)))
        {
            if (executor.HasOwnedInput) executor.RetryCleanup();
            workReady.Dispose(); targetProcess?.Dispose(); schedulerWait.Dispose();
        }
        Control.Dispose();
    }
}


