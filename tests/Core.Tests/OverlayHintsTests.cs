using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class OverlayHintsTests
{
    [TestMethod]
    public void TheShortcutLineUsesTheBoundKeys()
    {
        Assert.AreEqual("F8 to Start/Stop", OverlayHints.StartStop("F8"));
        Assert.AreEqual("F9 to Stop All", OverlayHints.StopAll("F9"));
        Assert.AreEqual("Pause to Stop All", OverlayHints.StopAll("Pause"));
    }

    [TestMethod]
    public void TheActionsLineDropsTheWordActionsAndSaysLooping()
    {
        Assert.AreEqual("10 Actions · Looping", OverlayHints.Activity("Actions · 10 actions · until stopped"));
        Assert.AreEqual("1 Action · Once", OverlayHints.Activity("Actions · 1 action · once"));
        Assert.AreEqual("3 Actions · 5 Loops", OverlayHints.Activity("Actions · 3 actions · 5 loops"));
        Assert.AreEqual("2 Actions · 30s active time", OverlayHints.Activity("Actions · 2 actions · 30s active time"));
        Assert.AreEqual("My macro · until stopped", OverlayHints.Activity("My macro · until stopped"));
    }

    [TestMethod]
    public void ToggleStatesAreReadyOnOrTheRunsOwnState()
    {
        Assert.AreEqual("Ready", OverlayToggle.StateFor(false, active: false, playbackActive: true, "Running"));
        Assert.IsTrue(new OverlayToggle("W", "1", OverlayToggle.StateFor(false, true, true, "Running"), true).On);
        Assert.AreEqual("Paused", OverlayToggle.StateFor(false, true, false, "Paused"));
        Assert.AreEqual("Release pending", OverlayToggle.StateFor(true, true, true, "Running"));
    }
}
