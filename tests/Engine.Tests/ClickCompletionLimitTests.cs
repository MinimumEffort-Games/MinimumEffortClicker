using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class ClickCompletionLimitTests
{
    private sealed class Wait(FakeClock clock) : IWaitStrategy
    {
        public void WaitUntil(TimeSpan due, CancellationToken stop)
        { stop.ThrowIfCancellationRequested(); if (due > clock.Elapsed) clock.Advance(due - clock.Elapsed); }
    }
    [TestMethod]
    public void ThreeClicksInsideOneMacroStopBeforeFourthPressAndLeaveNoHeldInput()
    {
        var clock = new FakeClock(); var sink = new FakeInputSink(); var target = Guid.NewGuid();
        var engine = new ClickExecutor(sink, clock, new Wait(clock), new FakeForegroundProvider { Current = new(target, true) })
        { ClickCompletionLimit = 3 };
        var clicks = Enumerable.Range(0, 4).Select(_ => QuickActions.Click(PointerButton.Left, QuickClickKind.Single,
            TimeSpan.FromMilliseconds(20), TimeSpan.Zero, TimeSpan.FromMilliseconds(80))).ToArray();
        var result = engine.Execute(SequenceCompiler.Compile(new(clicks)), target, default);
        Assert.AreEqual(ClickRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(3L, engine.TotalCompletedClicks);
        Assert.AreEqual(0L, engine.TotalCompletedLoops);
        Assert.AreEqual(3, sink.Accepted.Count(i => i.Edge == InputEdge.Down));
        Assert.AreEqual(3, sink.Accepted.Count(i => i.Edge == InputEdge.Up));
        Assert.IsTrue(result.CleanupSucceeded); Assert.IsFalse(engine.HasOwnedInput);
    }
}
