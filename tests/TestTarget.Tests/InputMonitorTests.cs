using MinimumEffortClicker.TestTarget;

namespace TestTarget.Tests;

[TestClass]
public sealed class InputMonitorTests
{
    private static InputMonitor Focused()
    {
        var monitor = new InputMonitor();
        monitor.SetFocus(true, true);
        return monitor;
    }

    [TestMethod]
    public void DeferredViewKeepsExactImmediateCountsAndBoundedEventsWithOnePendingRefresh()
    {
        var monitor = new InputMonitor(deferViewUpdates: true); int invalidations = 0, notifications = 0;
        monitor.ViewInvalidated += () => invalidations++;
        monitor.PropertyChanged += (_, _) => notifications++;
        monitor.SetFocus(true, true);
        for (int i = 0; i < 500; i++) { monitor.ButtonDown("Left"); monitor.ButtonUp("Left", true); }
        Assert.AreEqual(500, monitor.ClickCount); Assert.AreEqual(500, monitor.ButtonUpEvents);
        Assert.AreEqual("None observed", monitor.ButtonsDown); Assert.AreEqual(InputMonitor.LogCapacity, monitor.Events.Count);
        Assert.AreEqual(1, invalidations); Assert.AreEqual(0, notifications); Assert.IsEmpty(monitor.VisibleEvents);
        monitor.RefreshView(); Assert.AreEqual(1, notifications); Assert.HasCount(InputMonitor.LogCapacity, monitor.VisibleEvents);
        monitor.RefreshView(); Assert.AreEqual(1, notifications);
        monitor.Reset(); Assert.AreEqual(0, monitor.ClickCount); Assert.AreEqual(2, invalidations);
        monitor.RefreshView(); Assert.HasCount(1, monitor.VisibleEvents); Assert.AreEqual(2, notifications);
    }

    [TestMethod]
    public void ClickRequiresMatchingDownAndUpInsideArea()
    {
        var monitor = Focused();
        monitor.ButtonUp("Left", true);
        Assert.AreEqual(0, monitor.ClickCount);
        monitor.ButtonDown("Left");
        monitor.ButtonUp("Left", false);
        Assert.AreEqual(0, monitor.ClickCount);
        monitor.ButtonDown("Right");
        monitor.ButtonUp("Right", true);
        Assert.AreEqual(1, monitor.ClickCount);
        Assert.AreEqual("Right: 1", monitor.ClickSummary);
    }

    [TestMethod]
    public void RepeatKeyDoesNotDuplicateHeldIndicator()
    {
        var monitor = Focused();
        monitor.KeyDown("W", false);
        monitor.KeyDown("W", true);
        Assert.AreEqual("W", monitor.KeysDown);
        monitor.KeyUp("W");
        Assert.AreEqual("None observed", monitor.KeysDown);
    }

    [TestMethod]
    public void FocusLossClearsObservedStateAndDoesNotInventACompletedClick()
    {
        var monitor = Focused();
        monitor.KeyDown("W", false);
        monitor.ButtonDown("Left");
        monitor.SetFocus(false, false);
        monitor.SetFocus(true, true);
        monitor.ButtonUp("Left", true);
        Assert.AreEqual("None observed", monitor.KeysDown);
        Assert.AreEqual("None observed", monitor.ButtonsDown);
        Assert.AreEqual(0, monitor.ClickCount);
    }

    [TestMethod]
    public void LeavingAreaInvalidatesPendingClick()
    {
        var monitor = Focused();
        monitor.ButtonDown("Left");
        monitor.PointerLeft();
        monitor.ButtonUp("Left", true);
        Assert.AreEqual(0, monitor.ClickCount);
    }

    [TestMethod]
    public void UnfocusedInputIsIgnored()
    {
        var monitor = new InputMonitor();
        monitor.KeyDown("W", false);
        monitor.ButtonDown("Left");
        monitor.Wheel(120);
        Assert.HasCount(0, monitor.Events);
    }

    [TestMethod]
    public void LogRemainsBoundedAndEvictsOldestEvents()
    {
        var monitor = Focused();
        for (int i = 0; i < 1000; i++) monitor.Wheel(i);
        Assert.HasCount(InputMonitor.LogCapacity, monitor.Events);
        StringAssert.EndsWith(monitor.Events[0], "Wheel: 800");
        StringAssert.EndsWith(monitor.Events[^1], "Wheel: 999");
    }

    [TestMethod]
    public void ResetClearsCountersHeldStateAndPriorLog()
    {
        var monitor = Focused();
        monitor.ButtonDown("Left");
        monitor.ButtonUp("Left", true);
        monitor.KeyDown("W", false);
        monitor.Reset();
        Assert.AreEqual(0, monitor.ClickCount);
        Assert.AreEqual("None observed", monitor.KeysDown);
        Assert.HasCount(1, monitor.Events);
        Assert.IsFalse(monitor.Events[0].Contains("Key down", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PointerCountersDistinguishTravelFromDragAndReset()
    {
        var monitor = new MinimumEffortClicker.TestTarget.InputMonitor(); monitor.SetFocus(true, true);
        monitor.PointerMoved(10, 20); monitor.ButtonDown("Right"); monitor.PointerMoved(30, 40); monitor.ButtonUp("Right", true);
        Assert.AreEqual(2, monitor.PointerMoves); Assert.AreEqual(1, monitor.DragMoves); Assert.AreEqual("30, 40", monitor.PointerPosition);
        monitor.Reset(); Assert.AreEqual(0, monitor.PointerMoves); Assert.AreEqual(0, monitor.DragMoves);
    }
}
