using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class ActionToggleSessionTests
{
    private static readonly InputControl W = InputControl.Key(0x11), E = InputControl.Key(0x12);
    private static NamedHelper Hold(InputControl input) => new(Guid.NewGuid(), "Test hold", new(input, HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0));
    private static SequencePlan Tap(InputControl input) => SequenceCompiler.Compile(new([SequenceStep.Input("Tap", input, TimeSpan.FromMilliseconds(20))]));
    private sealed class Wait(FakeClock clock) : IWaitStrategy
    {
        public Action<TimeSpan>? Before;
        private int count;
        public void WaitUntil(TimeSpan due, CancellationToken stop)
        {
            if (++count > 500) throw new InvalidOperationException("Unexpected unbounded scheduler loop.");
            Before?.Invoke(due); stop.ThrowIfCancellationRequested();
            if (due == TimeSpan.MaxValue) throw new InvalidOperationException("The test must wake an idle session explicitly.");
            if (due > clock.Elapsed) clock.Advance(due - clock.Elapsed);
        }
    }
    private sealed class Fixture
    {
        public readonly FakeClock Clock = new(); public readonly FakeInputSink Input = new();
        public readonly Guid TargetId = Guid.NewGuid(); public readonly Wait Waits; public readonly ClickExecutor Executor;
        public Fixture() { Waits = new(Clock); Executor = new(Input, Clock, Waits, new FakeForegroundProvider { Current = new(TargetId, true) }); }
    }
    [TestMethod]
    public void RecordedDesktopClickAndPauseRepeatWithoutEndingAfterFirstAction()
    {
        var f = new Fixture(); using var session = new ActionToggleSession();
        var mouse = InputControl.Mouse(PointerButton.Left);
        var point = new ClientPoint(400, 250, 2558, 1280, 96, CoordinateSpace.Desktop);
        var recorded = RecordingNormalizer.Normalize([
            new(TimeSpan.Zero, mouse, true, point),
            new(TimeSpan.FromMilliseconds(80), mouse, false, point),
            new(TimeSpan.FromMilliseconds(280), E, true),
            new(TimeSpan.FromMilliseconds(300), E, false)
        ], TimeSpan.FromMilliseconds(500)).Sequence;
        var plan = SequenceCompiler.Compile(recorded with { Repetition = new(3, TimeSpan.FromMilliseconds(20)) });
        session.Start(plan, "Recorded desktop", TimeSpan.Zero);
        var result = f.Executor.Execute(session, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(3, result.CompletedCycles);
        Assert.AreEqual(12, f.Input.Accepted.Count);
        Assert.AreEqual(6, f.Input.Accepted.Count(i => i.Position == point));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void HoldSurvivesActionCompletionAndCanBeReleasedIndependently()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); var hold = Hold(W);
        session.Toggle(hold); session.Start(Tap(E), "Tap E", TimeSpan.Zero);
        f.Executor.SessionActionChanged = (_, start) => { if (!start) { Assert.IsTrue(f.Executor.Owns(W)); Assert.IsFalse(f.Executor.Owns(E)); session.Toggle(hold); } };
        var result = f.Executor.Execute(session, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        CollectionAssert.AreEqual(new[] { W, E, E, W }, f.Input.Accepted.Select(i => i.Control).ToArray());
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void StartingActionsAfterAHoldDoesNotReleaseOrRepeatTheHold()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); var hold = Hold(W); session.Toggle(hold);
        bool started = false;
        f.Waits.Before = _ => { if (!started) { started = true; Assert.IsTrue(session.Start(Tap(E), "Tap E", TimeSpan.Zero)); } };
        f.Executor.SessionActionChanged = (_, start) => { if (!start) session.Toggle(hold); };
        var result = f.Executor.Execute(session, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(1, f.Input.Accepted.Count(i => i.Control == W && i.Edge == InputEdge.Down));
        Assert.AreEqual(W, f.Input.Accepted.Last().Control);
    }
    [TestMethod]
    public void ActionOnlyCancellationReleasesItsInputWhileKeepingUnrelatedHold()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); var hold = Hold(W); session.Toggle(hold);
        session.Start(SequenceCompiler.Compile(new([SequenceStep.Input("Long E", E, TimeSpan.FromSeconds(10))])), "Long E", TimeSpan.Zero);
        bool stopped = false;
        f.Waits.Before = _ => { if (!stopped && f.Executor.Owns(E)) { stopped = true; session.StopAction(); } };
        f.Executor.SessionActionChanged = (_, start) => { if (!start) { Assert.IsTrue(f.Executor.Owns(W)); Assert.IsFalse(f.Executor.Owns(E)); session.Toggle(hold); } };
        var result = f.Executor.Execute(session, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome, result.Error); Assert.IsTrue(stopped); Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void GlobalStopReleasesEveryOwnedInput()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); using var stop = new CancellationTokenSource();
        session.Toggle(Hold(W)); session.Start(Tap(E), "Tap E", TimeSpan.Zero);
        f.Waits.Before = _ => { if (f.Executor.Owns(E)) stop.Cancel(); };
        var result = f.Executor.Execute(session, f.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome); Assert.IsFalse(f.Executor.HasOwnedInput);
        Assert.AreEqual(2, f.Input.Accepted.Count(i => i.Edge == InputEdge.Up));
    }
    [TestMethod]
    public void ConflictingInputsAndCompetingPointerActionsAreRejectedBothDirections()
    {
        using var first = new ActionToggleSession(); first.Toggle(Hold(W));
        Assert.ThrowsExactly<ArgumentException>(() => first.Start(Tap(W), "Conflict", TimeSpan.Zero));
        using var second = new ActionToggleSession(); second.Start(Tap(E), "E", TimeSpan.Zero);
        Assert.ThrowsExactly<ArgumentException>(() => second.Toggle(Hold(E)));
        using var pointer = new ActionToggleSession(); pointer.Toggle(Hold(InputControl.Mouse(PointerButton.Right)));
        Assert.ThrowsExactly<ArgumentException>(() => pointer.Start(Tap(InputControl.Mouse(PointerButton.Left)), "Pointer", TimeSpan.Zero));
    }
    [TestMethod]
    public void ToggleCanBeReleasedDuringAConditionWaitWithoutResettingItsDeadline()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); var hold = Hold(W); session.Toggle(hold);
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2), 1));
        session.Start(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Wait", condition), SequenceStep.Input("Tap E", E, TimeSpan.FromMilliseconds(20))])), "Condition", TimeSpan.Zero);
        bool released = false; int samples = 0;
        f.Waits.Before = _ => { if (!released) { released = true; session.Toggle(hold); } };
        f.Executor.EvaluateCondition = (c, token) => new ConditionWaiter(f.Clock, f.Waits).Wait(c.Policy, new Probe(() => new(++samples == 3 ? ConditionObservationState.Met : ConditionObservationState.NotMet, 0, samples)), token, serviceInputs: () => f.Executor.ServiceConcurrentInputs(token));
        var result = f.Executor.Execute(session, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error); Assert.AreEqual(3, samples);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1020), f.Clock.Elapsed); Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    private sealed class Probe(Func<ConditionObservation> observe) : IConditionProbe { public ConditionObservation Observe(CancellationToken stop) => observe(); }
    private sealed class ConcurrentWait(FakeClock clock, ClickExecutor executor) : IWaitStrategy
    { public void WaitUntil(TimeSpan deadline, CancellationToken stop) => executor.WaitWithConcurrentInputs(deadline - clock.Elapsed, stop); }
    [TestMethod]
    public void SpamKeepsItsCadenceDuringSlowConditionPolling()
    {
        var f = new Fixture(); using var session = new ActionToggleSession();
        var spam = new NamedHelper(Guid.NewGuid(), "Spam", new(W, HelperMode.Repeat, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(2), 0));
        session.Toggle(spam);
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), 1));
        session.Start(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Wait", condition)])), "Condition", TimeSpan.Zero);
        int samples = 0;
        f.Executor.EvaluateCondition = (c, token) => new ConditionWaiter(f.Clock, new ConcurrentWait(f.Clock, f.Executor)).Wait(c.Policy,
            new Probe(() => new(++samples >= 3 ? ConditionObservationState.Met : ConditionObservationState.NotMet, 0, samples)), token,
            serviceInputs: () => f.Executor.ServiceConcurrentInputs(token));
        f.Executor.SessionActionChanged = (_, started) => { if (!started) session.Toggle(spam); };
        var result = f.Executor.Execute(session, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.IsTrue(f.Input.Accepted.Count(i => i.Edge == InputEdge.Down) >= 20);
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SpamRepeatsAndGlobalStopReleasesTheCurrentPress(bool mouse)
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); using var stop = new CancellationTokenSource();
        var input = mouse ? InputControl.Mouse(PointerButton.Left) : W;
        session.Toggle(new(Guid.NewGuid(), "Spam", new(input, HelperMode.Repeat, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(2), 0)));
        f.Waits.Before = _ => { if (f.Input.Accepted.Count(i => i.Edge == InputEdge.Down) >= 6) stop.Cancel(); };
        var result = f.Executor.Execute(session, f.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome, result.Error);
        Assert.AreEqual(6, f.Input.Accepted.Count(i => i.Edge == InputEdge.Down));
        Assert.AreEqual(6, f.Input.Accepted.Count(i => i.Edge == InputEdge.Up));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void SpamRunsDuringActionPausesAndCanStopIndependently()
    {
        var f = new Fixture(); using var session = new ActionToggleSession();
        var spam = new NamedHelper(Guid.NewGuid(), "Spam W", new(W, HelperMode.Repeat, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(2), 0));
        session.Toggle(spam);
        session.Start(SequenceCompiler.Compile(new([SequenceStep.Wait(TimeSpan.FromMilliseconds(100)), SequenceStep.Input("E", E, TimeSpan.FromMilliseconds(20))])), "Action", TimeSpan.Zero);
        f.Executor.SessionActionChanged = (_, started) => { if (!started) session.Toggle(spam); };
        var result = f.Executor.Execute(session, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.IsTrue(f.Input.Accepted.Count(i => i.Control == W && i.Edge == InputEdge.Down) >= 10);
        Assert.AreEqual(1, f.Input.Accepted.Count(i => i.Control == E && i.Edge == InputEdge.Down));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void MultipleSpamTogglesShareTheirRateBudgetWithoutStarving()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); using var stop = new CancellationTokenSource();
        foreach (var key in new[] { W, E }) session.Toggle(new(Guid.NewGuid(), "Spam", new(key, HelperMode.Repeat, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(2), 0)));
        f.Waits.Before = _ => { if (f.Clock.Elapsed >= TimeSpan.FromMilliseconds(100)) stop.Cancel(); };
        var result = f.Executor.Execute(session, f.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome, result.Error);
        Assert.IsTrue(f.Input.Accepted.Count(i => i.Edge == InputEdge.Down) <= 11);
        Assert.IsTrue(f.Input.Accepted.Count(i => i.Edge == InputEdge.Down && i.Control == W) >= 4);
        Assert.IsTrue(f.Input.Accepted.Count(i => i.Edge == InputEdge.Down && i.Control == E) >= 4);
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void ManyIndependentHoldsAreAllReleasedByGlobalStop()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); using var stop = new CancellationTokenSource();
        var holds = new[] { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1E, 0x1F }
            .Select(scan => Hold(InputControl.Key(scan))).ToArray();
        foreach (var hold in holds) Assert.IsTrue(session.Toggle(hold));
        Assert.HasCount(12, session.ActiveIds);
        f.Waits.Before = _ => { Assert.IsTrue(holds.All(h => f.Executor.Owns(h.Plan.Control))); stop.Cancel(); };
        var result = f.Executor.Execute(session, f.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(12, f.Input.Accepted.Count(i => i.Edge == InputEdge.Down));
        Assert.AreEqual(12, f.Input.Accepted.Count(i => i.Edge == InputEdge.Up));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void TurningOffToggleWhilePausedDoesNotRepressItOnResume()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); using var playback = new PlaybackControl();
        f.Executor.Playback = playback; var hold = Hold(W); session.Toggle(hold); session.Start(Tap(E), "E", TimeSpan.Zero);
        bool paused = false;
        f.Waits.Before = _ => { if (!paused && f.Executor.Owns(E)) { paused = true; playback.RequestPause(); } };
        f.Executor.PlaybackPaused = value => { if (value) { session.Toggle(hold); playback.RequestResume(); } };
        var result = f.Executor.Execute(session, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error); Assert.IsTrue(paused);
        Assert.AreEqual(1, f.Input.Accepted.Count(i => i.Control == W && i.Edge == InputEdge.Down));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void ASecondSessionRunIsRejectedWithoutDisturbingTheActiveSession()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); using var other = new ActionToggleSession();
        using var stop = new CancellationTokenSource(); session.Toggle(Hold(W));
        bool tried = false;
        f.Waits.Before = _ =>
        {
            if (tried) { stop.Cancel(); return; }
            tried = true;
            Assert.ThrowsExactly<InvalidOperationException>(() => f.Executor.Execute(other, f.TargetId, default));
            Assert.IsTrue(f.Executor.Owns(W));
            Assert.IsTrue(session.Toggle(Hold(E))); // The live session still accepts work after the rejected call.
        };
        var result = f.Executor.Execute(session, f.TargetId, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome, result.Error);
        Assert.AreEqual(1, f.Input.Accepted.Count(i => i.Control == E && i.Edge == InputEdge.Down));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public void PauseWithAMouseHoldToggleKeepsTheSessionAndRepressesOnResume()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); using var playback = new PlaybackControl();
        f.Executor.Playback = playback; var hold = Hold(InputControl.Mouse(PointerButton.Right)); session.Toggle(hold); session.Start(Tap(E), "E", TimeSpan.Zero);
        bool paused = false;
        f.Waits.Before = _ => { if (!paused && f.Executor.Owns(E)) { paused = true; playback.RequestPause(); } };
        f.Executor.PlaybackPaused = value => { if (value) playback.RequestResume(); };
        f.Executor.SessionActionChanged = (_, started) => { if (!started) session.Toggle(hold); };
        var result = f.Executor.Execute(session, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error); Assert.IsTrue(paused);
        // Pressed, released for the pause, pressed again on resume: a hold toggle is not a partial click.
        Assert.AreEqual(2, f.Input.Accepted.Count(i => i.Control.Kind == InputKind.MouseButton && i.Edge == InputEdge.Down));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }
    [TestMethod]
    public async Task ActionOnlyStopCleansItsKeyEvenWhenSchedulerIsBlocked()
    {
        var f = new Fixture(); using var session = new ActionToggleSession(); using var stop = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
        session.Toggle(Hold(W)); session.Start(Tap(E), "E", TimeSpan.Zero);
        f.Waits.Before = _ => { if (f.Executor.Owns(E)) { entered.Set(); if (!resume.Wait(TimeSpan.FromSeconds(4))) throw new TimeoutException(); } };
        var worker = Task.Run(() => f.Executor.Execute(session, f.TargetId, stop.Token));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(2))); session.StopAction();
            Assert.IsTrue(SpinWait.SpinUntil(() => !f.Executor.Owns(E), TimeSpan.FromSeconds(2)));
            Assert.IsTrue(f.Executor.Owns(W)); stop.Cancel(); Assert.IsFalse(f.Executor.HasOwnedInput);
        }
        finally { resume.Set(); }
        Assert.AreEqual(ClickRunOutcome.Cancelled, (await worker.WaitAsync(TimeSpan.FromSeconds(2))).Outcome);
    }
}
