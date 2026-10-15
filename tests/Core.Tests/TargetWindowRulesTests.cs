using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class TargetWindowRulesTests
{
    [TestMethod]
    public void HelperWindowsOfBackgroundProgramsAreLeftOut()
    {
        // Brother's update service: a 0×0 tool window.
        Assert.IsFalse(TargetWindowRules.IsChoosable(toolWindow: true, appWindow: false, owned: false, width: 0, height: 0, minimized: false));
        // Brother's iPrint&Scan monitor: a 14×14 window owned by another one.
        Assert.IsFalse(TargetWindowRules.IsChoosable(toolWindow: false, appWindow: false, owned: true, width: 14, height: 14, minimized: false));
        // A tiny but otherwise normal window.
        Assert.IsFalse(TargetWindowRules.IsChoosable(toolWindow: false, appWindow: false, owned: false, width: 30, height: 600, minimized: false));
    }

    [TestMethod]
    public void RealAppWindowsStayIncludingMinimisedOnes()
    {
        Assert.IsTrue(TargetWindowRules.IsChoosable(toolWindow: false, appWindow: false, owned: false, width: 1280, height: 720, minimized: false));
        Assert.IsTrue(TargetWindowRules.IsChoosable(toolWindow: false, appWindow: true, owned: false, width: 1280, height: 684, minimized: false), "Action! marks itself as an app window.");
        Assert.IsTrue(TargetWindowRules.IsChoosable(toolWindow: true, appWindow: true, owned: true, width: 800, height: 600, minimized: false), "The app-window mark wins, as in Alt+Tab.");
        Assert.IsTrue(TargetWindowRules.IsChoosable(toolWindow: false, appWindow: false, owned: false, width: 0, height: 0, minimized: true));
    }
}
