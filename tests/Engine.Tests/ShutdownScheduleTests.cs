using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class ShutdownScheduleTests
{
    [TestMethod]
    public void ClickPlanUsesAcceptedClickTotalsRatherThanMacroLoopsAndCancelsBeforeCommit()
    {
        var schedule = new ShutdownSchedule();
        schedule.Arm(new(ShutdownTrigger.CompletedClicks, Clicks: 3), Now, 80, TimeSpan.Zero, clicks: 100);
        Assert.IsFalse(schedule.Update(Now, TimeSpan.Zero, 1000, TimeSpan.Zero, clicks: 102).CountingDown);
        Assert.IsTrue(schedule.Update(Now, TimeSpan.Zero, 1000, TimeSpan.Zero, clicks: 103).CountingDown);
        schedule.BeginCountdown(TimeSpan.Zero); schedule.Cancel();
        Assert.IsFalse(schedule.TryCommit(TimeSpan.FromSeconds(31), true, () => Assert.Fail("Cancelled shutdown executed.")));
        Assert.ThrowsExactly<ArgumentException>(() => new ShutdownRequest(ShutdownTrigger.CompletedClicks, Clicks: 0).Validate(Now));
    }
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    [TestMethod]
    public void ActiveTimeExcludesIdleAndAccumulatesAcrossRuns()
    {
        var schedule = new ShutdownSchedule();
        schedule.Arm(new(ShutdownTrigger.ActiveRunTime, ActiveTime: TimeSpan.FromMinutes(2)), Now, 10, TimeSpan.FromMinutes(5));
        var first = schedule.Update(Now.AddHours(1), TimeSpan.FromHours(1), 12, TimeSpan.FromMinutes(6));
        Assert.IsFalse(first.CountingDown); Assert.AreEqual(TimeSpan.FromMinutes(1), first.Remaining);
        var idle = schedule.Update(Now.AddHours(2), TimeSpan.FromHours(2), 12, TimeSpan.FromMinutes(6));
        Assert.AreEqual(first.Remaining, idle.Remaining);
        Assert.IsTrue(schedule.Update(Now.AddHours(3), TimeSpan.FromHours(3), 14, TimeSpan.FromMinutes(7)).CountingDown);
    }
    [TestMethod]
    public void LoopsUseArmingBaselineAndCommitOnceAfterVisibleCountdownAndCleanup()
    {
        var schedule = new ShutdownSchedule(); int shutdowns = 0;
        schedule.Arm(new(ShutdownTrigger.CompletedLoops, Loops: 3), Now, 80, TimeSpan.Zero);
        Assert.AreEqual(1L, schedule.Update(Now, TimeSpan.Zero, 82, TimeSpan.Zero).LoopsRemaining);
        Assert.AreEqual(TimeSpan.Zero, schedule.Update(Now, TimeSpan.Zero, 82, TimeSpan.Zero).Remaining); // Count plans report no time.
        Assert.IsTrue(schedule.Update(Now, TimeSpan.Zero, 83, TimeSpan.Zero).CountingDown);
        Assert.IsFalse(schedule.TryCommit(TimeSpan.FromHours(1), true, () => shutdowns++));
        schedule.BeginCountdown(TimeSpan.FromHours(1));
        Assert.IsFalse(schedule.TryCommit(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(29)), true, () => shutdowns++));
        Assert.IsFalse(schedule.TryCommit(TimeSpan.FromHours(2), false, () => shutdowns++));
        Assert.IsTrue(schedule.TryCommit(TimeSpan.FromHours(2), true, () => shutdowns++));
        Assert.IsFalse(schedule.TryCommit(TimeSpan.FromHours(3), true, () => shutdowns++));
        Assert.AreEqual(1, shutdowns);
    }
    [TestMethod]
    public void StopDuringCountdownPreventsShutdownAndDoesNotRearm()
    {
        var schedule = new ShutdownSchedule();
        schedule.Arm(new(ShutdownTrigger.SystemTime, At: Now.AddMinutes(1)), Now, 0, TimeSpan.Zero);
        Assert.IsTrue(schedule.Update(Now.AddMinutes(1), TimeSpan.FromMinutes(1), 0, TimeSpan.Zero).CountingDown);
        schedule.BeginCountdown(TimeSpan.FromMinutes(1)); schedule.Cancel();
        Assert.IsFalse(schedule.TryCommit(TimeSpan.FromMinutes(3), true, () => Assert.Fail("Cancelled shutdown executed.")));
        Assert.IsFalse(schedule.Update(Now.AddDays(1), TimeSpan.FromDays(1), 1000, TimeSpan.FromDays(1)).Armed);
    }
    [TestMethod]
    public void WallClockChangesCannotShortenCountdownAndInvalidPlansAreRejected()
    {
        var schedule = new ShutdownSchedule();
        Assert.ThrowsExactly<ArgumentException>(() => schedule.Arm(new(ShutdownTrigger.ActiveRunTime, ActiveTime: TimeSpan.Zero), Now, 0, TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentException>(() => schedule.Arm(new(ShutdownTrigger.CompletedLoops, Loops: -1), Now, 0, TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentException>(() => schedule.Arm(new(ShutdownTrigger.SystemTime, At: Now), Now, 0, TimeSpan.Zero));
        schedule.Arm(new(ShutdownTrigger.CompletedLoops, Loops: 1), Now, 0, TimeSpan.Zero);
        schedule.Update(Now, TimeSpan.Zero, 1, TimeSpan.Zero); schedule.BeginCountdown(TimeSpan.Zero);
        Assert.AreEqual(TimeSpan.FromSeconds(25), schedule.Update(Now.AddDays(100), TimeSpan.FromSeconds(5), 1, TimeSpan.Zero).Remaining);
    }
}
