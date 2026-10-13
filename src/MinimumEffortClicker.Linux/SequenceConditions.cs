using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Linux;

public sealed partial class DesktopRuntime
{
    private sealed class ConcurrentConditionWait(IClock clock, ClickExecutor executor) : IWaitStrategy
    {
        public void WaitUntil(TimeSpan deadline, CancellationToken stop)
            => executor.WaitWithConcurrentInputs(deadline - clock.Elapsed, stop);
    }
    private volatile bool observingWindowActive;
    private static bool StartsWithWindowWait(SequencePlan? plan) => plan?.Conditions.FirstOrDefault() is { Offset: var at, Definition.Kind: ConditionKind.WindowActive } condition
        && at == TimeSpan.Zero && (plan.Inputs.Count == 0 || condition.StepIndex < plan.Inputs[0].StepIndex)
        && (plan.Screenshots.Count == 0 || condition.StepIndex < plan.Screenshots[0].StepIndex);

    private sealed class Observation(Func<CancellationToken, ConditionObservation> observe) : IConditionProbe
    { public ConditionObservation Observe(CancellationToken stop) => observe(stop); }

    private ConditionResult EvaluateCondition(ConditionDefinition condition, CancellationToken stop, bool testing = false, Action<ConditionObservation>? observed = null)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var remaining = executor.RemainingRunTime;
        if (!testing && remaining < TimeSpan.FromDays(2)) budget.CancelAfter(remaining);
        observingWindowActive = condition.Kind == ConditionKind.WindowActive && actionToggleSession is null;
        Action? service = !testing && executor.HasConcurrentSession ? () => executor.ServiceConcurrentInputs(stop) : null;
        try
        {
            var selected = target;
            if (selected is null) return new(ConditionOutcome.Unavailable, 0, 0);
            var waiter = new ConditionWaiter(clock, service is null ? new DeadlineWait(clock) : new ConcurrentConditionWait(clock, executor));
            RasterSample? baseline = null;
            var template = condition.Template?.Decode();
            // The same judgement for a window area and a monitor area.
            ConditionObservation Judge(RasterSample image, long frameId, CancellationToken token)
            {
                bool matches;
                if (condition.Kind == ConditionKind.RegionChanged)
                {
                    baseline ??= image;
                    matches = RegionComparison.Changed(baseline, image, condition.Tolerance, condition.ChangedFraction);
                }
                else if (condition.Kind is ConditionKind.ImageAppears or ConditionKind.ImageDisappears)
                {
                    var match = RegionComparison.Find(image, template!, condition.Tolerance, token);
                    if (match == VisualMatch.Ambiguous) return new(ConditionObservationState.Ambiguous, 0, frameId);
                    matches = condition.Kind == ConditionKind.ImageAppears ? match == VisualMatch.Met : match == VisualMatch.NotMet;
                }
                else matches = RegionComparison.MatchesColorCondition(image, condition.Kind, condition.Color, condition.Tolerance);
                return new(matches ? ConditionObservationState.Met : ConditionObservationState.NotMet, 0, frameId);
            }
            if (condition.Kind == ConditionKind.WindowActive)
            {
                long sample = 0;
                return waiter.Wait(condition.Policy, new Observation(_ =>
                {
                    if (targetInvalidated || !Matches(selected, Query(selected)))
                        return new(ConditionObservationState.Unavailable, 0, sample++);
                    return new(TargetAvailable ? ConditionObservationState.Met : ConditionObservationState.NotMet, 0, sample++);
                }), budget.Token, testing, observed, service);
            }
            RegionCapture? capture = null;
            long lastFrame = 0;
            return waiter.Wait(condition.Policy, new Observation(token =>
            {
                CapturedRegion frame;
                // The window went behind another one, was resized or closed: report it as unavailable rather than failing
                // the whole run, so playback can wait for the window to come back.
                try { capture ??= new RegionCapture(Helper, selected.Handle, condition.Region!, () => TargetAvailable); frame = capture.Capture(token); }
                catch (Exception ex) when (ex is InvalidOperationException or ShellHelperException) { return new(ConditionObservationState.Unavailable, 0, lastFrame); }
                lastFrame = frame.FrameId;
                return Judge(frame.Image, frame.FrameId, token);
            }), budget.Token, testing, observed, service);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !stop.IsCancellationRequested)
        { return new(ConditionOutcome.Stopped, 0, 0); }
        finally { observingWindowActive = false; }
    }

    /// <summary>Observes only the selected condition until a match or explicit cancellation. No input sink.</summary>
    public ConditionResult TestCondition(ConditionDefinition condition, CancellationToken stop, Action<ConditionObservation>? observed = null)
    {
        condition.Validate();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(stop);
        stop.ThrowIfCancellationRequested();
        BeginTargetPreview(reason => { _ = cancelled.CancelAsync(); });
        lock (gate) { activity = "Test · Wait Until"; state = "Testing"; reason = "Scanning the selected area. Stop All aborts."; }
        NotifyChanged();
        try
        {
            var result = EvaluateCondition(condition, cancelled.Token, true, observation =>
            {
                lock (gate) activity = observation.State switch
                {
                    ConditionObservationState.NotMet => "Test · False — still scanning",
                    ConditionObservationState.Met => "Test · Confirming match",
                    _ => "Test · " + observation.State
                };
                NotifyChanged(); observed?.Invoke(observation);
            });
            lock (gate) activity = result.Outcome == ConditionOutcome.Matched ? "Test · True" : "Test · " + result.Outcome;
            return result;
        }
        catch (OperationCanceledException) { lock (gate) activity = "Test · Aborted"; throw; }
        finally { EndTargetPreview("Condition test finished. Playback remains stopped."); }
    }

    public WindowSnapshot CaptureAuthoringPreview(CancellationToken stop)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(stop);
        BeginTargetPreview(reason => { _ = cancelled.CancelAsync(); });
        try
        {
            var selected = target ?? throw new InvalidOperationException("Choose a Click target first.");
            var frame = GetTargetFrame();
            var region = new CaptureRegion(new(0, 0, frame.Width, frame.Height, frame.Dpi), frame.Width, frame.Height);
            var capture = new RegionCapture(Helper, selected.Handle, region, () => TargetAvailable, authoringPreview: true);
            return capture.CapturePreview(cancelled.Token);
        }
        finally { EndTargetPreview("Snapshot ready. Playback remains stopped."); }
    }

    public RasterSample CaptureTemplate(CaptureRegion region, CancellationToken stop)
    {
        region.Validate();
        if (region.Width > 64 || region.Height > 64) throw new ArgumentException("Capture a template of at most 64×64 pixels; enlarge the search region afterwards if needed.");
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(stop);
        BeginTargetPreview(reason => { _ = cancelled.CancelAsync(); });
        bool captured = false;
        try
        {
            var selected = target ?? throw new InvalidOperationException("Choose a target first.");
            var capture = new RegionCapture(Helper, selected.Handle, region, () => TargetAvailable);
            var image = capture.Capture(cancelled.Token).Image;
            captured = true;
            return image;
        }
        finally { EndTargetPreview(captured ? "Template captured into the draft only. Playback remains stopped." : "Template capture ended. Playback remains stopped."); }
    }
}
