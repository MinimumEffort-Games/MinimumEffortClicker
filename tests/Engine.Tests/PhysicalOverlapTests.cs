using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class PhysicalOverlapTests
{
    [TestMethod]
    public void WithOnlyStopAllTheUsersOwnClicksNeverEndARun()
    {
        var only = EmergencyStopMode.OnlyStopAll;
        Assert.AreEqual(OverlapAction.Wait, PhysicalOverlap.For(only, InputEdge.Down, userHolds: true), "Press after the user lets go.");
        Assert.AreEqual(OverlapAction.HandOver, PhysicalOverlap.For(only, InputEdge.Up, userHolds: true), "The user's release finishes the click.");
        Assert.AreEqual(OverlapAction.Send, PhysicalOverlap.For(only, InputEdge.Down, userHolds: false));
        Assert.AreEqual(OverlapAction.Send, PhysicalOverlap.For(only, InputEdge.Up, userHolds: false));
    }

    [TestMethod]
    public void StopOnMyInputStillLetsTheUserWin()
    {
        var any = EmergencyStopMode.AnyInput;
        Assert.AreEqual(OverlapAction.Refuse, PhysicalOverlap.For(any, InputEdge.Down, userHolds: true));
        Assert.AreEqual(OverlapAction.Refuse, PhysicalOverlap.For(any, InputEdge.Up, userHolds: true));
        Assert.AreEqual(OverlapAction.Send, PhysicalOverlap.For(any, InputEdge.Down, userHolds: false));
    }
}
