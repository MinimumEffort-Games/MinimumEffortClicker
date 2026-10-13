using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class TriggerLatchTests
{
    [TestMethod]
    public void ToggleWaitsForReleaseAndFiresOnce()
    {
        var latch = new TriggerLatch();
        Assert.AreEqual(TriggerCommand.None, latch.KeyEdge(true, false));
        Assert.AreEqual(TriggerCommand.None, latch.RegisteredHotkey(false));
        Assert.AreEqual(TriggerCommand.Toggle, latch.KeyEdge(false, false));
        Assert.AreEqual(TriggerCommand.None, latch.KeyEdge(false, false));
    }
    [TestMethod]
    public void ReleaseBeforeRegisteredMessageDoesNotLoseTheToggle()
    {
        var latch = new TriggerLatch();
        latch.KeyEdge(true, false); latch.KeyEdge(false, false);
        Assert.AreEqual(TriggerCommand.Toggle, latch.RegisteredHotkey(false));
    }
    [TestMethod]
    public void StopAllCancelsPendingStart()
    {
        var latch = new TriggerLatch();
        latch.KeyEdge(true, false); latch.RegisteredHotkey(false); latch.CancelPending();
        Assert.AreEqual(TriggerCommand.None, latch.KeyEdge(false, false));
    }
    [TestMethod]
    public void StopAllAlsoRejectsAnAlreadyReleasedButQueuedHotkey()
    {
        var latch = new TriggerLatch();
        latch.KeyEdge(true, false); latch.KeyEdge(false, false); latch.CancelPending();
        Assert.AreEqual(TriggerCommand.None, latch.RegisteredHotkey(false));
        latch.KeyEdge(true, false); latch.KeyEdge(false, false);
        Assert.AreEqual(TriggerCommand.Toggle, latch.RegisteredHotkey(false));
        Assert.AreEqual(TriggerCommand.None, latch.RegisteredHotkey(false));
    }
    [TestMethod]
    public void HoldTriggerStartsOnlyWhileDownAndStopsOnRelease()
    {
        var latch = new TriggerLatch();
        Assert.AreEqual(TriggerCommand.None, latch.RegisteredHotkey(true));
        latch.KeyEdge(true, true);
        Assert.AreEqual(TriggerCommand.Toggle, latch.RegisteredHotkey(true));
        Assert.AreEqual(TriggerCommand.Stop, latch.KeyEdge(false, true));
        Assert.AreEqual(TriggerCommand.None, latch.RegisteredHotkey(true));
    }
}
