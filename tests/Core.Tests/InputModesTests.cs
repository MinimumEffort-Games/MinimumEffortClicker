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
    public void LogicalBindingRetainsTheDraftAndRejectsPhysicalAliasesAndUnknownMappings()
    {
        var logical = new InputControl(InputKind.LogicalKey, 'A');
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("Logical A", logical, TimeSpan.FromMilliseconds(50))]));
        var resolved = plan.BindLogicalKeys(_ => 0x10, "040C040C");
        Assert.AreEqual(InputControl.Key(0x10), resolved.Inputs[0].Control);
        Assert.AreEqual(logical, plan.Inputs[0].Control);
        Assert.AreEqual("040C040C", resolved.KeyboardLayout);
        Assert.ThrowsExactly<ArgumentException>(() => plan.BindLogicalKeys(_ => 0x1D, "04090409"));
        Assert.ThrowsExactly<ArgumentException>(() => plan.BindLogicalKeys(_ => 0, "04090409"));
        var collision = SequenceCompiler.Compile(new([SequenceStep.Group("Collision", TimeSpan.FromMilliseconds(50), [
            new(Guid.NewGuid(), TimeSpan.Zero, logical, TimeSpan.FromMilliseconds(50)),
            new(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(0x1E), TimeSpan.FromMilliseconds(50))])]));
        Assert.ThrowsExactly<ArgumentException>(() => collision.BindLogicalKeys(_ => 0x1E, "04090409"));
        Assert.ThrowsExactly<ArgumentException>(() => resolved.BindLogicalKeys(_ => 0x1E, "04090409"));
    }

    [TestMethod]
    public void HorizontalScrollIsInstantAndSurvivesRecordingAndHelperConversion()
    {
        var scroll = new InputControl(InputKind.HorizontalScroll, -120);
        var draft = RecordingNormalizer.Normalize([new(TimeSpan.Zero, scroll, true)], TimeSpan.FromMilliseconds(100));
        var plan = SequenceCompiler.Compile(draft.Sequence);
        Assert.AreEqual(1, plan.Inputs.Count); Assert.AreEqual(scroll, plan.Inputs[0].Control);
        Assert.AreEqual(TimeSpan.Zero, SequenceTimeline.Create(plan)[0].Duration);
        var helper = new HelperPlan(scroll, HelperMode.Scroll, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1), 3);
        Assert.IsEmpty(helper.Validate()); Assert.AreEqual(3, SequenceCompiler.Compile(SequenceCompiler.FromHelper(helper)).Inputs.Count);
    }
}
