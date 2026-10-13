using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class ClickDefinitionTests
{
    [TestMethod]
    [DataRow(PointerButton.Left)]
    [DataRow(PointerButton.Right)]
    [DataRow(PointerButton.Middle)]
    [DataRow(PointerButton.XButton1)]
    [DataRow(PointerButton.XButton2)]
    public void SupportedButtonsHaveValidTiming(PointerButton button) =>
        Assert.HasCount(0, new ClickDefinition(button, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20)).Validate());

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(-1, 1)]
    [DataRow(100, 0)]
    [DataRow(100, -1)]
    [DataRow(100, 101)]
    [DataRow(86400001, 1)]
    public void InvalidTimingIsRejected(int intervalMs, int pressMs) =>
        Assert.IsNotEmpty(new ClickDefinition(PointerButton.Left, TimeSpan.FromMilliseconds(intervalMs), TimeSpan.FromMilliseconds(pressMs)).Validate());

    [TestMethod]
    public void BoundaryDurationsAreAccepted() =>
        Assert.HasCount(0, new ClickDefinition(PointerButton.Left, TimeSpan.FromDays(1), TimeSpan.FromDays(1)).Validate());

    [TestMethod]
    public void UnknownButtonIsRejected() =>
        Assert.IsNotEmpty(new ClickDefinition((PointerButton)999, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)).Validate());
}
