using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

/// <summary>A recorded drag repeated by Loop must press, move and release in every loop, not only the first.</summary>
[TestClass]
public sealed class DragLoopTests
{
    private sealed class TimedSink(FakeClock clock) : IInputSink
    {
        public readonly List<(TimeSpan At, InputTransition Input)> Sent = [];
        public bool TrySend(InputTransition transition) { Sent.Add((clock.Elapsed, transition)); return true; }
    }
    private sealed class Wait(FakeClock clock) : IWaitStrategy
    {
        public void WaitUntil(TimeSpan due, CancellationToken stop) { stop.ThrowIfCancellationRequested(); if (due > clock.Elapsed) clock.Advance(due - clock.Elapsed); }
    }
    private static TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);
    private static ClientPoint At(int x) => new(x, 100, 800, 600, 96);

    private static SequenceDefinition RecordedDrag(bool includePauses)
    {
        var left = InputControl.Mouse(PointerButton.Left); var move = new InputControl(InputKind.PointerMove, 0);
        var recording = RecordingNormalizer.Normalize([
            new(Ms(0), left, true, At(100)), new(Ms(30), move, true, At(200)), new(Ms(60), move, true, At(300)), new(Ms(90), left, false, At(300))
        ], Ms(150)).Sequence;
        return QuickActions.RecordedActions(recording, includePauses);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void EveryLoopPressesMovesAndReleases(bool includePauses)
    {
        var clock = new FakeClock(); var sink = new TimedSink(clock); var target = Guid.NewGuid();
        var engine = new ClickExecutor(sink, clock, new Wait(clock), new FakeForegroundProvider { Current = new(target, true) });
        var drag = RecordedDrag(includePauses) with { Repetition = new(3) };
        var result = engine.Execute(SequenceCompiler.Compile(drag), target, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(3, result.CompletedCycles);
        var buttons = sink.Sent.Where(s => s.Input.Control.Kind == InputKind.MouseButton).ToArray();
        CollectionAssert.AreEqual(new[] { InputEdge.Down, InputEdge.Up, InputEdge.Down, InputEdge.Up, InputEdge.Down, InputEdge.Up },
            buttons.Select(b => b.Input.Edge).ToArray(), string.Join(", ", sink.Sent.Select(s => $"{s.At.TotalMilliseconds}:{s.Input.Control.Kind}/{s.Input.Edge}@{s.Input.Position?.X}")));
        // Each press starts where the drag starts and each release ends where it ends.
        Assert.IsTrue(buttons.Where(b => b.Input.Edge == InputEdge.Down).All(b => b.Input.Position?.X == 100));
        Assert.IsTrue(buttons.Where(b => b.Input.Edge == InputEdge.Up).All(b => b.Input.Position?.X == 300));
        // With pauses recorded, the next loop's press waits the recorded 60 ms after the release (the time until the
        // recording stopped), so the target can settle. Without them the loops run back to back, as asked.
        var gap = buttons[2].At - buttons[1].At;
        if (includePauses) Assert.IsGreaterThanOrEqualTo(Ms(60), gap); else Assert.IsLessThan(Ms(60), gap);
        Assert.IsFalse(engine.HasOwnedInput);
    }
}
