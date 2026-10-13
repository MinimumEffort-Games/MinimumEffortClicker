using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class InputModesTests
{
    [TestMethod]
    public void DesktopCoordinatesStayFixedWhileClientCoordinatesFollowMovedWindows()
    {
        var client = new ClientPoint(100, 50, 800, 600, 144);
        var desktop = new ClientPoint(-1100, 100, 800, 600, 144, CoordinateSpace.Desktop);
        Assert.AreEqual((-1100, 100), desktop.Resolve(-1200, 50, 800, 600, 144));
        Assert.AreEqual((-1100, 100), desktop.Resolve(-1000, 90, 800, 600, 144));
        Assert.AreEqual((-900, 140), client.Resolve(-1000, 90, 800, 600, 144));
        Assert.IsFalse(desktop.Matches(800, 600, 96));
        Assert.IsFalse((desktop with { Space = (CoordinateSpace)99 }).IsValid);
        Assert.ThrowsExactly<ArgumentException>(() => new CaptureRegion(desktop, 1, 1).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => desktop.Resolve(0, 0, 900, 600, 144));
    }

    [TestMethod]
    public void HorizontalScrollIsInstantAndSurvivesRecordingAndHelperConversion()
    {
        var scroll = new InputControl(InputKind.HorizontalScroll, -120);
        var draft = RecordingNormalizer.Normalize([new(TimeSpan.Zero, scroll, true)], TimeSpan.FromMilliseconds(100));
        var plan = SequenceCompiler.Compile(draft.Sequence);
        Assert.AreEqual(1, plan.Inputs.Count); Assert.AreEqual(scroll, plan.Inputs[0].Control);
        Assert.IsTrue(plan.Inputs[0].Down); // A scroll is instantaneous: one edge, nothing held.
        var helper = new HelperPlan(scroll, HelperMode.Scroll, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1), 3);
        Assert.IsEmpty(helper.Validate()); Assert.AreEqual(3, SequenceCompiler.Compile(SequenceCompiler.FromHelper(helper)).Inputs.Count);
    }
}
