using MinimumEffortClicker.Linux;

namespace Linux.Tests;

[TestClass]
public sealed class InputAccessTests
{
    [TestMethod]
    public void ThePasswordWindowsAnswersReadPlainly()
    {
        Assert.AreEqual((true, "Input access is on."), LinuxSetup.DescribeInputAccessResult(0, on: true, ""));
        Assert.AreEqual((true, "Input access is off."), LinuxSetup.DescribeInputAccessResult(0, on: false, ""));
        StringAssert.StartsWith(LinuxSetup.DescribeInputAccessResult(126, on: true, "").Message, "Cancelled");
        StringAssert.Contains(LinuxSetup.DescribeInputAccessResult(127, on: true, "").Message, "terminal");
        var failed = LinuxSetup.DescribeInputAccessResult(1, on: true, "udevadm: not found");
        Assert.IsFalse(failed.Done);
        StringAssert.EndsWith(failed.Message, "udevadm: not found");
    }
}
