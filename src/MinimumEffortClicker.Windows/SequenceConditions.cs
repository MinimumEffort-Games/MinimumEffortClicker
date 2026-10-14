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
            if (selected is null)
            {
                // Desktop mode: the area is in screen pixels on any monitor and is read straight from the screen.
                if (condition.Kind == ConditionKind.WindowActive || condition.Region is not { Origin.Space: CoordinateSpace.Desktop } area)
                    return new(ConditionOutcome.Unavailable, 0);
                long screenFrame = 0;
                return waiter.Wait(condition.Policy, new Observation(token =>
                {
                    // The screen can be briefly unreadable or a different size (locked screen, a UAC prompt, a game
                    // switching resolution). That is "not there yet": the wait keeps looking until the area is back.
                    // A test reports it, so a wrong area is not mistaken for a colour that is simply missing.
                    var frame = GetTargetFrame();
                    var unreadable = new ConditionObservation(testing ? ConditionObservationState.Unavailable : ConditionObservationState.NotMet, 0, screenFrame);
                    if (!area.Origin.Matches(frame.Width, frame.Height, frame.Dpi)) return unreadable;
                    byte[] pixels;
                    try { pixels = DesktopScreenshot.CaptureArea(area.Origin.X, area.Origin.Y, area.Width, area.Height, token); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return unreadable; }
                    return Judge(new RasterSample(area.Width, area.Height, pixels), ++screenFrame, token);
                }), budget.Token, observed, service);
            }
            if (condition.Kind == ConditionKind.WindowActive)
            {
                long sample = 0;
                return waiter.Wait(condition.Policy, new Observation(_ =>
                {
                    if (targetInvalidated || !ProbeNative.IsWindow(selected.Handle)
                        || ProbeNative.GetWindowThreadProcessId(selected.Handle, out uint pid) == 0 || pid != selected.ProcessId)
                        return new(ConditionObservationState.Unavailable, 0, sample++);
                    return new(TargetAvailable ? ConditionObservationState.Met : ConditionObservationState.NotMet, 0, sample++);
                }), budget.Token, observed, service);
            }
            if (condition.Region is not { Origin.Space: CoordinateSpace.WindowClient } windowArea) return new(ConditionOutcome.Unavailable, 0);
            using var capture = new WindowRegionCapture(selected.Handle, windowArea, () => CursorFreeActive ? BackgroundTargetAvailable : TargetAvailable, requireForeground: !CursorFreeActive);
            long lastFrame = 0;
            return waiter.Wait(condition.Policy, new Observation(token =>
            {
                CapturedRegion frame;
                // The window went behind another one, was resized or closed: report it as unavailable rather than failing
                // the whole run, so playback can wait for the window to come back.
                try { frame = capture.Capture(token); }
                catch (InvalidOperationException) { return new(ConditionObservationState.Unavailable, 0, lastFrame); }
                lastFrame = frame.FrameId;
                return Judge(frame.Image, frame.FrameId, token);
            }), budget.Token, observed, service);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !stop.IsCancellationRequested)
        { return new(ConditionOutcome.Stopped, 0); }
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

    /// <summary>
    /// A still picture of the Click target window for the area and colour picker, with the frame it was taken in.
    /// The window may stay behind others: Windows captures its own content. A minimised window is restored without
    /// taking focus. Sends no input and never moves the clicker.
    /// </summary>
    public (WindowSnapshot Picture, TargetClientFrame Frame) CaptureTargetPicture(CancellationToken stop)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(stop);
        RestoreTargetWithoutFocus(cancelled.Token);
        BeginTargetPreview(reason => { _ = cancelled.CancelAsync(); }, needsFront: false);
        try
        {
            var selected = target ?? throw new InvalidOperationException("Choose a Click target first.");
            var frame = GetTargetFrame();
            var region = new CaptureRegion(new(0, 0, frame.Width, frame.Height, frame.Dpi), frame.Width, frame.Height);
            using var capture = new WindowRegionCapture(selected.Handle, region, () => TargetIdentityAvailable, authoringPreview: true, requireForeground: false);
            return (capture.CapturePreview(cancelled.Token), frame);
        }
        finally { EndTargetPreview("Picture ready. Playback remains stopped."); }
    }

    /// <summary>Window capture needs a window that is not minimised; restore it without bringing it to the front.</summary>
    private void RestoreTargetWithoutFocus(CancellationToken stop)
    {
        if (target is not { } selected || !TargetIdentityAvailable || !ProbeNative.IsIconic(selected.Handle)) return;
        DesktopNative.ShowWindow(selected.Handle, 4); // SW_SHOWNOACTIVATE
        // Give the restore animation time to finish before the frame is read.
        for (int i = 0; i < 20 && ProbeNative.IsIconic(selected.Handle); i++) Task.Delay(25, stop).Wait(stop);
        Task.Delay(250, stop).Wait(stop);
    }

    /// <summary>Desktop mode: a still picture of one monitor for the area and colour picker. Sends no input.</summary>
    public WindowSnapshot CaptureMonitorPreview(DesktopMonitor monitor, CancellationToken stop)
    {
        if (!IsDesktopMode) throw new InvalidOperationException("Choose Desktop as the Click target to pick on a monitor.");
        return new(monitor.Width, monitor.Height, DesktopScreenshot.CaptureArea(monitor.X, monitor.Y, monitor.Width, monitor.Height, stop), 0);
    }

    public RasterSample CaptureTemplate(CaptureRegion region, CancellationToken stop)
    {
        region.Validate();
        if (region.Width > 64 || region.Height > 64) throw new ArgumentException("Capture a template of at most 64×64 pixels; enlarge the search region afterwards if needed.");
        // Monitor areas are cut from the app's monitor pictures, taken with the clicker out of the way.
        if (region.Origin.Space != CoordinateSpace.WindowClient) throw new InvalidOperationException("This area is on a monitor. Capture it from the monitor picker.");
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(stop);
        RestoreTargetWithoutFocus(cancelled.Token);
        BeginTargetPreview(reason => { _ = cancelled.CancelAsync(); }, needsFront: false);
        bool captured = false;
        try
        {
            var selected = target ?? throw new InvalidOperationException("Choose a Click target first.");
            // Like the area picture, the template is read from the window itself; it may stay behind other windows.
            using var capture = new WindowRegionCapture(selected.Handle, region, () => TargetIdentityAvailable, requireForeground: false);
            var image = capture.Capture(cancelled.Token).Image;
            captured = true;
            return image;
        }
        finally { EndTargetPreview(captured ? "Template captured into the draft only. Playback remains stopped." : "Template capture ended. Playback remains stopped."); }
    }
}
