using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

public enum ClickRunOutcome { Completed, Cancelled, TargetUnavailable, InputRejected, Faulted, CleanupFailed, Paused }
public sealed record ClickRunResult(ClickRunOutcome Outcome, int CompletedCycles, int InputsSent,
    bool CleanupSucceeded, string? Error);
public sealed record ExecutionProgress(int CompletedCycles, string CurrentAction, string NextAction, IReadOnlyList<InputControl> Held);

/// <summary>
/// Runs helpers, action lists and toggle sessions on the scheduler worker, never the UI thread.
/// The composition owner must supply an available stop route and cancel on target/session/physical
/// input invalidation. Each down also rechecks foreground identity. This class installs no OS hooks.
/// One invocation is active at a time; a new run always requires a new explicit Execute call.
/// </summary>
public sealed partial class ClickExecutor(IInputSink sink, IClock wallClock, IWaitStrategy waits, IForegroundTargetProvider targets)
{
    private readonly PlaybackClock clock = new(wallClock);
    public PlaybackControl? Playback { get; set; }
    // Scheduler-thread callback, outside the dispatch gate; used for scoped power/status changes.
    public Action<bool>? PlaybackPaused { get; set; }
    /// <summary>
    /// When the bound target is not in front, wait (an automatic pause) instead of ending the run. Set by the runtime for
    /// a run that targets a window; the runtime resumes the pause once that window is back in front.
    /// </summary>
    public bool PauseOnTargetLoss { get; set; }
    private PlaybackControl? playback;
    private Run? activeRun;
    private Guid activeTarget;
    public Func<ConditionDefinition, CancellationToken, ConditionResult>? EvaluateCondition { private get; set; }
    // Completion means the file has been committed. Native capture and storage stay outside Engine.
    public Func<CancellationToken, Task>? CaptureScreenshot { private get; set; }
    public Func<ClientPoint, ClickMotion, Func<double>, ClientPoint>? PrepareClickPoint { get; set; }
    public Func<ClientPoint, ClientPoint?>? CursorPosition { get; set; }
    private readonly object gate = new();
    private bool executing;
    private readonly List<InputControl> owned = [];
    private ClickRateLimit clickRateLimit = new();
    private TimeSpan? lastClick;
    private long totalCompletedLoops;
    private long totalCompletedClicks;
    private long clickCompletionLimit = long.MaxValue;
    private long completionLimit = long.MaxValue;
    private long activeTimeLimit = long.MaxValue;
    private long sequenceDeadline = long.MaxValue;
    public TimeSpan RemainingRunTime
    {
        get
        {
            var global = ActiveTimeLimit == TimeSpan.MaxValue ? TimeSpan.MaxValue : ActiveTimeLimit - TotalActiveTime;
            var deadline = Interlocked.Read(ref sequenceDeadline);
            var local = deadline == long.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromTicks(deadline) - clock.Elapsed;
            var remaining = global < local ? global : local;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
    public TimeSpan ActiveTimeLimit { get => TimeSpan.FromTicks(Interlocked.Read(ref activeTimeLimit)); set => Interlocked.Exchange(ref activeTimeLimit, value.Ticks); }
    private long activeTicks;
    private long activeSince = -1;
    private readonly object timeGate = new();
    public long CompletionLimit { get => Interlocked.Read(ref completionLimit); set => Interlocked.Exchange(ref completionLimit, value); }
    public TimeSpan TotalActiveTime
    {
        get { lock (timeGate) return TimeSpan.FromTicks(activeTicks + (activeSince >= 0 ? clock.Elapsed.Ticks - activeSince : 0)); }
    }
    public long TotalCompletedLoops => Interlocked.Read(ref totalCompletedLoops);
    public long TotalCompletedClicks => Interlocked.Read(ref totalCompletedClicks);
    public long ClickCompletionLimit { get => Interlocked.Read(ref clickCompletionLimit); set => Interlocked.Exchange(ref clickCompletionLimit, value); }
    public ClickRateLimit ClickRateLimit
    {
        get { lock (gate) return clickRateLimit; }
        set
        {
            ArgumentNullException.ThrowIfNull(value); _ = value.MinimumGap;
            lock (gate) { if (executing) throw new InvalidOperationException("Stop playback before changing the click limit."); clickRateLimit = value; }
        }
    }
    private void CompleteLoop(Run run)
    {
        run.Completed++;
        if (Interlocked.Increment(ref totalCompletedLoops) >= CompletionLimit) run.Outcome = ClickRunOutcome.Cancelled;
    }
    private bool WaitForClick(InputControl control, CancellationToken stop)
    {
        if (control.Kind != InputKind.MouseButton) return true;
        TimeSpan due;
        lock (gate) due = lastClick is { } last ? last + clickRateLimit.MinimumGap : clock.Elapsed;
        return WaitDeadline(due, stop);
    }
    private bool WaitDeadline(TimeSpan due, CancellationToken stop, CancellationToken? changed = null, bool needsTarget = true)
    {
        // Every step waits here first, so a target that left the front pauses the run at the step instead of ending it.
        // Checked before and after each wait: the window may leave while the wait runs, before the runtime's pause arrives.
        bool TargetGone() => needsTarget && !TargetMatches(activeTarget) && RequestTargetLossPause();
        while (true)
        {
        PauseCheckpoint(stop);
        if (TargetGone()) continue;
        stop.ThrowIfCancellationRequested();
        ServiceConcurrentInputs(stop);
        var remaining = RemainingRunTime;
        if (remaining != TimeSpan.MaxValue)
        {
            var budgetEnd = clock.Elapsed + (remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            if (due > budgetEnd) due = budgetEnd;
        }
        var wakeDue = actionSession is not null && NextConcurrentDue < due ? NextConcurrentDue : due;
        var delay = wakeDue - clock.Elapsed;
        var now = wallClock.Elapsed;
        var physicalDue = delay <= TimeSpan.Zero ? now : TimeSpan.FromTicks(delay.Ticks > long.MaxValue - now.Ticks ? long.MaxValue : now.Ticks + delay.Ticks);
        if (actionSession is { } session)
        {
            var sessionChange = session.Changed;
            using var signal = CancellationTokenSource.CreateLinkedTokenSource(sessionChange, playback?.Changed ?? CancellationToken.None, changed ?? CancellationToken.None);
            bool reached = waits.WaitUntilOrChanged(physicalDue, stop, signal.Token);
            if (playback?.IsPaused == true) { PauseCheckpoint(stop); continue; }
            if (changed?.IsCancellationRequested == true) return false;
            if (sessionChange.IsCancellationRequested) continue;
            if (reached && wakeDue < due) continue;
            if (TargetGone()) continue;
            return reached;
        }
        if (playback is { } control)
        {
            using var signal = changed is { } update ? CancellationTokenSource.CreateLinkedTokenSource(control.Changed, update) : null;
            bool reached = waits.WaitUntilOrChanged(physicalDue, stop, signal?.Token ?? control.Changed);
            if (control.IsPaused) { PauseCheckpoint(stop); continue; }
            if (TargetGone()) continue;
            return reached;
        }
        if (changed is { } change) return waits.WaitUntilOrChanged(physicalDue, stop, change);
        waits.WaitUntil(physicalDue, stop); return true;
        }
    }

    // Injectable for deterministic tests; sampled only by the single scheduler worker.
    public Func<double> NextRandom { get; set; } = Random.Shared.NextDouble;

    private sealed class InterruptedPlaybackException : Exception;

    /// <summary>
    /// The target is not in front and this run waits for it (PauseOnTargetLoss): ask for the automatic pause. A pause the
    /// user asked for stays theirs. True when a pause is now pending, so the next wait holds the run there.
    /// </summary>
    private bool RequestTargetLossPause() => PauseOnTargetLoss && playback is { } control && activeRun is not null
        && (control.RequestPause(automatic: true) || control.IsPaused);
    private bool PausePending => playback?.IsPaused == true;
    /// <summary>
    /// A press, click or move was refused. When the target left the front meanwhile (or the automatic pause for that is
    /// already pending), wait for it and send that same input again afterwards; otherwise the refusal ends the run.
    /// </summary>
    private bool PauseForRefusedDown() => (!TargetMatches(activeTarget) || playback?.Automatic == true) && RequestTargetLossPause();
    private void PauseCheckpoint(CancellationToken stop)
    {
        if (playback is not { IsPaused: true } control || activeRun is not { } run) return;
        InputControl[] reacquire;
        bool automatic = control.Automatic;
        lock (gate)
        {
            stop.ThrowIfCancellationRequested();
            bool KeptHold(InputControl i) => sessionHeld.Values.Contains(i) && actionSession?.KeepHold(i) == true;
            // A pause the user asked for mid-click ends the run: replaying a partial click or drag can duplicate side effects.
            // A pause because the target left the front lets the click finish where it is and carries on after it.
            if (!automatic && owned.Any(i => i.Kind == InputKind.MouseButton && !KeptHold(i)))
            {
                run.Outcome = ClickRunOutcome.Paused;
                run.Error = "Interaction interrupted. Inputs released; review and explicitly restart the selected step or stop. Delivered effects cannot be undone.";
                throw new InterruptedPlaybackException();
            }
            // Held keys and hold toggles are pressed again on resume; a finished partial click is not.
            reacquire = owned.Where(i => i.Kind != InputKind.MouseButton || KeptHold(i)).ToArray();
            if (!Release(run)) { run.Outcome = ClickRunOutcome.CleanupFailed; throw new InterruptedPlaybackException(); }
            clock.Pause(); Publish(run, "Paused", automatic ? "Waiting for the target window" : "Manual resume required");
        }
        bool announced = false;
        try
        {
            control.Acknowledge(); PlaybackPaused?.Invoke(true); announced = true;
            while (true)
            {
                control.WaitForResume(stop); stop.ThrowIfCancellationRequested();
                if (TargetMatches(activeTarget)) break;
                if (!PauseOnTargetLoss) { run.Outcome = ClickRunOutcome.TargetUnavailable; throw new InterruptedPlaybackException(); }
                control.Repause();
            }
            lock (gate)
            {
                stop.ThrowIfCancellationRequested();
                foreach (var input in reacquire)
                {
                    if (sessionHeld.Values.Contains(input) && actionSession?.KeepHold(input) == false) continue;
                    if (!TargetMatches(activeTarget) || !sink.TrySend(new(input, InputEdge.Down)))
                    { run.Outcome = ClickRunOutcome.InputRejected; throw new InterruptedPlaybackException(); }
                    owned.Add(input); run.Sent++;
                }
                Publish(run, automatic ? "Resumed: target back in front" : "Resumed explicitly");
            }
            control.CompleteResume();
        }
        finally
        {
            clock.Resume();
            // Every paused=true is paired with a paused=false, even when the pause ends by cancellation.
            if (announced) PlaybackPaused?.Invoke(false);
        }
    }

    private ConditionResult ObserveCondition(ConditionDefinition definition, CancellationToken stop)
    {
        while (true)
        {
            PauseCheckpoint(stop);
            using var observation = playback is { } control ? CancellationTokenSource.CreateLinkedTokenSource(stop, control.Changed) : null;
            try
            {
                var result = EvaluateCondition?.Invoke(definition, observation?.Token ?? stop) ?? new(ConditionOutcome.Unavailable, 0);
                if (playback?.IsPaused == true) continue; // discard pre-pause observations
                // Capture fails while the target is behind another window: wait for it to return, then look again.
                if (result.Outcome == ConditionOutcome.Unavailable && PauseOnTargetLoss && playback is { } lost && !TargetMatches(activeTarget)
                    && lost.RequestPause(automatic: true))
                    continue;
                return result;
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested && playback?.IsPaused == true) { }
        }
    }
    private void AcceptedClick(InputControl control) { if (control.Kind == InputKind.MouseButton) lastClick = clock.Elapsed; }
    private ExecutionProgress progress = new(0, "Stopped", "None", Array.Empty<InputControl>());
    // UI/runtime readers must never wait on the dispatch gate while SendInput is in flight.
    public ExecutionProgress Progress => Volatile.Read(ref progress);
    private void Publish(Run run, string? current = null, string? next = null)
    {
        var previous = progress;
        Volatile.Write(ref progress, new(run.Completed, current ?? previous.CurrentAction,
            next ?? previous.NextAction, Array.AsReadOnly(owned.ToArray())));
    }

    // Lock-free on purpose: the hook/control thread asks these while SendInput may be in flight under the gate,
    // and SendInput itself waits for that hook thread. Every ownership change publishes a fresh snapshot before
    // the gate is released, so readers see at most a microsecond-old ledger, never a deadlock.
    public bool HasOwnedInput => Progress.Held.Count != 0;
    public bool Owns(InputControl input) => Progress.Held.Contains(input);

    private sealed class Run
    {
        public ClickRunOutcome? Outcome;
        public int Completed;
        public int Sent;
        public string? Error;
    }

    public ClickRunResult Execute(HelperPlan plan, Guid targetId, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var errors = plan.Validate();
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(plan));
        return ExecuteSession(targetId, stop, run =>
        {
            string action = RecordingNormalizer.Label(plan.Control);
            var origin = clock.Elapsed;
            var next = origin;
            var end = plan.ActiveDuration is { } duration ? origin + duration : TimeSpan.MaxValue;
            for (int cycle = 0; plan.Count == 0 || cycle < plan.Count; cycle++)
            {
                if (next >= end) break;
                WaitDeadline(next, stop);
                WaitForClick(plan.Control, stop);
                if (clock.Elapsed >= end) break;
                TimeSpan releaseAt;
                bool retry = false;
                lock (gate)
                {
                    if (!CanContinue(run, targetId, stop)) break;
                    // The target left: wait for it, then send this same press once.
                    if (PausePending) retry = true;
                    else if (!sink.TrySend(new(plan.Control, InputEdge.Down, plan.Position)))
                    {
                        if (PauseForRefusedDown()) retry = true;
                        else { run.Outcome = ClickRunOutcome.InputRejected; break; }
                    }
                }
                if (retry) { cycle--; continue; }
                lock (gate)
                {
                    AcceptedClick(plan.Control);
                    if (plan.Mode != HelperMode.Scroll) owned.Add(plan.Control);
                    run.Sent++;
                    Publish(run, action, plan.Mode == HelperMode.Scroll ? "Next cycle" : "Release " + action);
                    releaseAt = plan.Mode == HelperMode.Hold ? end : clock.Elapsed + TimingSample.Range(plan.PressDuration, plan.MaximumPressDuration, NextRandom);
                    if (releaseAt > end) releaseAt = end;
                }
                if (plan.Mode != HelperMode.Scroll) WaitDeadline(releaseAt, stop);
                lock (gate)
                {
                    if (!CanContinue(run, targetId, stop)) break;
                    if (!Release(run)) { run.Outcome = ClickRunOutcome.InputRejected; break; }
                    CompleteLoop(run);
                    Publish(run, "Waiting", action);
                }
                if (run.Outcome is not null) break;
                if (plan.Mode == HelperMode.Hold || plan.Count > 0 && cycle == plan.Count - 1) break;
                var interval = TimingSample.Range(plan.Interval, plan.MaximumInterval, NextRandom);
                next += interval;
                var now = clock.Elapsed;
                if (next < now)
                {
                    if (plan.MaximumInterval is not null) next = now + interval;
                    else
                    {
                        // Skip every missed repeat slot rather than issuing a catch-up burst.
                        long slots = (now - origin).Ticks / plan.Interval.Ticks + 1;
                        next = origin + TimeSpan.FromTicks(checked(slots * plan.Interval.Ticks));
                    }
                }
            }
        });
    }

    public ClickRunResult Execute(SequencePlan plan, Guid targetId, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ExecuteSession(targetId, stop, run => ExecuteSequenceLoops(plan, targetId, stop, run));
    }
    private void ExecuteSequenceLoops(SequencePlan plan, Guid targetId, CancellationToken stop, Run run)
    {
            if (plan.Repetition.ActiveDuration is { } duration) Interlocked.Exchange(ref sequenceDeadline, (clock.Elapsed + duration).Ticks);
            ClientPoint? carried = null;
            // Once actions run in the first loop only; every later loop runs the list without them.
            var later = plan.LaterLoops ?? plan;
            // A list made only of Once actions has nothing left to repeat: the run is finished after the first loop.
            bool onlyOnce = plan.LaterLoops is { IsEmpty: true };
            for (int loop = 0; plan.Repetition.Count == 0 || loop < plan.Repetition.Count; loop++)
            {
                var loopStart = clock.Elapsed;
                bool loopsAgain = (plan.Repetition.Count == 0 || loop + 1 < plan.Repetition.Count) && !onlyOnce;
                ExecuteSequenceSegment(loop == 0 ? plan : later, targetId, stop, run, loopsAgain ? later : null, ref carried);
                lock (gate)
                {
                    if (!CanContinue(run, targetId, stop)) return;
                    CompleteLoop(run); Publish(run);
                }
                if (run.Outcome is not null || !loopsAgain) return;
                // Even an empty or immediately-true loop has a bounded wake rate.
                var next = clock.Elapsed + plan.Repetition.DelayBetweenLoops;
                if (next < loopStart + TimeSpan.FromMilliseconds(10)) next = loopStart + TimeSpan.FromMilliseconds(10);
                WaitDeadline(next, stop);
                lock (gate) { if (!CanContinue(run, targetId, stop)) return; }
            }
    }
    /// <summary>
    /// One pass over the list. <paramref name="carried"/> is the first click's position when the previous loop already moved the
    /// pointer there during its closing pause; on return it holds the position prepared for the next loop, or null.
    /// <paramref name="next"/> is the list the next loop runs, or null when this is the last loop.
    /// </summary>
    private void ExecuteSequenceSegment(SequencePlan plan, Guid targetId, CancellationToken stop, Run run, SequencePlan? next, ref ClientPoint? carried)
    {
            plan = plan.SampleTiming(NextRandom);
            var clickPositions = new Dictionary<Guid, ClientPoint>();
            var stepPositions = new Dictionary<int, ClientPoint>();
            if (carried is { } arrived && plan.Inputs.Count > 0) stepPositions[plan.Inputs[0].StepIndex] = arrived;
            carried = null;
            var origin = clock.Elapsed;
            for (int index = 0, conditionIndex = 0, screenshotIndex = 0; index < plan.Inputs.Count || conditionIndex < plan.Conditions.Count || screenshotIndex < plan.Screenshots.Count;)
            {
                if (screenshotIndex < plan.Screenshots.Count
                    && (index == plan.Inputs.Count || Before(plan.Screenshots[screenshotIndex].Offset, plan.Screenshots[screenshotIndex].StepIndex, plan.Inputs[index].Offset, plan.Inputs[index].StepIndex))
                    && (conditionIndex == plan.Conditions.Count || Before(plan.Screenshots[screenshotIndex].Offset, plan.Screenshots[screenshotIndex].StepIndex, plan.Conditions[conditionIndex].Offset, plan.Conditions[conditionIndex].StepIndex)))
                {
                    var screenshot = plan.Screenshots[screenshotIndex++];
                    WaitDeadline(origin + screenshot.Offset, stop);
                    bool waitForTarget;
                    lock (gate)
                    {
                        if (!CanContinue(run, targetId, stop)) return;
                        waitForTarget = PausePending;
                    }
                    // The target left: wait for it, then take this screenshot.
                    if (waitForTarget) { screenshotIndex--; continue; }
                    lock (gate)
                    {
                        if (owned.Any(i => !sessionHeld.Values.Contains(i))) throw new InvalidOperationException("Release action inputs before a screenshot.");
                        Publish(run, screenshot.Name, "Save image, then pause");
                    }
                    var before = clock.Elapsed;
                    using var captureStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    try
                    {
                        var capture = CaptureScreenshot?.Invoke(captureStop.Token) ?? throw new InvalidOperationException("Screenshot saving is unavailable.");
                        // If Stop returns before the platform task settles, still observe its failure.
                        _ = capture.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        // Keep Stop, Pause, toggle changes and active-time limits responsive during encoding.
                        while (!capture.IsCompleted)
                        {
                            WaitDeadline(clock.Elapsed + TimeSpan.FromMilliseconds(25), stop);
                            lock (gate) { if (!CanContinue(run, targetId, stop)) return; }
                        }
                        capture.GetAwaiter().GetResult();
                        stop.ThrowIfCancellationRequested();
                    }
                    finally { captureStop.Cancel(); }
                    // Disk/capture latency extends the sequence; never catch up or overlap saves.
                    origin += clock.Elapsed - before;
                    continue;
                }
                if (conditionIndex < plan.Conditions.Count && (index == plan.Inputs.Count
                    || plan.Conditions[conditionIndex].Offset < plan.Inputs[index].Offset
                    || plan.Conditions[conditionIndex].Offset == plan.Inputs[index].Offset && plan.Conditions[conditionIndex].StepIndex < plan.Inputs[index].StepIndex))
                {
                    var condition = plan.Conditions[conditionIndex++];
                    // A "window is active" wait observes an inactive target on purpose; every other step needs it in front.
                    WaitDeadline(origin + condition.Offset, stop, needsTarget: condition.Definition.Kind != ConditionKind.WindowActive);
                    lock (gate)
                    {
                        // The same budget checks as CanContinue, without the foreground check: a window wait may observe an inactive target.
                        if (stop.IsCancellationRequested || RemainingRunTime == TimeSpan.Zero || TotalCompletedLoops >= CompletionLimit || TotalCompletedClicks >= ClickCompletionLimit)
                        { run.Outcome = ClickRunOutcome.Cancelled; return; }
                        if (owned.Any(i => !sessionHeld.Values.Contains(i))) throw new InvalidOperationException("Conditions cannot wait with action inputs held.");
                        Publish(run, "Waiting: " + condition.Name, "Continue only after a fresh match");
                    }
                    var beforeCondition = clock.Elapsed;
                    var result = ObserveCondition(condition.Definition, stop);
                    origin += clock.Elapsed - beforeCondition;
                    if (result.Outcome != ConditionOutcome.Matched)
                    {
                        run.Outcome = ClickRunOutcome.Cancelled;
                        run.Error = "Stopped: " + result.Outcome switch
                        {
                            ConditionOutcome.Ambiguous => $"'{condition.Name}' found the image more than once. Make its area smaller.",
                            ConditionOutcome.Unavailable => $"'{condition.Name}' could not see its area. Bring the Click target back, or pick the area again.",
                            _ => $"the run's time limit ran out while '{condition.Name}' was waiting.",
                        };
                        return;
                    }
                    continue;
                }
                var item = plan.Inputs[index];
                lock (gate) Publish(run, next: (item.Down ? "Press / move: " : "Release: ") + item.Name);
                var due = origin + item.Offset;
                WaitDeadline(item.Down && item.Motion?.UsePreviousPause == true ? due - item.PauseBefore : due, stop);
                // Stretch a late sequence instead of shortening a hold or bursting missed steps.
                if (clock.Elapsed > due) { origin += clock.Elapsed - due; due = clock.Elapsed; }
                var position = item.Position;
                if (item.Down && item.Motion is { } motion && position is { } saved)
                {
                    bool sameClick = stepPositions.TryGetValue(item.StepIndex, out var previousPoint);
                    position = sameClick ? previousPoint! : PrepareClickPoint is { } prepare ? prepare(saved, motion, NextRandom) : motion.Offset(saved, NextRandom);
                    stepPositions[item.StepIndex] = position;
                    clickPositions[item.ActionId] = position;
                    var movementStarted = clock.Elapsed;
                    var moveDuration = motion.UsePreviousPause ? due - movementStarted : motion.MoveDuration;
                    if (!sameClick && moveDuration >= TimeSpan.FromMilliseconds(32) && CursorPosition?.Invoke(position) is { } start
                        && !MovePointer(run, targetId, stop, start, position, movementStarted, moveDuration, motion.UsePreviousPause ? 20 : 16, item)) return;
                    if (!motion.UsePreviousPause) { var extra = clock.Elapsed - movementStarted; origin += extra; due += extra; }
                }
                else if (!item.Down && clickPositions.TryGetValue(item.ActionId, out var clickedAt)) position = clickedAt;
                WaitDeadline(due, stop);
                if (item.Down) WaitForClick(item.Control, stop);
                if (clock.Elapsed > due) origin += clock.Elapsed - due;
                bool retry = false;
                lock (gate)
                {
                    if (!CanContinue(run, targetId, stop)) return;
                    var before = clock.Elapsed;
                    if (item.Down)
                    {
                        // The target left (before or while this input went out): wait for it, then send only this input,
                        // once. Inputs that already went out are not sent again. Releases below never wait.
                        if (PausePending) retry = true;
                        else if (!sink.TrySend(new(item.Control, InputEdge.Down, position, item.ClickCount)))
                        {
                            if (PauseForRefusedDown()) retry = true;
                            else { run.Outcome = ClickRunOutcome.InputRejected; run.Error = $"Action {item.StepIndex + 1}: {item.Name}"; return; }
                        }
                        else
                        {
                            AcceptedClick(item.Control);
                            if (item.Control.Kind is not (InputKind.Scroll or InputKind.HorizontalScroll or InputKind.PointerMove)) owned.Add(item.Control);
                            run.Sent++;
                        }
                    }
                    else if (!Release(run, item.Control, position)) { run.Outcome = ClickRunOutcome.InputRejected; run.Error = $"Action {item.StepIndex + 1}: {item.Name}"; return; }
                    if (!retry) { Publish(run, item.Name); origin += clock.Elapsed - before; }
                }
                if (retry) continue;
                index++;
            }
            lock (gate) Publish(run, owned.Count == 0 ? "Waiting" : null, "Finish sequence");
            var end = origin + plan.Duration;
            // Loop is on and the list ends with a pause: that pause is the travel time to the first click of the next loop,
            // so the pointer moves during it instead of jumping there when the next loop starts.
            if (next is { Inputs.Count: > 0 } && end - clock.Elapsed >= TimeSpan.FromMilliseconds(32)
                && next.Inputs[0] is { Down: true, Motion: { UsePreviousPause: true } lead, Position: { } first } leadIn && leadIn.PauseBefore == TimeSpan.Zero)
            {
                bool free; lock (gate) free = owned.Count == 0;
                var position = PrepareClickPoint is { } prepare ? prepare(first, lead, NextRandom) : lead.Offset(first, NextRandom);
                var started = clock.Elapsed;
                if (free && CursorPosition?.Invoke(position) is { } start)
                {
                    if (!MovePointer(run, targetId, stop, start, position, started, end - started, 20, leadIn)) return;
                    carried = position;
                }
            }
            WaitDeadline(end, stop);
            lock (gate) { CanContinue(run, targetId, stop); }
    }

    /// <summary>Sends paced pointer moves from <paramref name="start"/> to <paramref name="position"/>, finishing at started + duration. False when the run ended.</summary>
    private bool MovePointer(Run run, Guid targetId, CancellationToken stop, ClientPoint start, ClientPoint position, TimeSpan started, TimeSpan duration, int paceMilliseconds, ScheduledInput item)
    {
        int count = (int)Math.Floor(duration.TotalMilliseconds / paceMilliseconds);
        for (int part = 1; part <= count; part++)
        {
            WaitDeadline(started + TimeSpan.FromTicks(duration.Ticks * part / count), stop);
            lock (gate)
            {
                if (!CanContinue(run, targetId, stop)) return false;
                if (owned.Any(i => i.Kind == InputKind.MouseButton)) throw new InvalidOperationException("Release held mouse buttons before smooth movement.");
                var point = ClickMotion.Interpolate(start, position, (double)part / count);
                // The target left: wait for it, then make this same move.
                if (PausePending) { part--; continue; }
                if (!sink.TrySend(new(new(InputKind.PointerMove, 0), InputEdge.Down, point)))
                {
                    if (PauseForRefusedDown()) { part--; continue; }
                    run.Outcome = ClickRunOutcome.InputRejected; run.Error = $"Action {item.StepIndex + 1}: {item.Name}"; return false;
                }
                run.Sent++;
            }
        }
        return true;
    }

    private static bool Before(TimeSpan first, int firstStep, TimeSpan second, int secondStep) =>
        first < second || first == second && firstStep < secondStep;

    private bool CanContinue(Run run, Guid targetId, CancellationToken stop)
    {
        if (stop.IsCancellationRequested) run.Outcome ??= ClickRunOutcome.Cancelled;
        if (TotalCompletedLoops >= CompletionLimit) run.Outcome ??= ClickRunOutcome.Cancelled;
        if (TotalCompletedClicks >= ClickCompletionLimit) run.Outcome ??= ClickRunOutcome.Cancelled;
        if (RemainingRunTime == TimeSpan.Zero) run.Outcome ??= ClickRunOutcome.Cancelled;
        if (run.Outcome is not null) return false;
        if (TargetMatches(targetId)) return true;
        // The target left: with PauseOnTargetLoss the run waits for it at its next wait. Callers about to send check
        // PausePending first, so nothing goes out meanwhile.
        if (RequestTargetLossPause()) return true;
        run.Outcome = ClickRunOutcome.TargetUnavailable; return false;
    }

    private ClickRunResult ExecuteSession(Guid targetId, CancellationToken stop, Action<Run> execute)
    {
        if (targetId == Guid.Empty) throw new ArgumentException("A bound target instance is required.", nameof(targetId));
        lock (gate)
        {
            if (executing) throw new InvalidOperationException("Another click run is active.");
            if (owned.Count != 0) throw new InvalidOperationException("Owned input cleanup must succeed before another run.");
            executing = true;
            Interlocked.Exchange(ref sequenceDeadline, long.MaxValue);
            lock (timeGate) activeSince = clock.Elapsed.Ticks;
        }

        var run = new Run();
        playback = Playback; activeRun = run; activeTarget = targetId;
        lock (gate) Publish(run, "Starting", "First action");
        CancellationTokenRegistration registration = default;
        try
        {
            registration = stop.Register(() =>
            {
                lock (gate)
                {
                    run.Outcome ??= ClickRunOutcome.Cancelled;
                    Release(run); // Cleanup is independent of the scheduler wait and UI responsiveness.
                }
            });
            execute(run);
        }
        catch (InterruptedPlaybackException) { }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            lock (gate) run.Outcome ??= ClickRunOutcome.Cancelled;
        }
        catch (Exception exception)
        {
            lock (gate) { run.Outcome = ClickRunOutcome.Faulted; run.Error = exception.Message; }
        }
        finally
        {
            // Never join a cancellation callback while holding the dispatch gate.
            registration.Dispose();
            lock (gate)
            {
                if (!Release(run)) run.Outcome = ClickRunOutcome.CleanupFailed;
                Publish(run, run.Outcome == ClickRunOutcome.CleanupFailed ? "Cleanup pending" : "Stopped", "None");
                executing = false;
                playback?.End(); playback = null; activeRun = null;
                Interlocked.Exchange(ref sequenceDeadline, long.MaxValue);
                lock (timeGate) { activeTicks += clock.Elapsed.Ticks - activeSince; activeSince = -1; }
            }
        }
        return new(run.Outcome ?? ClickRunOutcome.Completed, run.Completed, run.Sent,
            run.Outcome != ClickRunOutcome.CleanupFailed, run.Error);
    }

    /// <summary>Explicit recovery after a rejected up; never releases unrelated inputs or starts a run.</summary>
    public bool RetryCleanup()
    {
        lock (gate)
        {
            if (executing) throw new InvalidOperationException("Wait for the active run to finish before retrying cleanup.");
            return Release(new Run());
        }
    }

    private bool TargetMatches(Guid targetId) => targets.Current is { IsAvailable: true } target && target.InstanceId == targetId;

    // Called under gate; only successfully accepted downs enter this ownership ledger.
    private bool Release(Run run, InputControl? only = null, ClientPoint? position = null)
    {
        bool success = true;
        foreach (var input in owned.AsEnumerable().Reverse().ToArray())
        {
            if (only is { } requested && input != requested) continue;
            try
            {
                if (!sink.TrySend(new(input, InputEdge.Up, position))) { run.Error = "The input adapter rejected owned-input release."; success = false; continue; }
                owned.Remove(input); run.Sent++;
                if (input.Kind == InputKind.MouseButton) Interlocked.Increment(ref totalCompletedClicks);
                Publish(run);
            }
            catch (Exception exception) { run.Error = exception.Message; success = false; }
        }
        return success;
    }
}
