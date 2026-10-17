using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class ControllerKeyRouterTests
{
    private static readonly InputControl W = InputControl.Key(0x11), Space = InputControl.Key(0x39), Left = InputControl.Mouse(PointerButton.Left);
    private static ControllerKeyBind Bind(ControllerButton button, InputControl output, bool active = true) => new(Guid.NewGuid(), "Bind", button, output, active);

    [TestMethod]
    public void TheKeyIsHeldExactlyAsLongAsTheButton()
    {
        var router = new ControllerKeyRouter();
        router.Configure([Bind(ControllerButton.R4, Space)]);
        CollectionAssert.AreEqual(new[] { (Space, true) }, router.Edge(ControllerButton.R4, true).ToArray());
        Assert.AreEqual(0, router.Edge(ControllerButton.R4, true).Count, "a repeated down presses nothing again");
        CollectionAssert.AreEqual(new[] { (Space, false) }, router.Edge(ControllerButton.R4, false).ToArray());
        Assert.AreEqual(0, router.Edge(ControllerButton.A, true).Count, "an unbound button presses nothing");
    }

    [TestMethod]
    public void ASharedKeyStaysDownUntilBothButtonsAreUp()
    {
        var router = new ControllerKeyRouter();
        router.Configure([Bind(ControllerButton.L4, W), Bind(ControllerButton.LeftStickUp, W)]);
        CollectionAssert.AreEqual(new[] { (W, true) }, router.Edge(ControllerButton.L4, true).ToArray());
        Assert.AreEqual(0, router.Edge(ControllerButton.LeftStickUp, true).Count);
        Assert.AreEqual(0, router.Edge(ControllerButton.L4, false).Count, "the stick still holds W");
        CollectionAssert.AreEqual(new[] { (W, false) }, router.Edge(ControllerButton.LeftStickUp, false).ToArray());
    }

    [TestMethod]
    public void InactiveBindsPressNothing()
    {
        var router = new ControllerKeyRouter();
        router.Configure([Bind(ControllerButton.RightTrigger, Left, active: false)]);
        Assert.AreEqual(0, router.Edge(ControllerButton.RightTrigger, true).Count);
    }

    [TestMethod]
    public void StopAndNewBindsReleaseWhatIsHeldAndLaterReleasesAreIgnored()
    {
        var router = new ControllerKeyRouter();
        router.Configure([Bind(ControllerButton.RightTrigger, Left), Bind(ControllerButton.R5, Space)]);
        router.Edge(ControllerButton.RightTrigger, true); router.Edge(ControllerButton.R5, true);
        CollectionAssert.AreEquivalent(new[] { Left, Space }, router.ReleaseAll().ToArray());
        Assert.AreEqual(0, router.Edge(ControllerButton.RightTrigger, false).Count, "Stop All already released it");
        // Binds stay armed after Stop All: the next press presses again.
        CollectionAssert.AreEqual(new[] { (Left, true) }, router.Edge(ControllerButton.RightTrigger, true).ToArray());
        CollectionAssert.AreEqual(new[] { Left }, router.Configure([Bind(ControllerButton.RightTrigger, W)]).ToArray());
        CollectionAssert.AreEqual(new[] { (W, true) }, router.Edge(ControllerButton.RightTrigger, true).ToArray());
    }
}
