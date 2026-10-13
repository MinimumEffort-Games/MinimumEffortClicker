using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Vision;

namespace MinimumEffortClicker.Windows;

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
            if (condition.Kind == ConditionKind.WindowActive)
            {
                long sample = 0;
                return waiter.Wait(condition.Policy, new Observation(_ =>
                {
                    if (targetInvalidated || !ProbeNative.IsWindow(selected.Handle)
                        || ProbeNative.GetWindowThreadProcessId(selected.Handle, out uint pid) == 0 || pid != selected.ProcessId)
                        return new(ConditionObservationState.Unavailable, 0, sample++);
                    return new(TargetAvailable ? ConditionObservationState.Met : ConditionObservationState.NotMet, 0, sample++);
                }), budget.Token, condition.CheckOnly, testing, observed, service);
            }
            using var capture = new WindowRegionCapture(selected.Handle, condition.Region!, () => CursorFreeActive ? BackgroundTargetAvailable : TargetAvailable, requireForeground: !CursorFreeActive);
            RasterSample? baseline = null;
            var template = condition.Template?.Decode();
            return waiter.Wait(condition.Policy, new Observation(token =>
            {
                var frame = capture.Capture(token);
                bool matches;
                if (condition.Kind == ConditionKind.RegionChanged)
                {
                    baseline ??= frame.Image;
                    matches = RegionComparison.Changed(baseline, frame.Image, condition.Tolerance, condition.ChangedFraction);
                }
                else if (condition.Kind is ConditionKind.ImageAppears or ConditionKind.ImageDisappears)
                {
                    var match = RegionComparison.Find(frame.Image, template!, condition.Tolerance, token);
                    if (match == VisualMatch.Ambiguous) return new(ConditionObservationState.Ambiguous, 0, frame.FrameId);
                    matches = condition.Kind == ConditionKind.ImageAppears ? match == VisualMatch.Met : match == VisualMatch.NotMet;
                }
                else
                {
                    matches = RegionComparison.MatchesColorCondition(frame.Image, condition.Kind, condition.Color, condition.Tolerance);
                }
                return new(matches ? ConditionObservationState.Met : ConditionObservationState.NotMet, 0, frame.FrameId);
            }), budget.Token, condition.CheckOnly, testing, observed, service);
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
            var result = EvaluateCondition(condition with { CheckOnly = false }, cancelled.Token, true, observation =>
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
            using var capture = new WindowRegionCapture(selected.Handle, region, () => TargetAvailable, authoringPreview: true);
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
            using var capture = new WindowRegionCapture(selected.Handle, region, () => TargetAvailable);
            var image = capture.Capture(cancelled.Token).Image;
            captured = true;
            return image;
        }
        finally { EndTargetPreview(captured ? "Template captured into the draft only. Playback remains stopped." : "Template capture ended. Playback remains stopped."); }
    }
}
