using System.Diagnostics;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Linux;

public sealed record TargetWindow(ulong Handle, int ProcessId, string Title)
{
    public override string ToString() => Title;
}
public sealed record RuntimeSnapshot(string State, string Reason, long InputsSent, int CompletedCycles,
    TimeSpan Elapsed, bool Busy, bool CleanupPending, string CurrentAction = "", string NextAction = "", string HeldInputs = "None", string Activity = "Minimum Effort", long TotalCompletedLoops = 0, TimeSpan TotalActiveTime = default, bool PlaybackActive = false, IReadOnlyList<Guid>? ActiveHelperIds = null, bool ActionsActive = false, long TotalCompletedClicks = 0);
public sealed record TargetClientFrame(int X, int Y, int Width, int Height, int Dpi);
public sealed record ArmedHelper(MacroBinding Binding, HelperPlan? Plan, SequencePlan? Sequence = null, string? DisplayName = null, bool IsToggle = false);

/// <summary>One scheduler worker and one independent control thread. Starts only by explicit command.</summary>
public sealed partial class DesktopRuntime : IInputSink, IForegroundTargetProvider, IDisposable
{
    private readonly object gate = new();
    private readonly AutoResetEvent workReady = new(false);
    private readonly Thread worker;
    private readonly ClickExecutor executor;
    private readonly DeadlineWait schedulerWait;
    private readonly Func<InputControl, bool> ownsInput;
    private readonly DeferredCleanupRetry cleanupRetry;
    private readonly StopwatchClock clock = new();
    private readonly Timer autoResumeTimer;
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
    /// <summary>Title of the click-through status overlay; hit tests look through it.</summary>
    public const string OverlayTitle = "ME Clicker status overlay";
    private IDesktopHelper Helper => Control.Helper ?? throw new InvalidOperationException(Control.HelperError ?? "The ME Clicker " + LinuxSetup.HelperName + " is not available.");
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
        autoResumeTimer = new(AutoResumeTick, null, Timeout.Infinite, Timeout.Infinite);
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
            var pointer = CursorPosition() ?? throw new InvalidOperationException("Cursor position is unavailable.");
            // Movement stays inside the target frame. Entering from outside uses the usual move.
            if (pointer.X < frame.X || pointer.Y < frame.Y || pointer.X >= frame.X + frame.Width || pointer.Y >= frame.Y + frame.Height) return null;
            return point with { X = point.Space == CoordinateSpace.Desktop ? pointer.X : pointer.X - frame.X,
                Y = point.Space == CoordinateSpace.Desktop ? pointer.Y : pointer.Y - frame.Y };
        };
        ownsInput = executor.Owns;
        cleanupRetry = new(action => ThreadPool.QueueUserWorkItem(_ => action()), RecoverDeferredCleanup);
        Control = new();
        Control.StopRequested += Stop;
        Control.ActionStopRequested += () => StopActions();
        Control.TriggerAllowed = () => TargetAvailable;
        Control.ToggleRequested += Toggle;
        Control.MacroTriggered += MacroTriggered;
        Control.WindowChanged += OnWindowChanged;
        Control.PhysicalChanged += OnPhysical;
        Control.MouseObserved += sample =>
        {
            if (EmergencyStop == EmergencyStopMode.AnyInput && !observingWindowActive && Volatile.Read(ref activeControls).Any(c => c.Kind == InputKind.PointerMove)) Stop("Stopped: physical pointer input takes priority.");
        };
        worker = new Thread(Worker) { IsBackground = true, Name = "MEC scheduler" };
        worker.Start();
    }

    public IReadOnlyList<TargetWindow> ListTargets()
    {
        if (Control.Helper is not { } helper) return [];
        try
        {
            return helper.ListWindows().Where(w => w.ProcessId > 0 && w.ProcessId != Environment.ProcessId && w.Title.Length > 0)
                .Select(w => new TargetWindow(w.Id, w.ProcessId, w.Title)).OrderBy(x => x.Title).ToArray();
        }
        catch (HelperException) { return []; }
    }

    /// <summary>
    /// Brings one of the clicker's own windows back from minimised and in front. GNOME refuses that request from an
    /// X11 app that is not focused (it shows "is ready" instead), so the shell helper does it. False when it could not.
    /// </summary>
    public bool ActivateOwnWindow(string title)
    {
        if (Control.Helper is not { } helper) return false;
        try { return OwnWindow(helper, title) is { } id && helper.ActivateWindow(id); }
        catch (Exception ex) when (ex is HelperException or InvalidOperationException) { return false; }
    }

    /// <summary>Whether GNOME shows one of the clicker's own windows as minimised; null when the helper cannot say.</summary>
    public bool? OwnWindowMinimized(string title)
    {
        if (Control.Helper is not { } helper) return null;
        try { return OwnWindow(helper, title) is { } id ? helper.GetWindow(id).Minimized : null; }
        catch (Exception ex) when (ex is HelperException or InvalidOperationException) { return null; }
    }

    private static ulong? OwnWindow(IDesktopHelper helper, string title) =>
        helper.ListWindows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && w.Title == title)?.Id;

    private HelperTargetState? Query(TargetWindow? selected)
    {
        if (Control.Helper is not { } helper) return null;
        try { return helper.QueryTarget(selected?.Handle ?? 0); }
        catch (HelperException) { return null; }
    }
    private bool Matches(TargetWindow selected, HelperTargetState? state) => state is { Exists: true } live && live.ProcessId == selected.ProcessId;
    /// <summary>The pointer in GNOME's logical desktop pixels, or null when the helper cannot say.</summary>
    public (int X, int Y)? PointerPosition => CursorPosition();
    private (int X, int Y)? CursorPosition()
    {
        try { return Control.Helper?.GetPointer(); }
        catch (HelperException) { return null; }
    }
    private (ulong Id, int ProcessId)? WindowAt(int x, int y, bool includeOverlay = false)
    {
        try { return Control.Helper?.WindowAt(x, y, includeOverlay ? "" : OverlayTitle); }
        catch (HelperException) { return null; }
    }

    public void SelectTarget(TargetWindow selected)
    {
        lock (gate)
        {
            if (busy || recording || executor.HasOwnedInput) throw new InvalidOperationException("Stop and release owned input before changing the target.");
            if (!Matches(selected, Query(selected)))
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
            if (selected is null || targetInvalidated || Control.Helper is not { } helper) return null;
            try
            {
                var window = helper.GetWindow(selected.Handle);
                if (!window.Exists || window.ProcessId != selected.ProcessId) return null;
                return window.Title.Length == 0 ? selected.Title : window.Title;
            }
            catch (HelperException) { return null; }
        }
    }
    public static bool InputMatchesKey(InputControl input, int key) => input.Kind switch
    {
        InputKind.MouseButton => ShortcutKeys.IsMouse(key) && ShortcutKeys.MouseInput(key) == input,
        InputKind.ScanCode => LinuxKeys.EvdevFromScanCode(input.Code) is int code and not 0 && LinuxKeys.EvdevFromVirtualKey(key) == code,
        _ => false
    };
    /// <summary>The virtual key of a key or mouse button (0 when it has none), as shortcuts store it.</summary>
    public static int VirtualKeyOf(InputControl input) => DesktopControl.VirtualKey(input);
    public static InputControl InputFromVirtualKey(int key)
    {
        if (ShortcutKeys.IsMouse(key)) return ShortcutKeys.MouseInput(key);
        if (LinuxKeys.ScanCodeFromEvdev(LinuxKeys.EvdevFromVirtualKey(key)) is not { } scan || !SequenceKeys.Supported.Values.Contains(scan))
            throw new ArgumentException("That key cannot be held. Choose a standard keyboard key or mouse button.");
        return InputControl.Key(scan);
    }
    public bool BringTargetForward(bool resume = false)
    {
        if (resume && CursorFreeActive) return BackgroundTargetAvailable;
        if (IsDesktopMode) return true;
        var selected = target;
        string? problem = null;
        if (selected is null || targetInvalidated || !Matches(selected, Query(selected)))
            problem = "Choose an open Click target first.";
        else if (Query(selected) is not { Focus: var focus } || focus != selected.Handle)
        {
            bool raised = false;
            try { raised = Helper.ActivateWindow(selected.Handle); } catch (Exception ex) when (ex is HelperException or InvalidOperationException) { }
            // GNOME activates asynchronously; give the focus change a moment to land.
            for (int i = 0; raised && i < 20 && Query(selected)?.Focus != selected.Handle; i++) Thread.Sleep(15);
            if (!raised || Query(selected)?.Focus != selected.Handle)
                problem = "The desktop could not bring the Click target forward. Select its window, then press the start hotkey.";
        }
        if (problem is null) return true;
        lock (gate) { state = "Not started"; reason = problem; }
        NotifyChanged(); return false;
    }

    /// <summary>
    /// The Click target's client area, or the whole desktop. Throws InvalidOperationException whenever it is unavailable,
    /// also when the desktop helper does not answer in time (a busy KWin or GNOME Shell): callers already treat that
    /// exception as "not now", and a helper timeout must not end the app.
    /// </summary>
    public TargetClientFrame GetTargetFrame()
    {
        try
        {
            if (IsDesktopMode) return DesktopFrame();
            var selected = target;
            if (selected is null || targetInvalidated || Query(selected) is not { Exists: true, Minimized: false } state || state.ProcessId != selected.ProcessId)
                throw new InvalidOperationException("Choose an open, restored target window first.");
            if (state.Width <= 0 || state.Height <= 0) throw new InvalidOperationException("The selected window's client geometry is unavailable.");
            return new(state.X, state.Y, state.Width, state.Height, Dpi(state.Scale));
        }
        catch (HelperException ex) { throw new InvalidOperationException("The desktop helper did not answer: " + ex.Message, ex); }
    }

    private static int Dpi(double scale) => (int)Math.Round(96 * (scale > 0 ? scale : 1));
    /// <summary>The bounding box of every monitor in GNOME's logical desktop pixels.</summary>
    public TargetClientFrame DesktopFrame()
    {
        var monitors = Helper.GetMonitors();
        if (monitors.Count == 0) throw new InvalidOperationException("No monitor is available.");
        int left = monitors.Min(m => m.X), top = monitors.Min(m => m.Y);
        int right = monitors.Max(m => m.X + m.Width), bottom = monitors.Max(m => m.Y + m.Height);
        return new(left, top, right - left, bottom - top, 96);
    }

    private bool TargetAvailable
    {
        get
        {
            // Desktop coordinates have no bound foreground window; GNOME may briefly report no focus at all.
            if (IsDesktopMode) return Control.Available;
            var selected = target;
            try
            {
                return !targetInvalidated && selected is not null && targetProcess is { HasExited: false }
                    && Query(selected) is { Exists: true, Minimized: false, ShellReady: true } state && state.Focus == selected.Handle && state.ProcessId == selected.ProcessId;
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
            if (!request.LeaveCursorFree && Control.ModifiersDown) problem = "Release Ctrl, Alt, Shift and Super keys before starting.";
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
                if (request.Sequence?.Inputs.Any(i => i.Control.Kind == InputKind.Look || (i.Motion?.MoveDuration > TimeSpan.Zero || i.Motion?.UsePreviousPause == true)) == true)
                    controls = controls.Append(new InputControl(InputKind.PointerMove, 0)).Distinct().ToArray();
                cancellation = new();
                playbackControl = new(); executor.Playback = playbackControl; runningRequest = request;
                // A window target that goes behind another window pauses the run and resumes it when it is back in front.
                // Desktop mode has no window to wait for.
                executor.PauseOnTargetLoss = target is not null;
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
            if ((item.Plan is null) == (item.Sequence is null) || item.Plan?.Validate().Count > 0) throw new ArgumentException("Invalid macro plan.");
        if (copy.Any(h => h.IsToggle && (h.Binding.HoldTrigger || h.Plan is not { Mode: HelperMode.Hold or HelperMode.Repeat, Control.Kind: InputKind.ScanCode or InputKind.MouseButton, Count: 0, ActiveDuration: null }
            || h.Plan.Position is not null || h.Plan.LeaveCursorFree)))
            throw new ArgumentException("A toggle needs an until-stopped key/button hold or repeat and a tap shortcut.");
        lock (gate) armedHelpers = [];
        if (actionToggleSession is { } session)
        {
            session.Retain(copy.Where(h => h.IsToggle).Select(h => h.Binding.MacroId));
            if (session.ActionId != Guid.Empty && !copy.Any(h => h.Binding.MacroId == session.ActionId)) session.StopAction();
        }
        else Stop("Stopped while profile availability changed.");
        bool success = await Control.ConfigureBindingsAsync(copy.Select(x => x.Binding).ToArray()).ConfigureAwait(false);
        lock (gate) if (success && !disposed) armedHelpers = copy;
        return success;
    }
    /// <summary>A toggle's controller button was pressed: the same as tapping its keyboard or mouse shortcut.</summary>
    public void TriggerToggle(Guid id) => MacroTriggered(id, TriggerCommand.Toggle, false);
    private void MacroTriggered(Guid id, TriggerCommand command, bool holdTrigger)
    {
        if (command == TriggerCommand.None) return;
        lock (gate)
        {
            var helper = armedHelpers.FirstOrDefault(x => x.Binding.MacroId == id);
            if (helper is null) return;
            if (helper.IsToggle && command == TriggerCommand.Toggle && helper.Plan is { } toggleHold)
            {
                if (!IsDesktopMode && !HasSelectedTarget) { reason = "Select a Click target before using a toggle."; NotifyChanged(); return; }
                try
                {
                    if (actionToggleSession is { } session)
                    {
                        bool alreadyOn = session.ActiveIds.Contains(id);
                        if (!alreadyOn && (!TargetAvailable || playbackControl?.IsPaused == true || countingDown || ShutdownPending)) return;
                        if (!alreadyOn) { if (Control.UsesRegisteredInput(toggleHold.Control)) throw new ArgumentException("The held key is also a registered shortcut. Change that shortcut first."); Control.SampleBeforeRun(toggleHold.Control); if (Control.IsPhysicalDown(toggleHold.Control) || Control.ModifiersDown) throw new ArgumentException("Release physical keys/buttons before enabling this hold."); }
                        if (!session.Toggle(new(id, helper.DisplayName ?? "Toggle", toggleHold))) reason = "The session is finishing. Press the shortcut again once stopped.";
                        NotifyChanged(); return;
                    }
                    if (!busy) TryStart(new RunRequest(null, null, Label: "Toggles", ToggleHold: new(id, helper.DisplayName ?? "Toggle", toggleHold)), null, default);
                    else { reason = "Stop the legacy helper before using this toggle."; NotifyChanged(); }
                }
                catch (ArgumentException ex) { reason = ex.Message; NotifyChanged(); }
                return;
            }
            if (actionToggleSession is { } activeSession && helper.Sequence is { } sequence)
            {
                if (activeSession.ActionBusy && activeSession.ActionId == id) StopActions();
                else if (command == TriggerCommand.Toggle) TryStart(sequence, macroId: id);
                return;
            }
            if (busy)
            {
                if (playbackControl?.IsPaused == true)
                { StopPlayback("Paused macro stopped with its trigger. Use Resume for continuation."); return; }
                if (runningMacro == id && (command == TriggerCommand.Stop || !holdTrigger)) StopPlayback("Macro stopped with its trigger.");
                else { reason = "Stop the current Quick action or macro before starting another toggle."; NotifyChanged(); }
                return; // Exclusive Quick/sequence playback; never queue a later start.
            }
            // Toggles were handled above through the shared toggle session.
            if (command == TriggerCommand.Toggle)
            {
                if (helper.Plan is { } plan) TryStart(plan, macroId: id);
                else TryStart(helper.Sequence!, macroId: id);
            }
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
    /// <summary>
    /// How your own input stops a run. OnlyStopAll (default): nothing you press or move stops a run, so Stop All (F9) is
    /// the one stop for the Action List and every toggle. AnyInput: pressing a key or button the clicker is using, or
    /// moving the mouse while it moves it, stops everything (the original safety stop). Stop All always works.
    /// </summary>
    public EmergencyStopMode EmergencyStop { get; set; } = EmergencyStopMode.OnlyStopAll;
    public void StopActions(string message = "Actions stopped.")
    {
        foreach (var observer in StopIssued?.GetInvocationList() ?? [])
        { try { ((Action)observer)(); } catch (Exception) { Interlocked.Increment(ref notificationFailures); } }
        lock (gate)
        {
            if (actionToggleSession is { } session && session.ActiveIds.Length > 0)
            { session.StopAction(); reason = "Actions stopped; unrelated toggles continue."; NotifyChanged(); }
            else StopPlayback(message);
        }
    }
    public bool Pause(string message = "Paused by you. Resume explicitly after reviewing the target.")
    {
        lock (gate)
        {
            if (!busy || countingDown || cancellation?.IsCancellationRequested != false || playbackControl is not { } control) return false;
            // Pausing while already waiting for the target turns it into the user's pause: it no longer resumes by itself.
            if (!control.RequestPause() && !control.MakeManual()) return false;
            state = "Pausing"; reason = message;
        }
        NotifyChanged(); return true;
    }
    /// <summary>The target left the front: pause now and resume by itself once it is back (see AutoResumeTick).</summary>
    private bool PauseForTargetLoss()
    {
        lock (gate)
        {
            if (!busy || countingDown || cancellation?.IsCancellationRequested != false || playbackControl is not { } control) return false;
            if (control.Automatic) return true;
            if (!control.RequestPause(automatic: true)) return control.IsPaused;
            state = "Pausing"; reason = RunPauseStatus.TargetLost;
        }
        NotifyChanged(); return true;
    }
    public bool TryResume() => TryResume(automatic: false);
    private bool TryResume(bool automatic)
    {
        lock (gate)
        {
            if (disposed || cancellation?.IsCancellationRequested != false || playbackControl?.CanResume != true) return false;
            if (!(CursorFreeActive ? BackgroundTargetAvailable : TargetAvailable) || !Control.Available || ShutdownPending || !CursorFreeActive && Control.ModifiersDown
                || !CoordinatesAvailable(runningRequest?.Sequence)
                || activeLayout is { } layout && !string.Equals(layout, TargetKeyboardLayout, StringComparison.OrdinalIgnoreCase))
            { if (automatic) return false; reason = "Resume refused. Restore the selected target, calibration and layout, and release physical modifiers."; NotifyChanged(); return false; }
            foreach (var input in actionToggleSession?.ActiveControls ?? activeControls)
            {
                Control.SampleBeforeRun(input);
                if (!CursorFreeActive && Control.IsPhysicalDown(input)) { if (automatic) return false; reason = "Release physical inputs before resuming."; NotifyChanged(); return false; }
            }
            if (!playbackControl.RequestResume()) return false;
            state = "Resuming"; reason = automatic ? RunPauseStatus.TargetBack : "Resuming the remaining timing after explicit review.";
        }
        NotifyChanged(); return true;
    }
    // Polls only while a run is paused because its target left the front. The window must stay in front for a moment first,
    // so alt-tabbing back does not send input while Alt is still held or the window is still animating.
    private static readonly TimeSpan AutoResumeSettle = TimeSpan.FromMilliseconds(500);
    private long targetBackSince = -1;
    private void SetAutoResumePolling(bool on)
    {
        try { autoResumeTimer.Change(on ? 100 : Timeout.Infinite, on ? 100 : Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }
    private void AutoResumeTick(object? _)
    {
        lock (gate)
        {
            // Stop polling once the pause ended or became the user's; keep polling while a resume is being handed over,
            // because the scheduler may put it back on hold if the window left the front again.
            if (disposed || playbackControl is not { Automatic: true } control) { targetBackSince = -1; SetAutoResumePolling(false); return; }
            if (!control.CanResume) { targetBackSince = -1; return; }
            if (!(CursorFreeActive ? BackgroundTargetAvailable : TargetAvailable)) { targetBackSince = -1; return; }
            if (targetBackSince < 0) { targetBackSince = Environment.TickCount64; return; }
            if (Environment.TickCount64 - targetBackSince < AutoResumeSettle.TotalMilliseconds) return;
        }
        if (TryResume(automatic: true)) lock (gate) targetBackSince = -1;
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
            // behind an in-flight helper call that may itself be waiting for the compositor.
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
            try { return Control.Helper is { } helper ? KeyboardLayouts.FromGnome(helper.GetKeyboardLayout()) : null; }
            catch (HelperException) { return null; }
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

    private void OnWindowChanged(ulong hwnd, uint evt)
    {
        // Our own overlay and picker never take focus from the target; ignore them if GNOME reports one.
        if (evt == 3 && hwnd != 0 && IsOwnOverlay(hwnd)) return;
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
            else if (!PauseForTargetLoss())
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
        // release, off the input thread; a helper call must never block that callback.
        if (!down) QueueCleanupRetry();
        // Only Stop All (F9) stops a run; nothing else you press stops anything, so you can click into your game.
        if (EmergencyStop == EmergencyStopMode.OnlyStopAll) return;
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

    /// <summary>
    /// The user holds this key or button, or a modifier the clicker does not own. With Only Stop All the press waits until
    /// they let go (Stop All still ends the wait, and the target is checked again after it); with Stop on my input it is
    /// refused and the run ends.
    /// </summary>
    private bool WaitForUsersRelease(InputControl control)
    {
        while (Control.IsPhysicalDown(control) || Control.UnownedModifiersDown(ownsInput))
        {
            if (PhysicalOverlap.For(EmergencyStop, InputEdge.Down, userHolds: true) != OverlapAction.Wait) return false;
            if (cancellation?.IsCancellationRequested != false || !Control.Available) return false;
            Thread.Sleep(5);
        }
        return true;
    }
    /// <summary>
    /// Controller to Key: presses or releases one key or mouse button in whichever window is in
    /// front, at the current pointer, through the desktop helper's virtual devices like playback, so it never counts
    /// as the user's own input.
    /// </summary>
    public bool SendControllerOutput(InputControl output, bool down)
    {
        if (output.Kind is not (InputKind.ScanCode or InputKind.MouseButton)) return false;
        bool sent = Send(new InputTransition(output, down ? InputEdge.Down : InputEdge.Up), null);
        if (sent) Interlocked.Increment(ref inputsSent);
        return sent;
    }

    public bool TrySend(InputTransition transition)
    {
        if (CursorFreeActive)
        {
            bool accepted = SendBackgroundClick(transition);
            if (accepted) Interlocked.Increment(ref inputsSent);
            return accepted;
        }
        (int X, int Y)? destination = null;
        if (transition.Edge == InputEdge.Down || transition.Position is not null)
        {
            if (!WaitForUsersRelease(transition.Control)) return RejectInput("Release physical keys/buttons before starting. Physical input takes priority.");
            if (!TargetAvailable) return RejectInput("The Click target is unavailable or no longer in front. Bring it forward and start again.");
            if (cancellation?.IsCancellationRequested != false || !Control.Available) return RejectInput("Playback was stopped or its input controls became unavailable.");
            if (activeLayout is { } layout && !string.Equals(layout, TargetKeyboardLayout, StringComparison.OrdinalIgnoreCase)) return RejectInput("The foreground keyboard layout differs from this recording.");
            if (transition.Control.Kind != InputKind.ScanCode)
            {
                TargetClientFrame frame;
                try { frame = GetTargetFrame(); } catch (InvalidOperationException) { return false; }
                (int X, int Y) pointer;
                if (transition.Position is { } position)
                {
                    if (IsDesktopMode && position.Space != CoordinateSpace.Desktop || !position.Matches(frame.Width, frame.Height, frame.Dpi)) return RejectInput("This click's saved location no longer matches the target size or desktop layout. Set its location again.");
                    pointer = position.Resolve(frame.X, frame.Y, frame.Width, frame.Height, frame.Dpi); destination = pointer;
                }
                else if (CursorPosition() is { } current) pointer = current;
                else return false;
                if (pointer.X < frame.X || pointer.Y < frame.Y
                    || pointer.X >= frame.X + frame.Width || pointer.Y >= frame.Y + frame.Height) return false;
                if (WindowAt(pointer.X, pointer.Y) is not { } underPointer) return RejectInput("The desktop could not report the window at this click location.");
                if (IsDesktopMode)
                {
                    if (underPointer.Id == 0) return RejectInput("No receiving window is available at this desktop click location.");
                    if (underPointer.ProcessId == Environment.ProcessId) return RejectInput("This action points at ME Clicker itself. Set its location in the app you want to click.");
                }
                else if (underPointer.Id != target!.Handle) return RejectInput("This click is covered by another window or falls outside the Click target. Bring the target forward or set a new location.");
            }
            else if (IsDesktopMode && Query(null) is { } desktop && (desktop.Focus == 0 || ForegroundIsOwn(desktop.Focus))) return RejectInput("Desktop key presses need another app in front. ME Clicker cannot play keys into itself.");
        }
        // Never release a user's press. With Only Stop All they took the button over: the clicker's release is skipped and
        // their own release lets go, so the run carries on. With Stop on my input the run ends, as before.
        else if (Control.IsPhysicalDown(transition.Control)) return PhysicalOverlap.For(EmergencyStop, InputEdge.Up, userHolds: true) == OverlapAction.HandOver;
        long dispatchStarted = Stopwatch.GetTimestamp();
        bool sent = Send(transition, destination);
        long dispatchTicks = Stopwatch.GetTimestamp() - dispatchStarted;
        Interlocked.Add(ref nativeDispatchTicks, dispatchTicks);
        Interlocked.Increment(ref nativeDispatchCount);
        long maximum;
        do { maximum = Interlocked.Read(ref longestNativeDispatchTicks); }
        while (dispatchTicks > maximum && Interlocked.CompareExchange(ref longestNativeDispatchTicks, dispatchTicks, maximum) != maximum);
        if (sent) Interlocked.Increment(ref inputsSent);
        return sent;
    }

    private bool IsOwnOverlay(ulong window)
    {
        try { return Control.Helper?.GetWindow(window) is { Exists: true } state && state.ProcessId == Environment.ProcessId && state.Title == OverlayTitle; }
        catch (HelperException) { return false; }
    }

    private bool ForegroundIsOwn(ulong focus)
    {
        try { return Control.Helper?.GetWindow(focus) is { Exists: true } window && window.ProcessId == Environment.ProcessId; }
        catch (HelperException) { return false; }
    }

    private static readonly TimeSpan PointerSettle = TimeSpan.FromMilliseconds(35);
    /// <summary>Sends one transition through the GNOME helper's virtual devices. Movement precedes the edge.</summary>
    private bool Send(InputTransition transition, (int X, int Y)? position)
    {
        if (Control.Helper is not { } helper) return false;
        var control = transition.Control;
        bool down = transition.Edge == InputEdge.Down;
        try
        {
            if (position is { } point && helper.GetPointer() != point)
            {
                // GNOME applies motion asynchronously and re-picks the window under the pointer on its
                // next frame. Confirm the move, then let one frame pass so the edge reaches that window.
                var settle = Stopwatch.StartNew();
                helper.PointerMove(point.X + 0.5, point.Y + 0.5);
                while (helper.GetPointer() != point)
                {
                    if (settle.ElapsedMilliseconds > 150) return false;
                    Thread.Sleep(2);
                    // A new virtual pointer can drop its first absolute motion; send it again.
                    if (settle.ElapsedMilliseconds > 20) helper.PointerMove(point.X + 0.5, point.Y + 0.5);
                }
                if (control.Kind != InputKind.PointerMove) Thread.Sleep(PointerSettle);
            }
            switch (control.Kind)
            {
                case InputKind.PointerMove: return position is not null;
                case InputKind.MouseButton:
                    helper.PointerButton(LinuxKeys.ClutterButton((PointerButton)control.Code), down); return true;
                case InputKind.ScanCode:
                    int code = LinuxKeys.EvdevFromScanCode(control.Code);
                    if (code == 0) return false;
                    helper.Key(code, down); return true;
                case InputKind.Look:
                    // Relative movement: a game that holds the cursor turns its camera; it never has a saved spot.
                    if (!down) return true;
                    var (dx, dy) = control.LookStep;
                    helper.PointerMoveBy(dx, dy); return true;
                case InputKind.Scroll or InputKind.HorizontalScroll:
                    if (!down) return true;
                    uint steps = (uint)Math.Clamp(Math.Abs(control.Code) / 120, 1, 100);
                    uint direction = control.Kind == InputKind.Scroll ? (control.Code > 0 ? 0u : 1u) : (control.Code > 0 ? 3u : 2u);
                    helper.Scroll(direction, steps); return true;
                default: return false;
            }
        }
        catch (HelperException) { return false; }
    }

    public bool IsOwnPoint(int screenX, int screenY, bool includeOverlay = true)
        => WindowAt(screenX, screenY, includeOverlay) is { ProcessId: var pid } && pid == Environment.ProcessId;
    internal ClientPoint? CapturePoint(int screenX, int screenY)
    {
        if (IsDesktopMode)
        {
            var frame = GetTargetFrame();
            return screenX >= frame.X && screenY >= frame.Y && screenX < frame.X + frame.Width && screenY < frame.Y + frame.Height
                ? new(screenX, screenY, frame.Width, frame.Height, frame.Dpi, CoordinateSpace.Desktop) : null;
        }
        var selected = target;
        if (selected is null || !TargetAvailable || Query(selected) is not { Exists: true } state) return null;
        var point = new ClientPoint(screenX - state.X, screenY - state.Y, state.Width, state.Height, Dpi(state.Scale));
        return point.IsValid && WindowAt(screenX, screenY)?.Id == selected.Handle ? point : null;
    }

    private bool CoordinatesAvailable(SequencePlan? plan)
    {
        if (IsDesktopMode && plan is not null)
        {
            try { var frame = GetTargetFrame(); plan.ValidateDesktopMode(frame.Width, frame.Height, frame.Dpi); return true; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
        }
        if (plan is null || !plan.Inputs.Any(i => i.Position is not null) && !plan.Conditions.Any(c => c.Definition.Region is not null)) return true;
        var selected = target;
        if (selected is null || Query(selected) is not { Exists: true } client) return false;
        int dpi = Dpi(client.Scale);
        return plan.Inputs.All(i => i.Position is null || i.Position.Matches(client.Width, client.Height, dpi)
            && (i.Position.Space != CoordinateSpace.Desktop || i.Position.X >= client.X && i.Position.Y >= client.Y
                && i.Position.X < client.X + client.Width && i.Position.Y < client.Y + client.Height))
            && plan.Conditions.All(c => c.Definition.Region is null
                || c.Definition.Region.Origin.Space == CoordinateSpace.WindowClient && c.Definition.Region.Origin.Matches(client.Width, client.Height, dpi));
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
                bool automatic;
                lock (gate)
                {
                    automatic = playbackControl?.Automatic == true;
                    if (RunPauseStatus.After(paused, automatic, stop.IsCancellationRequested, reason) is { } status) (state, reason) = status;
                    targetBackSince = -1;
                }
                // The timer only runs while a pause can resume by itself; it checks every 100 ms.
                SetAutoResumePolling(paused && automatic);
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
                    Volatile.Write(ref activeControls, started && action.Plan.Inputs.Any(i => i.Control.Kind is InputKind.PointerMove or InputKind.Look || (i.Motion?.MoveDuration > TimeSpan.Zero || i.Motion?.UsePreviousPause == true))
                        ? [new(InputKind.PointerMove, 0)] : []);
                    if (started && !CoordinatesAvailable(action.Plan)) throw new InvalidOperationException("Recalibrate the action positions before playback.");
                    // Only a toggle that is still held makes the rest of the run "Toggles"; otherwise the run stays named after the
                    // Action List, so the log does not call a finished list a toggle run.
                    activity = started || actionToggleSession is not { ActiveIds.Length: > 0 } ? action.Name : "Toggles";
                    reason = started ? "Actions running alongside any enabled holds." : actionToggleSession?.LastActionCancelled == true
                        ? "Actions stopped. Any active toggles remain held." : "Actions finished. Any active toggles remain held.";
                    state = "Running";
                }
                NotifyChanged();
            };
            result = !power.Succeeded
                ? new ClickRunResult(ClickRunOutcome.Faulted, 0, 0, true, "The desktop rejected the keep-awake request. Review power settings before starting again.")
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
                else { state = "Could not continue"; reason = inputRejection is { } rejection ? $"{result.Error}: {rejection}" : result.Error ?? (plan.LeaveCursorFree ? "The cursor-free click was rejected. Keep the target open at its original size; try normal clicks if this app does not accept window messages." : TargetAvailable ? "Put the pointer inside the Click target and release held buttons or keys, then press Start again. The target may also ignore input from GNOME's virtual devices." : "The Click target is not the active window. Select it, then press Start again."); }
                if (result.CleanupSucceeded) cursorFreeActive = false;
                cleanupPendingRecovery = !result.CleanupSucceeded;
                busy = false; cancellation = null; stop.Dispose();
                playbackControl?.Dispose(); playbackControl = null; executor.Playback = null; executor.PlaybackPaused = null; runningRequest = null;
                executor.PauseOnTargetLoss = false; targetBackSince = -1; SetAutoResumePolling(false);
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
            workReady.Dispose(); targetProcess?.Dispose(); autoResumeTimer.Dispose();
        }
        Control.Dispose();
    }
}


