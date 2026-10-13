using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;
[TestClass]
public sealed class ClickMotionTests
{
    [TestMethod]
    [DataRow(100, 5)]
    [DataRow(200, 10)]
    public void ListMovementUsesAfterActionPauseAndPreservesTotalDuration(int pause, int samples)
    {
        var clock = new FakeClock(); var input = new FakeInputSink(); var target = Guid.NewGuid();
        var engine = new ClickExecutor(input, clock, new Wait(clock), new FakeForegroundProvider { Current = new(target, true) })
        { CursorPosition = p => p with { X = 100, Y = 100 } };
        var first = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(0), Ms(pause), new(100, 100, 800, 600, 96));
        var next = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(0), Ms(0), new(300, 100, 800, 600, 96));
        var source = new SequenceDefinition([first, next]);
        var result = engine.Execute(SequenceCompiler.Compile(QuickActions.WithClickMovement(source, true)), target, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(Ms(pause + 40), clock.Elapsed);
        var moves = input.Accepted.Where(i => i.Control.Kind == InputKind.PointerMove).ToArray();
        Assert.AreEqual(samples, moves.Length);
        Assert.AreEqual(100 + 200 / samples, moves[0].Position!.X); Assert.AreEqual(300, moves[^1].Position!.X);
        Assert.AreEqual(InputEdge.Up, input.Accepted[1].Edge);
        Assert.IsFalse(engine.HasOwnedInput);
    }
    private sealed class Wait(FakeClock clock, Action<TimeSpan>? before = null) : IWaitStrategy
    {
        public void WaitUntil(TimeSpan due, CancellationToken stop)
        { before?.Invoke(due); stop.ThrowIfCancellationRequested(); if (due > clock.Elapsed) clock.Advance(due - clock.Elapsed); }
    }
    private static TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);
    private static SequenceStep Click() => QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(80), Ms(20), new(100, 100, 800, 600, 96)) with { Motion = new(Ms(64), 3) };
    [TestMethod]
    public void MovementIsPacedEachLoopAndReleaseUsesTheSameVariedPoint()
    {
        var clock = new FakeClock(); var input = new FakeInputSink(); var target = Guid.NewGuid();
        var engine = new ClickExecutor(input, clock, new Wait(clock), new FakeForegroundProvider { Current = new(target, true) })
        { NextRandom = () => 0, CursorPosition = point => point with { X = 0, Y = 0 } };
        var result = engine.Execute(SequenceCompiler.Compile(new([Click()], Repetition: new(3))), target, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(3, result.CompletedCycles); Assert.AreEqual(18, input.Accepted.Count);
        foreach (var chunk in input.Accepted.Chunk(6))
        {
            Assert.AreEqual(InputKind.PointerMove, chunk[0].Control.Kind);
            Assert.AreEqual(97, chunk[4].Position!.X);
            Assert.AreEqual(chunk[4].Position, chunk[5].Position);
        }
        Assert.IsTrue(clock.Elapsed >= Ms(312)); Assert.IsFalse(engine.HasOwnedInput);
    }
    [TestMethod]
    public void DoubleClickUsesOneOffsetAndShortMovesNeverExceedSixteenMillisecondPacing()
    {
        var clock = new FakeClock(); var input = new FakeInputSink(); var target = Guid.NewGuid(); var dueTimes = new List<TimeSpan>();
        var engine = new ClickExecutor(input, clock, new Wait(clock, dueTimes.Add), new FakeForegroundProvider { Current = new(target, true) })
        { NextRandom = () => 0, CursorPosition = point => point with { X = 0, Y = 0 } };
        var step = QuickActions.Click(PointerButton.Left, QuickClickKind.Double, Ms(20), Ms(80), Ms(20), new(100, 100, 800, 600, 96)) with { Motion = new(Ms(33), 3) };
        var result = engine.Execute(SequenceCompiler.Compile(new([step])), target, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome);
        Assert.AreEqual(2, input.Accepted.Count(i => i.Control.Kind == InputKind.PointerMove));
        Assert.IsTrue(input.Accepted.Where(i => i.Control.Kind == InputKind.MouseButton).All(i => i.Position == new ClientPoint(97, 97, 800, 600, 96)));
        Assert.IsTrue(dueTimes.Where(t => t > TimeSpan.Zero).First() >= Ms(16));
    }
    [TestMethod]
    public void StopDuringMovementNeverPressesButton()
    {
        var clock = new FakeClock(); var input = new FakeInputSink(); var target = Guid.NewGuid(); using var stop = new CancellationTokenSource();
        var engine = new ClickExecutor(input, clock, new Wait(clock, due => { if (due >= Ms(32)) stop.Cancel(); }), new FakeForegroundProvider { Current = new(target, true) })
        { CursorPosition = point => point with { X = 0, Y = 0 }, NextRandom = () => 0 };
        var result = engine.Execute(SequenceCompiler.Compile(new([Click()])), target, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.IsTrue(input.Accepted.All(i => i.Control.Kind == InputKind.PointerMove));
        Assert.IsFalse(engine.HasOwnedInput);
    }

    [TestMethod]
    public void RecordedPauseIsUsedForVisibleTravelWithoutAddingTime()
    {
        var clock = new FakeClock(); var input = new FakeInputSink(); var target = Guid.NewGuid(); var times = new List<TimeSpan>();
        var engine = new ClickExecutor(input, clock, new Wait(clock, times.Add), new FakeForegroundProvider { Current = new(target, true) })
        { CursorPosition = p => p with { X = 0, Y = 0 } };
        var click = Click() with { Motion = new(UsePreviousPause: true) };
        var plan = SequenceCompiler.Compile(new([click, QuickActions.Pause(Ms(200)), click with { Id = Guid.NewGuid(), Children = [new(Guid.NewGuid(), default, InputControl.Mouse(PointerButton.Left), Ms(20), new(200, 100, 800, 600, 96))] }]));
        var result = engine.Execute(plan, target, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(Ms(280), clock.Elapsed);
        // Travel now includes the first click's 20ms after-pause plus the explicit 200ms Pause.
        Assert.AreEqual(11, input.Accepted.Count(i => i.Control.Kind == InputKind.PointerMove));
        Assert.AreEqual(InputEdge.Up, input.Accepted[1].Edge);
        Assert.AreEqual(InputKind.PointerMove, input.Accepted[2].Control.Kind);
        Assert.AreEqual(200, input.Accepted[^2].Position!.X);
        Assert.IsFalse(engine.HasOwnedInput);
    }
    [TestMethod]
    public void StopDuringRecordedPauseTravelDoesNotClickNextLocation()
    {
        var clock = new FakeClock(); var input = new FakeInputSink(); var target = Guid.NewGuid(); using var stop = new CancellationTokenSource();
        var engine = new ClickExecutor(input, clock, new Wait(clock, due => { if (due >= Ms(100)) stop.Cancel(); }), new FakeForegroundProvider { Current = new(target, true) })
        { CursorPosition = p => p with { X = 0, Y = 0 } };
        var click = Click() with { Motion = new(UsePreviousPause: true) };
        var result = engine.Execute(SequenceCompiler.Compile(new([QuickActions.Pause(Ms(200)), click])), target, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.IsTrue(input.Accepted.All(i => i.Control.Kind == InputKind.PointerMove)); Assert.IsFalse(engine.HasOwnedInput);
    }
    [TestMethod]
    public void RecordedDragRetainsPathTimingAndStopReleasesButton()
    {
        var clock = new FakeClock(); var input = new FakeInputSink(); var target = Guid.NewGuid(); using var stop = new CancellationTokenSource();
        var engine = new ClickExecutor(input, clock, new Wait(clock, due => { if (due >= Ms(150)) stop.Cancel(); }), new FakeForegroundProvider { Current = new(target, true) });
        var button = InputControl.Mouse(PointerButton.Right); var point = new ClientPoint(10, 10, 800, 600, 96);
        var draft = RecordingNormalizer.Normalize([new(default, button, true, point),
            new(Ms(50), new(InputKind.PointerMove, 0), true, point with { X = 20 }),
            new(Ms(100), new(InputKind.PointerMove, 0), true, point with { X = 30 }),
            new(Ms(200), button, false, point with { X = 40 })], Ms(200));
        var result = engine.Execute(SequenceCompiler.Compile(QuickActions.RecordedActions(draft.Sequence, false, true)), target, stop.Token);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(InputEdge.Down, input.Accepted[0].Edge);
        Assert.AreEqual(20, input.Accepted[1].Position!.X); Assert.AreEqual(30, input.Accepted[2].Position!.X);
        Assert.AreEqual(InputEdge.Up, input.Accepted[^1].Edge); Assert.IsFalse(engine.HasOwnedInput);
    }
}
