using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class DesktopModeTests
{
    private static SequenceStep Click(ClientPoint? point = null) =>
        SequenceStep.Input("Left click", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(20)) with { Position = point };

    [TestMethod]
    public void DesktopCoordinatesStayAbsoluteAcrossWindowAdaptation()
    {
        var point = new ClientPoint(-900, 200, 3840, 1080, 96, CoordinateSpace.Desktop);
        var plan = SequenceCompiler.Compile(new([Click(point)]));
        plan.ValidateDesktopMode(3840, 1080, 96);
        var adapted = plan.AdaptClickPoints(3840, 1080, 96, true);
        Assert.AreEqual(point, adapted.Inputs[0].Position);
        Assert.AreEqual((-900, 200), point.Resolve(-1920, 0, 3840, 1080, 96));
    }

    [TestMethod]
    public void WindowCoordinatesAreNeverSilentlyUsedAsDesktopCoordinates()
    {
        var plan = SequenceCompiler.Compile(new([Click(new(30, 40, 1280, 720, 96))]));
        Assert.ThrowsExactly<ArgumentException>(() => plan.ValidateDesktopMode(1280, 720, 96));
    }

    [TestMethod]
    public void DesktopLayoutChangeRequiresRecalibration()
    {
        var plan = SequenceCompiler.Compile(new([Click(new(100, 200, 1920, 1080, 96, CoordinateSpace.Desktop))]));
        Assert.ThrowsExactly<ArgumentException>(() => plan.ValidateDesktopMode(2560, 1440, 96));
        Assert.ThrowsExactly<ArgumentException>(() => plan.ValidateDesktopMode(1920, 1080, 144));
    }

    [TestMethod]
    public void DesktopModeAllowsCursorKeyboardPauseAndScreenshotActions()
    {
        var plan = SequenceCompiler.Compile(new([Click(), SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20)),
            SequenceStep.Wait(TimeSpan.FromMilliseconds(20)), SequenceStep.Screenshot()]));
        plan.ValidateDesktopMode(1920, 1080, 96);
        Assert.HasCount(1, plan.Screenshots);
    }

    [TestMethod]
    public void CursorFreeModeRequiresSelectedWindow()
    {
        var plan = SequenceCompiler.Compile(new([Click(new(20, 30, 1280, 720, 96)) with { LeaveCursorFree = true }]));
        Assert.ThrowsExactly<ArgumentException>(() => plan.ValidateDesktopMode(1280, 720, 96));
    }

    [TestMethod]
    public void ConditionsRequireSelectedWindow()
    {
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5)));
        var plan = SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Wait", condition)]));
        Assert.ThrowsExactly<ArgumentException>(() => plan.ValidateDesktopMode(1920, 1080, 96));
    }
}
