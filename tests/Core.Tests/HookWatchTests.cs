using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class HookWatchTests
{
    [TestMethod]
    public void InputNoHookSawMeansTheHooksAreLost()
    {
        // A hook saw the last input: nothing to do.
        Assert.IsFalse(HookWatch.Missed(now: 20_000, lastInput: 10_000, lastKeyboardHook: 10_000, lastMouseHook: 3_000, lastReinstall: 0));
        Assert.IsFalse(HookWatch.Missed(now: 20_000, lastInput: 10_000, lastKeyboardHook: 2_000, lastMouseHook: 10_010, lastReinstall: 0));
        // Neither hook saw input that is a second old: reinstall.
        Assert.IsTrue(HookWatch.Missed(now: 20_000, lastInput: 19_000, lastKeyboardHook: 2_000, lastMouseHook: 3_000, lastReinstall: 0));
        // Too fresh to judge, or reinstalled a moment ago.
        Assert.IsFalse(HookWatch.Missed(now: 20_000, lastInput: 19_800, lastKeyboardHook: 2_000, lastMouseHook: 3_000, lastReinstall: 0));
        Assert.IsFalse(HookWatch.Missed(now: 20_000, lastInput: 19_000, lastKeyboardHook: 2_000, lastMouseHook: 3_000, lastReinstall: 17_000));
    }

    [TestMethod]
    public void TheMillisecondCounterWrappingChangesNothing()
    {
        uint before = uint.MaxValue - 100;
        Assert.IsFalse(HookWatch.Missed(now: 2_000, lastInput: 1_000, lastKeyboardHook: 1_000, lastMouseHook: before, lastReinstall: before));
        Assert.IsTrue(HookWatch.Missed(now: 2_000, lastInput: 1_000, lastKeyboardHook: before, lastMouseHook: before, lastReinstall: unchecked(2_000u - 6_000u)));
    }
}
