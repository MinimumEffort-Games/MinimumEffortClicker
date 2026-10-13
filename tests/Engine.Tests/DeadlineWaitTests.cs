using System.Diagnostics;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

/// <summary>The one real-clock wait and clock the product uses outside Windows; every other test uses fakes.</summary>
[TestClass]
public sealed class DeadlineWaitTests
{
    [TestMethod]
    public void StopwatchClockIsMonotonic()
    {
        var clock = new StopwatchClock();
        var first = clock.Elapsed; Thread.Sleep(5); var second = clock.Elapsed;
        Assert.IsTrue(second > first);
    }

    [TestMethod]
    public void WaitUntilReturnsAfterTheDeadlineNotBefore()
    {
        var clock = new StopwatchClock(); var wait = new DeadlineWait(clock);
        var deadline = clock.Elapsed + TimeSpan.FromMilliseconds(40);
        wait.WaitUntil(deadline, default);
        Assert.IsTrue(clock.Elapsed >= deadline);
        var sw = Stopwatch.StartNew(); wait.WaitUntil(clock.Elapsed - TimeSpan.FromSeconds(1), default);
        Assert.IsTrue(sw.ElapsedMilliseconds < 50, "A past deadline must return at once.");
    }

    [TestMethod]
    public void StopInterruptsTheWaitPromptly()
    {
        var clock = new StopwatchClock(); var wait = new DeadlineWait(clock);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var sw = Stopwatch.StartNew();
        Assert.ThrowsExactly<OperationCanceledException>(() => wait.WaitUntil(clock.Elapsed + TimeSpan.FromSeconds(10), stop.Token));
        Assert.IsTrue(sw.ElapsedMilliseconds < 2000);
    }

    [TestMethod]
    public void ChangeSignalEndsTheWaitWithFalseAndDeadlineWithTrue()
    {
        var clock = new StopwatchClock(); var wait = new DeadlineWait(clock);
        using var changed = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        Assert.IsFalse(wait.WaitUntilOrChanged(clock.Elapsed + TimeSpan.FromSeconds(10), default, changed.Token));
        Assert.IsTrue(wait.WaitUntilOrChanged(clock.Elapsed + TimeSpan.FromMilliseconds(20), default, default));
    }
}
