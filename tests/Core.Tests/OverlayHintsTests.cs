using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class OverlayHintsTests
{
    [TestMethod]
    public void TheShortcutLineNamesTheStopThatWorksRightNow()
    {
        var both = EmergencyStopMode.BothMouseButtons; var any = EmergencyStopMode.AnyInput;
        Assert.AreEqual("F8 to Start/Stop - LMB+RMB to Stop", OverlayHints.WhileRunning("F8", "F9", both, actionsActive: true, togglesActive: false));
        Assert.AreEqual("F8 to Start/Stop - F9 to Stop All Toggles", OverlayHints.WhileRunning("F8", "F9", both, actionsActive: false, togglesActive: true));
        Assert.AreEqual("F8 to Start/Stop - LMB+RMB to Stop - F9 to Stop All", OverlayHints.WhileRunning("F8", "F9", both, actionsActive: true, togglesActive: true));
        Assert.AreEqual("F8 to Start/Stop - F9 to Stop All", OverlayHints.WhileRunning("F8", "F9", any, actionsActive: true, togglesActive: false));
        // Rebound keys show up as bound.
        Assert.AreEqual("F6 to Start/Stop - Pause to Stop All Toggles", OverlayHints.WhileRunning("F6", "Pause", any, actionsActive: false, togglesActive: true));
        Assert.AreEqual("F8 to Start/Stop", OverlayHints.WhileRunning("F8", "F9", both, actionsActive: false, togglesActive: false));
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
