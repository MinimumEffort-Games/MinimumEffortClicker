using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class ClickExecutorTests
{
    [TestMethod]
    public void ScreenshotOnlyLoopsWaitForEachSaveThenPauseWithoutSendingInput()
    {
        var f = new Fixture(); var captures = new List<TimeSpan>();
        f.Executor.CaptureScreenshot = _ => { captures.Add(f.Clock.Elapsed); f.Clock.Advance(TimeSpan.FromMilliseconds(300)); return Task.CompletedTask; };
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([SequenceStep.Screenshot()], Repetition: new(3))), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        CollectionAssert.AreEqual(new double[] { 0, 1300, 2600 }, captures.Select(t => t.TotalMilliseconds).ToArray());
        Assert.AreEqual(TimeSpan.FromMilliseconds(3900), f.Clock.Elapsed);
        Assert.AreEqual(3, result.CompletedCycles); Assert.IsEmpty(f.Input.Sent);
    }

    [TestMethod]
    public void ScreenshotWaitsForAsyncCommitBeforeFollowingInputAndCondition()
    {
        var f = new Fixture(); var saved = new TaskCompletionSource(); bool checkedCondition = false;
        f.Executor.CaptureScreenshot = _ => saved.Task;
        f.Waits.BeforeWait = (_, _) => { if (f.Clock.Elapsed >= TimeSpan.FromMilliseconds(250)) saved.TrySetResult(); };
        f.Executor.EvaluateCondition = (_, _) => { Assert.IsTrue(saved.Task.IsCompletedSuccessfully); checkedCondition = true; return new(ConditionOutcome.Matched, 1, 1); };
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
        var plan = SequenceCompiler.Compile(new([SequenceStep.Screenshot(), SequenceStep.WaitUntil("Ready", condition), SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome); Assert.IsTrue(checkedCondition);
        Assert.IsTrue(f.Input.Sent[0].At >= TimeSpan.FromMilliseconds(1250)); Assert.HasCount(2, f.Input.Sent);
    }

    [TestMethod]
    public void ScreenshotFailureStopsLoopAndNoFollowingInputRuns()
    {
        var f = new Fixture(); int calls = 0;
        f.Executor.CaptureScreenshot = _ => { calls++; return Task.FromException(new IOException("Disk full")); };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Screenshot(), SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))], Repetition: new(0)));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Faulted, result.Outcome); Assert.AreEqual("Disk full", result.Error);
        Assert.AreEqual(1, calls); Assert.IsEmpty(f.Input.Sent); Assert.AreEqual(0, result.CompletedCycles);
    }

    [TestMethod]
    public void StopDuringPendingScreenshotOrItsPauseNeverStartsAnotherCapture()
    {
        foreach (bool pending in new[] { false, true })
        {
            var f = new Fixture(); using var stop = new CancellationTokenSource(); int calls = 0; CancellationToken captureToken = default;
            f.Executor.CaptureScreenshot = token => { calls++; captureToken = token; return pending ? new TaskCompletionSource().Task : Task.CompletedTask; };
            f.Waits.BeforeWait = (_, _) => { if (calls > 0) stop.Cancel(); };
            var result = f.Executor.Execute(SequenceCompiler.Compile(new([SequenceStep.Screenshot()], Repetition: new(0))), f.TargetId, stop.Token);
            Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome); Assert.AreEqual(1, calls);
            Assert.IsTrue(captureToken.IsCancellationRequested); Assert.IsEmpty(f.Input.Sent);
        }
    }

    [TestMethod]
    public void ActiveTimeLimitCancelsPendingScreenshotWithoutSpinning()
    {
        var f = new Fixture(); CancellationToken captureToken = default; int waits = 0;
        f.Executor.ActiveTimeLimit = TimeSpan.FromMilliseconds(100);
        f.Executor.CaptureScreenshot = token => { captureToken = token; return new TaskCompletionSource().Task; };
        f.Waits.BeforeWait = (_, _) => { if (++waits > 20) Assert.Fail("Unbounded polling"); };
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([SequenceStep.Screenshot()])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome); Assert.IsTrue(captureToken.IsCancellationRequested);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), f.Clock.Elapsed);
    }

    [TestMethod]
    public void ScreenshotAtSharedBoundaryRunsAfterReleaseAndFreshCondition()
    {
        var f = new Fixture(); bool matched = false; int captures = 0;
        f.Executor.EvaluateCondition = (_, _) => { matched = true; return new(ConditionOutcome.Matched, 1, 1); };
        f.Executor.CaptureScreenshot = _ => { Assert.IsTrue(matched); Assert.IsFalse(f.Executor.HasOwnedInput); captures++; return Task.CompletedTask; };
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20)),
            SequenceStep.WaitUntil("Ready", condition), SequenceStep.Screenshot()]));
        Assert.AreEqual(ClickRunOutcome.Completed, f.Executor.Execute(plan, f.TargetId, default).Outcome);
        Assert.AreEqual(1, captures); Assert.HasCount(2, f.Input.Sent);
    }

    [TestMethod]
    public void PauseDuringScreenshotDoesNotDuplicateCommittedCapture()
    {
        var f = new Fixture(); using var playback = new PlaybackControl(); f.Executor.Playback = playback;
        var saved = new TaskCompletionSource(); int captures = 0; bool paused = false;
        f.Executor.CaptureScreenshot = _ => { captures++; return saved.Task; };
        f.Waits.BeforeWait = (_, _) => { if (captures > 0 && !paused) { paused = true; playback.RequestPause(); saved.SetResult(); } };
        f.Executor.PlaybackPaused = value => { if (value) Assert.IsTrue(playback.RequestResume()); };
        Assert.AreEqual(ClickRunOutcome.Completed, f.Executor.Execute(SequenceCompiler.Compile(new([SequenceStep.Screenshot()])), f.TargetId, default).Outcome);
        Assert.AreEqual(1, captures); Assert.IsTrue(paused); Assert.IsEmpty(f.Input.Sent);
    }

    [TestMethod]
    public void ActionsSampleNewRangesEveryLoopWithSafeStopAndRateLimit()
    {
        var f = new Fixture(); var samples = new Queue<double>([0, 0, 0.5, 0.5, 0.9, 0.9]);
        f.Executor.NextRandom = () => samples.Dequeue();
        var step = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, TimeSpan.FromMilliseconds(20), TimeSpan.Zero, TimeSpan.FromMilliseconds(20))
            with { Timing = new(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(80)) };
        var plan = SequenceCompiler.Compile(new([step], Repetition: new(3)));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        CollectionAssert.AreEqual(new double[] { 0, 20, 40, 70, 120, 158 }, f.Input.Sent.Select(x => x.At.TotalMilliseconds).ToArray());
        Assert.AreEqual(3, result.CompletedCycles); Assert.IsFalse(f.Executor.HasOwnedInput);
        var stopped = new Fixture(); using var stop = new CancellationTokenSource();
        stopped.Executor.NextRandom = () => 0.5;
        stopped.Waits.BeforeWait = (_, _) => { if (stopped.Input.Sent.Count > 0) stop.Cancel(); };
        Assert.AreEqual(ClickRunOutcome.Cancelled, stopped.Executor.Execute(plan, stopped.TargetId, stop.Token).Outcome);
        Assert.AreEqual(2, stopped.Input.Sent.Count); Assert.IsFalse(stopped.Executor.HasOwnedInput);
        var paced = new Fixture(); paced.Executor.ClickRateLimit = new(10); paced.Executor.NextRandom = () => 0;
        paced.Executor.Execute(plan, paced.TargetId, default);
        CollectionAssert.AreEqual(new double[] { 0, 100, 200 }, paced.Input.Sent.Where(i => i.Input.Edge == InputEdge.Down).Select(i => i.At.TotalMilliseconds).ToArray());
    }

    [TestMethod]
    public void CursorFreeActionsCannotShareAKeyboardHoldSession()
    {
        var step = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, TimeSpan.FromMilliseconds(20), TimeSpan.Zero,
            TimeSpan.FromMilliseconds(20), new(10, 20, 800, 600, 96)) with { LeaveCursorFree = true };
        var plan = SequenceCompiler.Compile(new([step]));
        var hold = new NamedHelper(Guid.NewGuid(), "W", new(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0));
        using var session = new ActionToggleSession(); Assert.IsTrue(session.Start(plan, "Free click", TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentException>(() => session.Toggle(hold));
        using var other = new ActionToggleSession(); Assert.IsTrue(other.Toggle(hold));
        Assert.ThrowsExactly<ArgumentException>(() => other.Start(plan, "Free click", TimeSpan.Zero));
    }

    [TestMethod]
    public void RandomTimingKeepsCyclesBoundedAndReleasesEveryPress()
    {
        var f = new Fixture(); f.Executor.NextRandom = () => 0.5;
        var plan = new HelperPlan(InputControl.Mouse(PointerButton.Left), HelperMode.Repeat,
            TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(20), 3)
        { MaximumInterval = TimeSpan.FromMilliseconds(400), MaximumPressDuration = TimeSpan.FromMilliseconds(40) };
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        CollectionAssert.AreEqual(new double[] { 0, 30, 300, 330, 600, 630 }, f.Input.Sent.Select(x => x.At.TotalMilliseconds).ToArray());
        Assert.IsFalse(f.Executor.HasOwnedInput);
        Assert.IsNotEmpty((plan with { MaximumPressDuration = TimeSpan.FromMilliseconds(250) }).Validate());
        Assert.IsNotEmpty((plan with { MaximumInterval = TimeSpan.FromMilliseconds(100) }).Validate());
    }

    [TestMethod]
    public void PauseReleasesAndResumesRemainingHoldWithoutCountingPausedTime()
    {
        var f = new Fixture(); using var control = new PlaybackControl(); f.Executor.Playback = control;
        bool requested = false;
        f.Waits.BeforeWait = (_, _) =>
        {
            if (!requested && f.Input.Sent.Count == 1) { requested = true; f.Clock.Advance(TimeSpan.FromMilliseconds(30)); control.RequestPause(); }
        };
        f.Executor.PlaybackPaused = paused =>
        {
            if (!paused) return;
            Assert.IsFalse(f.Executor.HasOwnedInput); Assert.IsTrue(control.CanResume);
            var active = f.Executor.TotalActiveTime;
            f.Clock.Advance(TimeSpan.FromHours(1)); Assert.AreEqual(active, f.Executor.TotalActiveTime);
            Assert.IsTrue(control.RequestResume());
        };
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(100))])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome); Assert.AreEqual(4, result.InputsSent);
        Assert.AreEqual(TimeSpan.FromHours(1) + TimeSpan.FromMilliseconds(100), f.Clock.Elapsed);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), f.Executor.TotalActiveTime); Assert.IsFalse(f.Executor.HasOwnedInput);
        Assert.IsFalse(control.RequestResume());
    }

    [TestMethod]
    public void PausedWaitKeepsRemainingTimeAndStopDiscardsContinuation()
    {
        foreach (bool cancel in new[] { false, true })
        {
            var f = new Fixture(); using var control = new PlaybackControl(); using var stop = new CancellationTokenSource();
            f.Executor.Playback = control; bool requested = false;
            f.Waits.BeforeWait = (_, _) => { if (!requested) { requested = true; f.Clock.Advance(TimeSpan.FromMilliseconds(25)); control.RequestPause(); } };
            f.Executor.PlaybackPaused = paused => { if (paused) { f.Clock.Advance(TimeSpan.FromSeconds(10)); if (cancel) stop.Cancel(); else control.RequestResume(); } };
            var result = f.Executor.Execute(SequenceCompiler.Compile(new([SequenceStep.Wait(TimeSpan.FromMilliseconds(100)), SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))])), f.TargetId, stop.Token);
            Assert.AreEqual(cancel ? ClickRunOutcome.Cancelled : ClickRunOutcome.Completed, result.Outcome);
            Assert.AreEqual(cancel ? 0 : 2, result.InputsSent); Assert.IsFalse(f.Executor.HasOwnedInput);
            if (!cancel) Assert.AreEqual(TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(100), f.Input.Sent[0].At);
        }
    }

    [TestMethod]
    public void InterruptedMouseInteractionCannotResumeAndFailedKeyResumeCleansUp()
    {
        foreach (bool mouse in new[] { true, false })
        {
            var f = new Fixture(); using var control = new PlaybackControl(); f.Executor.Playback = control;
            f.Waits.BeforeWait = (_, _) => { if (f.Input.Sent.Count == 1) control.RequestPause(); };
            f.Executor.PlaybackPaused = paused => { if (paused) { f.Target.Current = new(f.TargetId, false); control.RequestResume(); } };
            var input = mouse ? InputControl.Mouse(PointerButton.Left) : InputControl.Key(0x11);
            var result = f.Executor.Execute(SequenceCompiler.Compile(new([SequenceStep.Input("Hold", input, TimeSpan.FromMilliseconds(100))])), f.TargetId, default);
            Assert.AreEqual(mouse ? ClickRunOutcome.Paused : ClickRunOutcome.TargetUnavailable, result.Outcome);
            Assert.AreEqual(2, result.InputsSent); Assert.IsFalse(f.Executor.HasOwnedInput);
            Assert.IsFalse(control.RequestResume());
            if (mouse) StringAssert.Contains(result.Error!, "Interaction interrupted");
        }
    }

    [TestMethod]
    public void ResumedConditionDiscardsItsOldObservation()
    {
        var f = new Fixture(); using var control = new PlaybackControl(); f.Executor.Playback = control;
        int observations = 0;
        f.Executor.EvaluateCondition = (_, _) => { if (++observations == 1) control.RequestPause(); return new(ConditionOutcome.Matched, 1, 1); };
        f.Executor.PlaybackPaused = paused => { if (paused) { f.Clock.Advance(TimeSpan.FromMinutes(1)); control.RequestResume(); } };
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Window", condition)])), f.TargetId, default);
        Assert.AreEqual(2, observations); Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        Assert.AreEqual(TimeSpan.Zero, f.Executor.TotalActiveTime);
    }

    [TestMethod]
    public void ConditionTimeoutResumeRetriesOnlyTheConditionAndCallbackFailureCleansUp()
    {
        var f = new Fixture(); using var control = new PlaybackControl(); f.Executor.Playback = control;
        int observations = 0, pauses = 0;
        f.Executor.EvaluateCondition = (_, _) => new(++observations == 1 ? ConditionOutcome.Paused : ConditionOutcome.Matched, 1, 1);
        f.Executor.PlaybackPaused = paused => { if (paused) { pauses++; Assert.AreEqual(2, f.Input.Sent.Count); control.RequestResume(); } };
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
        var key = SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20));
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([key, SequenceStep.WaitUntil("Window", condition), key with { Id = Guid.NewGuid() }])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome); Assert.AreEqual(4, result.InputsSent);
        Assert.AreEqual(2, observations); Assert.AreEqual(1, pauses); Assert.IsFalse(f.Executor.HasOwnedInput);

        using var broken = new PlaybackControl(); f.Executor.Playback = broken;
        f.Waits.BeforeWait = (_, _) => broken.RequestPause();
        f.Executor.PlaybackPaused = _ => throw new InvalidOperationException("Power transition failed");
        result = f.Executor.Execute(SequenceCompiler.Compile(new([key])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Faulted, result.Outcome); Assert.IsTrue(result.CleanupSucceeded);
        Assert.IsFalse(f.Executor.HasOwnedInput); Assert.IsFalse(broken.CanResume);
    }

    private sealed class Wait(FakeClock clock) : IWaitStrategy
    {
        public Action<TimeSpan, CancellationToken>? BeforeWait { get; set; }
        public void WaitUntil(TimeSpan deadline, CancellationToken stop)
        {
            BeforeWait?.Invoke(deadline, stop);
            stop.ThrowIfCancellationRequested();
            if (deadline > clock.Elapsed) clock.Advance(deadline - clock.Elapsed);
        }
    }

    private sealed class Sink(FakeClock clock) : IInputSink
    {
        public List<(TimeSpan At, InputTransition Input)> Sent { get; } = [];
        public Func<InputTransition, bool>? Accept { get; set; }
        public bool TrySend(InputTransition input)
        {
            if (Accept?.Invoke(input) == false) return false;
            Sent.Add((clock.Elapsed, input));
            return true;
        }
    }

    private sealed class Fixture
    {
        public readonly FakeClock Clock = new();
        public readonly FakeForegroundProvider Target = new() { Current = new(Guid.NewGuid(), true) };
        public readonly Sink Input;
        public readonly Wait Waits;
        public readonly ClickExecutor Executor;
        public readonly Guid TargetId;
        public Fixture()
        {
            TargetId = Target.Current!.InstanceId;
            Input = new(Clock);
            Waits = new(Clock);
            Executor = new(Input, Clock, Waits, Target);
            Executor.ClickRateLimit = new(100);
        }
        public ClickRunResult Run(int count = 3, CancellationToken stop = default) => Executor.Execute(
            ClickPlan.Compile(new(PointerButton.Left, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20)), count), TargetId, stop);
    }

    [TestMethod]
    public void UnicodeTextUsesOrderedOwnershipAndCancellationReleasesOnlyAcceptedUnit()
    {
        var f = new Fixture();
        var plan = SequenceCompiler.Compile(new([SequenceStep.TypeText("é😀")]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        Assert.AreEqual(6, result.InputsSent); Assert.IsFalse(f.Executor.HasOwnedInput);
        CollectionAssert.AreEqual(new[] { InputEdge.Down, InputEdge.Up, InputEdge.Down, InputEdge.Up, InputEdge.Down, InputEdge.Up }, f.Input.Sent.Select(i => i.Input.Edge).ToArray());
        var cancelled = new Fixture(); using var stop = new CancellationTokenSource();
        cancelled.Waits.BeforeWait = (_, _) => { if (cancelled.Input.Sent.Count == 1) stop.Cancel(); };
        result = cancelled.Executor.Execute(plan, cancelled.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(2, result.InputsSent); Assert.IsFalse(cancelled.Executor.HasOwnedInput);
        Assert.AreEqual(cancelled.Input.Sent[0].Input.Control, cancelled.Input.Sent[1].Input.Control);
    }

    [TestMethod]
    public void SequenceRepeatsWholeLoopsWithSeparateDelay()
    {
        var f = new Fixture();
        var source = new SequenceDefinition([SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))],
            Repetition: new(3, TimeSpan.FromMilliseconds(100)));
        var result = f.Executor.Execute(SequenceCompiler.Compile(source), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome); Assert.AreEqual(3, result.CompletedCycles);
        CollectionAssert.AreEqual(new[] { 0d, 120d, 240d }, f.Input.Sent.Where(s => s.Input.Edge == InputEdge.Down).Select(s => s.At.TotalMilliseconds).ToArray());
        Assert.AreEqual(TimeSpan.FromMilliseconds(260), f.Clock.Elapsed);
    }

    [TestMethod]
    public void SequenceActiveDurationClampsAHoldAndDoesNotLeakIntoNextRun()
    {
        var f = new Fixture();
        var source = new SequenceDefinition([SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromSeconds(2))],
            Repetition: new(0, ActiveDuration: TimeSpan.FromMilliseconds(50)));
        var result = f.Executor.Execute(SequenceCompiler.Compile(source), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome); Assert.AreEqual(0, result.CompletedCycles);
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), f.Input.Sent[^1].At); Assert.IsFalse(f.Executor.HasOwnedInput);
        Assert.AreEqual(ClickRunOutcome.Completed, f.Run(1).Outcome);
    }

    [TestMethod]
    public void InfiniteInstantLoopsHaveBoundedCadenceAndObeyLifetimeBudget()
    {
        var f = new Fixture(); f.Executor.CompletionLimit = 3;
        var plan = SequenceCompiler.Compile(new([SequenceStep.Wait(TimeSpan.Zero)], Repetition: new(0)));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(3, result.CompletedCycles); Assert.AreEqual(TimeSpan.FromMilliseconds(20), f.Clock.Elapsed);
        Assert.IsEmpty(f.Input.Sent); Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
    }

    [TestMethod]
    public void CancellationDuringInterLoopDelayNeverStartsAnotherBody()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        f.Waits.BeforeWait = (due, _) => { if (due >= TimeSpan.FromMilliseconds(100)) stop.Cancel(); };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))], Repetition: new(3, TimeSpan.FromSeconds(1))));
        var result = f.Executor.Execute(plan, f.TargetId, stop.Token);
        Assert.AreEqual(1, result.CompletedCycles); Assert.HasCount(2, f.Input.Sent); Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void FinishLoopCompletesRemainingActionsAndStopNowStillWins()
    {
        foreach (bool stopNow in new[] { false, true })
        {
            var f = new Fixture(); using var stop = new CancellationTokenSource();
            f.Waits.BeforeWait = (due, _) =>
            {
                if (due == TimeSpan.FromMilliseconds(20))
                { Assert.IsTrue(f.Executor.RequestFinishSequenceLoop()); if (stopNow) stop.Cancel(); }
            };
            var source = new SequenceDefinition([SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20)),
                SequenceStep.Input("E", InputControl.Key(0x12), TimeSpan.FromMilliseconds(30))], Repetition: new(5));
            var result = f.Executor.Execute(SequenceCompiler.Compile(source), f.TargetId, stop.Token);
            Assert.AreEqual(stopNow ? 0 : 1, result.CompletedCycles);
            Assert.HasCount(stopNow ? 2 : 4, f.Input.Sent);
            Assert.IsFalse(f.Executor.RequestFinishSequenceLoop()); Assert.IsFalse(f.Executor.HasOwnedInput);
        }
    }

    [TestMethod]
    public void FinishDuringInterLoopWaitDoesNotStartAnotherLoop()
    {
        var f = new Fixture();
        f.Waits.BeforeWait = (due, _) => { if (due > TimeSpan.FromMilliseconds(20)) Assert.IsTrue(f.Executor.RequestFinishSequenceLoop()); };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))], Repetition: new(5, TimeSpan.FromDays(1))));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(1, result.CompletedCycles); Assert.AreEqual(TimeSpan.FromMilliseconds(20), f.Clock.Elapsed);
    }

    [TestMethod]
    public void BranchSelectsOnlyFreshBooleanOutcomeAndCountsOneOuterLoop()
    {
        foreach (var outcome in Enum.GetValues<ConditionOutcome>())
        {
            var f = new Fixture();
            var yes = new ReusableSequence(Guid.NewGuid(), "Yes", [SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))]);
            var no = new ReusableSequence(Guid.NewGuid(), "No", [SequenceStep.Input("E", InputControl.Key(0x12), TimeSpan.FromMilliseconds(30))]);
            var check = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
            var flow = SequenceStep.WaitUntil("Branch", check) with { Kind = StepKind.ConditionalFlow, Flow = new(FlowKind.IfElse, yes.Id, no.Id) };
            f.Executor.EvaluateCondition = (definition, _) => { Assert.IsTrue(definition.CheckOnly); Assert.IsFalse(f.Executor.HasOwnedInput); return new(outcome, 1, 1); };
            var result = f.Executor.Execute(SequenceCompiler.Compile(new([flow], Subsequences: [yes, no])), f.TargetId, default);
            bool boolean = outcome is ConditionOutcome.Matched or ConditionOutcome.NotMatched;
            Assert.AreEqual(boolean ? 2 : 0, f.Input.Sent.Count);
            Assert.AreEqual(boolean ? 1 : 0, result.CompletedCycles);
            if (boolean) Assert.AreEqual(outcome == ConditionOutcome.Matched ? 0x11 : 0x12, f.Input.Sent[0].Input.Control.Code);
        }
    }
    [TestMethod]
    public void RepeatUntilChecksBeforeAndAfterBodiesAndStopsAtBound()
    {
        foreach (bool eventuallyMatches in new[] { false, true })
        {
            var f = new Fixture(); int observations = 0;
            var body = new ReusableSequence(Guid.NewGuid(), "Tap", [SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))]);
            var check = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
            var flow = SequenceStep.WaitUntil("Repeat", check) with { Kind = StepKind.ConditionalFlow, Flow = new(FlowKind.RepeatUntil, body.Id, MaximumIterations: 3) };
            f.Executor.EvaluateCondition = (_, _) => { Assert.IsFalse(f.Executor.HasOwnedInput); observations++; return new(eventuallyMatches && observations == 3 ? ConditionOutcome.Matched : ConditionOutcome.NotMatched, 1, 1); };
            var result = f.Executor.Execute(SequenceCompiler.Compile(new([flow, SequenceStep.Input("After", InputControl.Key(0x12), TimeSpan.FromMilliseconds(10))], Subsequences: [body])), f.TargetId, default);
            Assert.AreEqual(eventuallyMatches ? ClickRunOutcome.Completed : ClickRunOutcome.Cancelled, result.Outcome);
            Assert.AreEqual(eventuallyMatches ? 3 : 4, observations);
            Assert.AreEqual(6, f.Input.Sent.Count);
            Assert.AreEqual(eventuallyMatches ? 1 : 0, result.CompletedCycles);
            Assert.IsFalse(f.Executor.HasOwnedInput);
            Assert.AreEqual(eventuallyMatches ? 0x12 : 0x11, f.Input.Sent[^1].Input.Control.Code);
        }
    }
    [TestMethod]
    public void BranchHoldRespectsActiveBudgetAndCleansOwnedInput()
    {
        var f = new Fixture(); f.Executor.ActiveTimeLimit = TimeSpan.FromMilliseconds(50);
        var body = new ReusableSequence(Guid.NewGuid(), "Hold", [SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromSeconds(1))]);
        var check = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
        var flow = SequenceStep.WaitUntil("If", check) with { Kind = StepKind.ConditionalFlow, Flow = new(FlowKind.IfElse, body.Id) };
        f.Executor.EvaluateCondition = (_, _) => new(ConditionOutcome.Matched, 1, 1);
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([flow], Subsequences: [body])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome); Assert.AreEqual(0, result.CompletedCycles);
        Assert.HasCount(2, f.Input.Sent); Assert.AreEqual(TimeSpan.FromMilliseconds(50), f.Input.Sent[^1].At);
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void ConditionWaitPreservesActionOrderAndNeverOwnsInput()
    {
        var f = new Fixture();
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
        var steps = new[] { SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20)),
            SequenceStep.WaitUntil("Window", condition), SequenceStep.Input("E", InputControl.Key(0x12), TimeSpan.FromMilliseconds(30)) };
        f.Executor.EvaluateCondition = (_, _) =>
        {
            Assert.IsFalse(f.Executor.HasOwnedInput); Assert.HasCount(2, f.Input.Sent);
            f.Clock.Advance(TimeSpan.FromMilliseconds(500)); return new(ConditionOutcome.Matched, 1, 1);
        };
        Assert.AreEqual(ClickRunOutcome.Completed, f.Executor.Execute(SequenceCompiler.Compile(new(steps)), f.TargetId, default).Outcome);
        CollectionAssert.AreEqual(new double[] { 0, 20, 520, 550 }, f.Input.Sent.Select(x => x.At.TotalMilliseconds).ToArray());
    }
    [TestMethod]
    public void FailedOrPausedConditionCannotDispatchFollowingInput()
    {
        foreach (var outcome in new[] { ConditionOutcome.Stopped, ConditionOutcome.Paused, ConditionOutcome.Ambiguous, ConditionOutcome.Unavailable })
        {
            var f = new Fixture();
            var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
            f.Executor.EvaluateCondition = (_, _) => new(outcome, 1, 1);
            var plan = SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Window", condition), SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))]));
            var result = f.Executor.Execute(plan, f.TargetId, default);
            Assert.AreEqual(outcome == ConditionOutcome.Paused ? ClickRunOutcome.Paused : ClickRunOutcome.Cancelled, result.Outcome);
            Assert.HasCount(0, f.Input.Sent); Assert.AreEqual(0, result.CompletedCycles);
        }
    }
    [TestMethod]
    public void WindowWaitCanObserveAnInactiveTargetButFollowingInputStillChecksForeground()
    {
        var f = new Fixture(); f.Target.Current = new(f.TargetId, false);
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
        f.Executor.EvaluateCondition = (_, _) => new(ConditionOutcome.Matched, 1, 1); // Even a stale/bad match cannot authorize input.
        var plan = SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Window", condition), SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))]));
        Assert.AreEqual(ClickRunOutcome.TargetUnavailable, f.Executor.Execute(plan, f.TargetId, default).Outcome);
        Assert.HasCount(0, f.Input.Sent);
    }

    [TestMethod]
    public void ActiveBudgetInterruptsAnInfiniteHoldWithoutUiPolling()
    {
        var f = new Fixture(); f.Executor.ActiveTimeLimit = TimeSpan.FromSeconds(3);
        var hold = new HelperPlan(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0);
        var result = f.Executor.Execute(hold, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(TimeSpan.FromSeconds(3), f.Executor.TotalActiveTime);
        Assert.AreEqual(TimeSpan.FromSeconds(3), f.Input.Sent.Last().At);
        Assert.AreEqual(InputEdge.Up, f.Input.Sent.Last().Input.Edge);
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void CompletionBudgetStopsExactlyAtThresholdAndActiveTimeExcludesIdle()
    {
        var f = new Fixture();
        f.Executor.CompletionLimit = 2;
        Assert.AreEqual(2, f.Run(100).CompletedCycles);
        Assert.IsFalse(f.Executor.HasOwnedInput);
        var active = f.Executor.TotalActiveTime;
        Assert.AreEqual(TimeSpan.FromMilliseconds(120), active);
        f.Clock.Advance(TimeSpan.FromHours(1));
        Assert.AreEqual(active, f.Executor.TotalActiveTime);
        Assert.AreEqual(0, f.Run(10).CompletedCycles);
        f.Executor.CompletionLimit = long.MaxValue;
        Assert.AreEqual(1, f.Run(1).CompletedCycles);
        Assert.AreEqual(active + TimeSpan.FromMilliseconds(20), f.Executor.TotalActiveTime);
    }

    [TestMethod]
    public void GlobalClickCapStretchesSequenceAndPreservesHoldDurations()
    {
        var f = new Fixture(); f.Executor.ClickRateLimit = new(10);
        var first = SequenceStep.Input("Left", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(20));
        var second = SequenceStep.Input("Right", InputControl.Mouse(PointerButton.Right), TimeSpan.FromMilliseconds(30));
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([first, second])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        CollectionAssert.AreEqual(new double[] { 0, 20, 100, 130 }, f.Input.Sent.Select(x => x.At.TotalMilliseconds).ToArray());
        Assert.AreEqual(1L, f.Executor.TotalCompletedLoops);
    }

    [TestMethod]
    public void ClickCapAppliesAcrossRunBoundariesAndCountsOnlyCompletedLoops()
    {
        var f = new Fixture(); f.Executor.ClickRateLimit = new(5);
        Assert.AreEqual(ClickRunOutcome.Completed, f.Run(1).Outcome);
        Assert.AreEqual(ClickRunOutcome.Completed, f.Run(1).Outcome);
        CollectionAssert.AreEqual(new double[] { 0, 200 }, f.Input.Sent.Where(x => x.Input.Edge == InputEdge.Down).Select(x => x.At.TotalMilliseconds).ToArray());
        Assert.AreEqual(2L, f.Executor.TotalCompletedLoops);
        Assert.ThrowsExactly<ArgumentException>(() => f.Executor.ClickRateLimit = new(0));
    }

    [TestMethod]
    public void ToggleCanJoinReleaseAndRejoinWithoutRepressingTheOtherHeldKey()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        var w = new NamedHelper(Guid.NewGuid(), "Toggle W", new(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0));
        var e = w with { Id = Guid.NewGuid(), Name = "Toggle E", Plan = w.Plan with { Control = InputControl.Key(0x12) } };
        var plan = CompatibleHelpers.Compile([w]); using var control = new HelperSetControl(plan);
        int stage = 0;
        f.Waits.BeforeWait = (due, _) =>
        {
            if (due <= TimeSpan.Zero) return;
            if (stage == 0) { Assert.IsTrue(control.RequestJoin(e)); stage++; }
            else if (stage == 1) { Assert.IsTrue(control.RequestStop(w.Id)); stage++; }
            else if (stage == 2 && !control.ActiveIds.Contains(w.Id)) { Assert.IsTrue(control.RequestJoin(w)); stage++; }
            else if (stage == 3) { stop.Cancel(); stage++; }
        };
        f.Executor.Execute(plan, f.TargetId, stop.Token, control);
        Assert.AreEqual(4, stage);
        Assert.AreEqual(2, f.Input.Sent.Count(x => x.Input.Control.Code == 0x11 && x.Input.Edge == InputEdge.Down));
        Assert.AreEqual(2, f.Input.Sent.Count(x => x.Input.Control.Code == 0x11 && x.Input.Edge == InputEdge.Up));
        Assert.AreEqual(1, f.Input.Sent.Count(x => x.Input.Control.Code == 0x12 && x.Input.Edge == InputEdge.Down));
        Assert.AreEqual(1, f.Input.Sent.Count(x => x.Input.Control.Code == 0x12 && x.Input.Edge == InputEdge.Up));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void ExplicitCompatibleJoinStartsAtJoinTimeWithoutRestartingExistingHold()
    {
        var f = new Fixture();
        var hold = new NamedHelper(Guid.NewGuid(), "W", new(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 1, TimeSpan.FromSeconds(1)));
        var tap = new NamedHelper(Guid.NewGuid(), "E", new(InputControl.Key(0x12), HelperMode.Repeat, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 2));
        var plan = CompatibleHelpers.Compile([hold]); using var control = new HelperSetControl(plan);
        bool joined = false;
        f.Waits.BeforeWait = (due, _) =>
        {
            if (due <= TimeSpan.Zero || joined) return;
            joined = true; f.Clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.ThrowsExactly<ArgumentException>(() => control.RequestJoin(hold with { Id = Guid.NewGuid() }));
            Assert.IsTrue(control.RequestJoin(tap));
        };
        var result = f.Executor.Execute(plan, f.TargetId, default, control);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome); Assert.AreEqual(3, result.CompletedCycles);
        CollectionAssert.AreEqual(new double[] { 100, 200 }, f.Input.Sent.Where(x => x.Input.Control.Code == 0x12 && x.Input.Edge == InputEdge.Down).Select(x => x.At.TotalMilliseconds).ToArray());
        Assert.AreEqual(TimeSpan.FromSeconds(1), f.Input.Sent.Last().At);
        Assert.IsFalse(control.RequestJoin(tap with { Id = Guid.NewGuid() }));
        Assert.HasCount(0, control.ActiveControls); Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void StoppingOneCombinedMemberWakesLongWaitAndPreservesOtherOwnership()
    {
        var f = new Fixture();
        var first = new NamedHelper(Guid.NewGuid(), "W", new(InputControl.Key(0x11), HelperMode.Hold,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0));
        var second = first with { Id = Guid.NewGuid(), Name = "E", Plan = first.Plan with { Control = InputControl.Key(0x12), Count = 1, ActiveDuration = TimeSpan.FromSeconds(1) } };
        var plan = CompatibleHelpers.Compile([first, second]); using var control = new HelperSetControl(plan);
        bool requested = false;
        f.Waits.BeforeWait = (due, _) =>
        {
            if (due <= TimeSpan.Zero || requested) return;
            requested = true; Assert.IsTrue(control.RequestStop(first.Id)); Assert.IsFalse(control.RequestStop(first.Id));
        };
        var result = f.Executor.Execute(plan, f.TargetId, default, control);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome); Assert.AreEqual(1, result.CompletedCycles);
        Assert.AreEqual(TimeSpan.Zero, f.Input.Sent.Single(x => x.Input.Edge == InputEdge.Up && x.Input.Control.Code == 0x11).At);
        Assert.AreEqual(TimeSpan.FromSeconds(1), f.Input.Sent.Single(x => x.Input.Edge == InputEdge.Up && x.Input.Control.Code == 0x12).At);
        Assert.HasCount(0, f.Executor.Progress.Held);
    }

    [TestMethod]
    public void CombinedFailureCleansOtherHeldHelpersAndRetainsRejectedRelease()
    {
        var f = new Fixture();
        var first = new NamedHelper(Guid.NewGuid(), "W", new(InputControl.Key(0x11), HelperMode.Hold,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 1, TimeSpan.FromSeconds(1)));
        var second = first with { Id = Guid.NewGuid(), Name = "E", Plan = first.Plan with { Control = InputControl.Key(0x12) } };
        f.Input.Accept = input => input.Edge == InputEdge.Down || input.Control.Code != 0x11;
        var result = f.Executor.Execute(CompatibleHelpers.Compile([first, second]), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.CleanupFailed, result.Outcome);
        Assert.AreEqual(1, f.Input.Sent.Count(x => x.Input.Control.Code == 0x12 && x.Input.Edge == InputEdge.Up));
        Assert.HasCount(1, f.Executor.Progress.Held);
        Assert.AreEqual(0x11, f.Executor.Progress.Held[0].Code);
        Assert.ThrowsExactly<InvalidOperationException>(() => f.Run());
    }

    [TestMethod]
    public void SharedClickCapDoesNotDelayOtherHelpersReleasesAndBudgetCleansHold()
    {
        var f = new Fixture(); f.Executor.ClickRateLimit = new(1);
        var mouse = new NamedHelper(Guid.NewGuid(), "Mouse", new(InputControl.Mouse(PointerButton.Left), HelperMode.Repeat,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(5), 2));
        var key = new NamedHelper(Guid.NewGuid(), "Key", new(InputControl.Key(0x11), HelperMode.Repeat,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 2));
        f.Executor.Execute(CompatibleHelpers.Compile([mouse, key]), f.TargetId, default);
        CollectionAssert.AreEqual(new double[] { 0, 20, 100, 120 }, f.Input.Sent.Where(x => x.Input.Control.Kind == InputKind.ScanCode).Select(x => x.At.TotalMilliseconds).ToArray());
        CollectionAssert.AreEqual(new double[] { 0, 5, 1000, 1005 }, f.Input.Sent.Where(x => x.Input.Control.Kind == InputKind.MouseButton).Select(x => x.At.TotalMilliseconds).ToArray());
        f.Executor.CompletionLimit = f.Executor.TotalCompletedLoops + 1;
        var hold = key with { Plan = key.Plan with { Mode = HelperMode.Hold, Count = 0 } };
        var result = f.Executor.Execute(CompatibleHelpers.Compile([mouse, hold]), f.TargetId, default);
        Assert.AreEqual(1, result.CompletedCycles); Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void CompatibleHoldAndRepeatShareOwnershipWithoutReleasingEachOther()
    {
        var f = new Fixture();
        var hold = new NamedHelper(Guid.NewGuid(), "Hold W", new(InputControl.Key(0x11), HelperMode.Hold,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 1, TimeSpan.FromSeconds(1)));
        var tap = new NamedHelper(Guid.NewGuid(), "Tap E", new(InputControl.Key(0x12), HelperMode.Repeat,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 3));
        var result = f.Executor.Execute(CompatibleHelpers.Compile([hold, tap]), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome); Assert.AreEqual(4, result.CompletedCycles);
        var w = f.Input.Sent.Where(x => x.Input.Control.Code == 0x11).ToArray();
        CollectionAssert.AreEqual(new double[] { 0, 1000 }, w.Select(x => x.At.TotalMilliseconds).ToArray());
        CollectionAssert.AreEqual(new double[] { 0, 20, 100, 120, 200, 220 },
            f.Input.Sent.Where(x => x.Input.Control.Code == 0x12).Select(x => x.At.TotalMilliseconds).ToArray());
        Assert.HasCount(0, f.Executor.Progress.Held);
    }

    [TestMethod]
    public void CombinedHelpersPreserveEqualPressAndIntervalWithoutHalvingCadence()
    {
        var f = new Fixture();
        var helper = new NamedHelper(Guid.NewGuid(), "W", new(InputControl.Key(0x11), HelperMode.Repeat,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), 3));
        Assert.AreEqual(ClickRunOutcome.Completed, f.Executor.Execute(CompatibleHelpers.Compile([helper]), f.TargetId, default).Outcome);
        CollectionAssert.AreEqual(new double[] { 0, 100, 200 }, f.Input.Sent.Where(x => x.Input.Edge == InputEdge.Down).Select(x => x.At.TotalMilliseconds).ToArray());
    }

    [TestMethod]
    public void CombinedHelperCancellationCleansEveryAcceptedHold()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        var first = new NamedHelper(Guid.NewGuid(), "W", new(InputControl.Key(0x11), HelperMode.Hold,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0));
        var second = first with { Id = Guid.NewGuid(), Name = "E", Plan = first.Plan with { Control = InputControl.Key(0x12) } };
        f.Waits.BeforeWait = (due, _) => { if (due > TimeSpan.Zero) stop.Cancel(); };
        var result = f.Executor.Execute(CompatibleHelpers.Compile([first, second]), f.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        CollectionAssert.AreEqual(new[] { 0x12, 0x11 }, f.Input.Sent.Where(x => x.Input.Edge == InputEdge.Up).Select(x => x.Input.Control.Code).ToArray());
        Assert.HasCount(0, f.Executor.Progress.Held);
    }

    [TestMethod]
    public async Task ProgressRemainsReadableDuringBlockedDispatch()
    {
        var f = new Fixture();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        f.Input.Accept = input =>
        {
            if (input.Edge == InputEdge.Down) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
            return true;
        };
        var worker = Task.Run(() => f.Run(1));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(2)));
            var progress = await Task.Run(() => f.Executor.Progress).WaitAsync(TimeSpan.FromSeconds(1));
            Assert.HasCount(0, progress.Held); // An unaccepted down is not owned yet.
        }
        finally { release.Set(); }
        Assert.AreEqual(ClickRunOutcome.Completed, (await worker).Outcome);
        Assert.AreEqual(1, f.Executor.Progress.CompletedCycles);
        Assert.HasCount(0, f.Executor.Progress.Held);
    }

    [TestMethod]
    public void ProgressSnapshotsRetainHeldStateWithoutLeakingMutableLedger()
    {
        var f = new Fixture(); ExecutionProgress? held = null;
        f.Waits.BeforeWait = (at, _) => { if (at > TimeSpan.Zero && held is null) held = f.Executor.Progress; };
        Assert.AreEqual(ClickRunOutcome.Completed, f.Run(1).Outcome);
        Assert.IsNotNull(held); Assert.HasCount(1, held.Held);
        Assert.AreEqual(InputControl.Mouse(PointerButton.Left), held.Held[0]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<InputControl>)held.Held).Clear());
        Assert.HasCount(0, f.Executor.Progress.Held);
    }

    [TestMethod]
    public void CancelledDragReleasesWithoutMovingToUnreachedEndpoint()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        var start = new ClientPoint(10, 20, 800, 600, 96);
        var step = SequenceStep.Input("Drag", InputControl.Mouse(PointerButton.Left), TimeSpan.FromSeconds(1))
            with { Position = start, EndPosition = start with { X = 700 } };
        f.Waits.BeforeWait = (deadline, _) => { if (deadline > TimeSpan.Zero) stop.Cancel(); };
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([step])), f.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.HasCount(2, f.Input.Sent); Assert.AreEqual(start, f.Input.Sent[0].Input.Position);
        Assert.AreEqual(InputEdge.Up, f.Input.Sent[1].Input.Edge); Assert.IsNull(f.Input.Sent[1].Input.Position);
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void RejectedPositionedReleaseFallsBackToStationaryCleanup()
    {
        var f = new Fixture(); var point = new ClientPoint(10, 20, 800, 600, 96);
        var step = SequenceStep.Input("Drag", InputControl.Mouse(PointerButton.Left), TimeSpan.FromSeconds(1))
            with { Position = point, EndPosition = point with { X = 700 } };
        f.Input.Accept = input => input.Edge == InputEdge.Down || input.Position is null;
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([step])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.InputRejected, result.Outcome); Assert.IsTrue(result.CleanupSucceeded);
        Assert.HasCount(2, f.Input.Sent); Assert.IsNull(f.Input.Sent[1].Input.Position);
    }

    [TestMethod]
    public void SequenceUsesSameExecutorAndPreservesOverlappingOwnership()
    {
        var f = new Fixture();
        var group = SequenceStep.Group("Hold W and tap Space", TimeSpan.FromSeconds(1),
            [new(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(0x11), TimeSpan.FromSeconds(1)),
             new(Guid.NewGuid(), TimeSpan.FromMilliseconds(200), InputControl.Key(0x39), TimeSpan.FromMilliseconds(50))]);
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([group])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        CollectionAssert.AreEqual(new[] { 0x11, 0x39, 0x39, 0x11 }, f.Input.Sent.Select(x => x.Input.Control.Code).ToArray());
        CollectionAssert.AreEqual(new double[] { 0, 200, 250, 1000 }, f.Input.Sent.Select(x => x.At.TotalMilliseconds).ToArray());
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void SequenceCancellationReleasesEveryOwnedInputInReverseOrder()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        var group = SequenceStep.Group("Overlap", TimeSpan.FromSeconds(2),
            [new(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(0x11), TimeSpan.FromSeconds(2)),
             new(Guid.NewGuid(), TimeSpan.FromSeconds(1), InputControl.Key(0x39), TimeSpan.FromSeconds(1))]);
        f.Waits.BeforeWait = (at, _) => { if (at >= TimeSpan.FromSeconds(2)) stop.Cancel(); };
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([group])), f.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        CollectionAssert.AreEqual(new[] { 0x39, 0x11 }, f.Input.Sent.Where(x => x.Input.Edge == InputEdge.Up).Select(x => x.Input.Control.Code).ToArray());
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void OneFailedReleaseDoesNotPreventCleaningOtherOwnedKeys()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        var group = SequenceStep.Group("Overlap", TimeSpan.FromSeconds(2),
            [new(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(0x11), TimeSpan.FromSeconds(2)),
             new(Guid.NewGuid(), TimeSpan.FromSeconds(1), InputControl.Key(0x39), TimeSpan.FromSeconds(1))]);
        f.Input.Accept = input => input.Edge != InputEdge.Up || input.Control.Code != 0x39;
        f.Waits.BeforeWait = (at, _) => { if (at >= TimeSpan.FromSeconds(2)) stop.Cancel(); };
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([group])), f.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.CleanupFailed, result.Outcome);
        Assert.AreEqual(1, f.Input.Sent.Count(x => x.Input.Edge == InputEdge.Up && x.Input.Control.Code == 0x11));
        Assert.IsTrue(f.Executor.HasOwnedInput);
        Assert.HasCount(1, f.Executor.Progress.Held);
        Assert.AreEqual(InputControl.Key(0x39), f.Executor.Progress.Held[0]);
        f.Input.Accept = _ => true; Assert.IsTrue(f.Executor.RetryCleanup());
        Assert.AreEqual(1, f.Input.Sent.Count(x => x.Input.Edge == InputEdge.Up && x.Input.Control.Code == 0x39));
    }

    [TestMethod]
    public void LateSequenceDispatchDoesNotShortenHold()
    {
        var f = new Fixture();
        f.Input.Accept = _ => { f.Clock.Advance(TimeSpan.FromMilliseconds(20)); return true; };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(100))]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100), f.Input.Sent[1].At - f.Input.Sent[0].At);
    }

    [TestMethod]
    public void FiniteRunUsesDeadlinesAndCountsAcceptedCycles()
    {
        var f = new Fixture();
        var result = f.Run();
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        Assert.AreEqual(3, result.CompletedCycles);
        Assert.AreEqual(6, result.InputsSent);
        CollectionAssert.AreEqual(new double[] { 0, 20, 100, 120, 200, 220 }, f.Input.Sent.Select(x => x.At.TotalMilliseconds).ToArray());
        CollectionAssert.AreEqual(new[] { InputEdge.Down, InputEdge.Up, InputEdge.Down, InputEdge.Up, InputEdge.Down, InputEdge.Up },
            f.Input.Sent.Select(x => x.Input.Edge).ToArray());
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void BoundedKeyboardHoldPreservesItsDuration()
    {
        var f = new Fixture();
        var plan = new HelperPlan(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 1, TimeSpan.FromSeconds(3));
        var result = f.Executor.Execute(plan, f.TargetId, CancellationToken.None);
        Assert.AreEqual(1, result.CompletedCycles); Assert.AreEqual(2, result.InputsSent);
        Assert.AreEqual(TimeSpan.FromSeconds(3), f.Input.Sent[1].At);
        Assert.AreEqual(InputKind.ScanCode, f.Input.Sent[0].Input.Control.Kind);
    }

    [TestMethod]
    public void ExplicitUntilStopHoldIsInterruptibleWithoutFakeTimeOverflow()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        f.Waits.BeforeWait = (deadline, _) => { if (deadline == TimeSpan.MaxValue) stop.Cancel(); };
        var plan = new HelperPlan(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0);
        Assert.AreEqual(ClickRunOutcome.Cancelled, f.Executor.Execute(plan, f.TargetId, stop.Token).Outcome);
        Assert.HasCount(2, f.Input.Sent);
    }

    [TestMethod]
    public void ScrollHasNoHeldInputOrInventedUp()
    {
        var f = new Fixture();
        var plan = new HelperPlan(new(InputKind.Scroll, -120), HelperMode.Scroll, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 3);
        var result = f.Executor.Execute(plan, f.TargetId, CancellationToken.None);
        Assert.AreEqual(3, result.InputsSent); Assert.AreEqual(3, result.CompletedCycles);
        Assert.IsTrue(f.Input.Sent.All(x => x.Input.Edge == InputEdge.Down));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void ActiveDurationBoundsRepeatsWithoutExpandingInfinitePlan()
    {
        var f = new Fixture();
        var plan = new HelperPlan(InputControl.Mouse(PointerButton.Left), HelperMode.Repeat, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0, TimeSpan.FromMilliseconds(250));
        var result = f.Executor.Execute(plan, f.TargetId, CancellationToken.None);
        Assert.AreEqual(3, result.CompletedCycles); Assert.AreEqual(6, result.InputsSent);
        Assert.IsTrue(f.Input.Sent.All(x => x.At < TimeSpan.FromMilliseconds(250)));
    }

    [TestMethod]
    public void OversleepSkipsMissedSlotsAndPreservesPressDuration()
    {
        var f = new Fixture();
        f.Waits.BeforeWait = (deadline, _) =>
        {
            if (deadline == TimeSpan.FromMilliseconds(100)) f.Clock.Advance(TimeSpan.FromMilliseconds(330));
        };
        var result = f.Run();
        Assert.AreEqual(3, result.CompletedCycles);
        CollectionAssert.AreEqual(new double[] { 0, 20, 350, 370, 400, 420 }, f.Input.Sent.Select(x => x.At.TotalMilliseconds).ToArray());
    }

    [TestMethod]
    public void CancelledBeforeStartSendsNothing()
    {
        var f = new Fixture();
        var result = f.Run(stop: new CancellationToken(true));
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.HasCount(0, f.Input.Sent);
    }

    [TestMethod]
    public void CancellationDuringHoldReleasesOnlyOwnedButtonOnce()
    {
        var f = new Fixture();
        using var stop = new CancellationTokenSource();
        f.Waits.BeforeWait = (deadline, _) => { if (deadline > TimeSpan.Zero) stop.Cancel(); };
        var result = f.Run(stop: stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, result.CompletedCycles);
        Assert.AreEqual(2, result.InputsSent);
        Assert.IsTrue(result.CleanupSucceeded);
        Assert.IsTrue(f.Executor.RetryCleanup());
        Assert.HasCount(2, f.Input.Sent);
    }

    [TestMethod]
    public void CancellationDuringGapPreventsNextDown()
    {
        var f = new Fixture();
        using var stop = new CancellationTokenSource();
        f.Waits.BeforeWait = (deadline, _) => { if (deadline == TimeSpan.FromMilliseconds(100)) stop.Cancel(); };
        var result = f.Run(stop: stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(1, result.CompletedCycles);
        Assert.HasCount(2, f.Input.Sent);
    }

    [TestMethod]
    public void ForegroundIdentityChangeStopsAndDoesNotResumeOnReturn()
    {
        var f = new Fixture();
        f.Waits.BeforeWait = (deadline, _) =>
        {
            if (deadline == TimeSpan.FromMilliseconds(100)) f.Target.Current = new(Guid.NewGuid(), true);
        };
        var result = f.Run();
        Assert.AreEqual(ClickRunOutcome.TargetUnavailable, result.Outcome);
        f.Target.Current = new(f.TargetId, true);
        Assert.AreEqual(1, result.CompletedCycles);
        Assert.HasCount(2, f.Input.Sent);
    }

    [TestMethod]
    public void SafetyCancellationReleasesHoldBeforeWaitReturns()
    {
        var f = new Fixture();
        using var safetyStop = new CancellationTokenSource();
        f.Waits.BeforeWait = (deadline, _) =>
        {
            if (deadline == TimeSpan.Zero) return;
            f.Target.Current = null;
            safetyStop.Cancel(); // Models an independent foreground/session notification adapter.
            Assert.IsFalse(f.Executor.HasOwnedInput);
            Assert.HasCount(2, f.Input.Sent);
        };
        Assert.AreEqual(ClickRunOutcome.Cancelled, f.Run(stop: safetyStop.Token).Outcome);
    }

    [TestMethod]
    public void RejectedDownDoesNotInventOwnershipOrCompletedCycle()
    {
        var f = new Fixture();
        f.Input.Accept = _ => false;
        var result = f.Run();
        Assert.AreEqual(ClickRunOutcome.InputRejected, result.Outcome);
        Assert.AreEqual(0, result.InputsSent);
        Assert.AreEqual(0, result.CompletedCycles);
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void FailedCleanupBlocksNextRunUntilExplicitRecovery()
    {
        var f = new Fixture();
        f.Input.Accept = input => input.Edge == InputEdge.Down;
        var result = f.Run();
        Assert.AreEqual(ClickRunOutcome.CleanupFailed, result.Outcome);
        Assert.IsFalse(result.CleanupSucceeded);
        Assert.AreEqual(0, result.CompletedCycles);
        Assert.AreEqual(1, result.InputsSent);
        Assert.ThrowsExactly<InvalidOperationException>(() => f.Run());
        f.Input.Accept = _ => true;
        Assert.IsTrue(f.Executor.RetryCleanup());
        Assert.IsTrue(f.Executor.RetryCleanup());
        Assert.HasCount(2, f.Input.Sent);
        Assert.AreEqual(ClickRunOutcome.Completed, f.Run(1).Outcome);
    }

    [TestMethod]
    public void WaitFailureStillCleansUpAcceptedDown()
    {
        var f = new Fixture();
        f.Waits.BeforeWait = (deadline, _) => { if (deadline > TimeSpan.Zero) throw new InvalidOperationException("Fake wait fault"); };
        var result = f.Run();
        Assert.AreEqual(ClickRunOutcome.Faulted, result.Outcome);
        Assert.IsTrue(result.CleanupSucceeded);
        Assert.HasCount(2, f.Input.Sent);
    }

    [TestMethod]
    public void ReleaseExceptionRetainsOwnershipUntilRecovered()
    {
        var f = new Fixture();
        f.Input.Accept = input => input.Edge == InputEdge.Down ? true : throw new InvalidOperationException("Fake release fault");
        Assert.AreEqual(ClickRunOutcome.CleanupFailed, f.Run().Outcome);
        Assert.IsTrue(f.Executor.HasOwnedInput);
        f.Input.Accept = _ => true;
        Assert.IsTrue(f.Executor.RetryCleanup());
        Assert.HasCount(2, f.Input.Sent);
    }

    [TestMethod]
    public void CancellationInsideInFlightDownStillReconcilesItsAcceptedOwnership()
    {
        var f = new Fixture();
        using var stop = new CancellationTokenSource();
        f.Input.Accept = input =>
        {
            if (input.Edge == InputEdge.Down) stop.Cancel();
            return true; // Models a dispatch already in flight when stop arrives.
        };
        var result = f.Run(stop: stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, result.CompletedCycles);
        Assert.AreEqual(2, result.InputsSent);
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void OverlappingExecutionIsRejectedWithoutReplacingActiveRun()
    {
        var f = new Fixture();
        f.Waits.BeforeWait = (_, _) => Assert.ThrowsExactly<InvalidOperationException>(() => f.Run());
        Assert.AreEqual(ClickRunOutcome.Completed, f.Run(1).Outcome);
        Assert.HasCount(2, f.Input.Sent);
    }

    [TestMethod]
    public async Task IndependentCancellationCleansUpWhileSchedulerIsBlocked()
    {
        var f = new Fixture();
        using var stop = new CancellationTokenSource();
        using var blocked = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        f.Waits.BeforeWait = (deadline, _) =>
        {
            if (deadline == TimeSpan.Zero) return;
            blocked.Set();
            if (!resume.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test scheduler did not resume.");
        };
        var worker = Task.Run(() => f.Run(stop: stop.Token));
        try
        {
            Assert.IsTrue(blocked.Wait(TimeSpan.FromSeconds(2)));
            stop.Cancel();
            Assert.IsFalse(f.Executor.HasOwnedInput);
            Assert.HasCount(2, f.Input.Sent);
        }
        finally { resume.Set(); }
        Assert.AreEqual(ClickRunOutcome.Cancelled, (await worker.WaitAsync(TimeSpan.FromSeconds(2))).Outcome);
    }
}
