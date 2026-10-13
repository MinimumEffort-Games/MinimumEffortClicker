using System.Text.Json;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class ResizeClickTests
{
    private static readonly ClientPoint Saved = new(200, 150, 800, 600, 96);
    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
    private static SequenceStep Click(ClientPoint? point = null) => QuickActions.Click(PointerButton.Left,
        QuickClickKind.Double, Ms(20), Ms(80), Ms(20), point ?? Saved);

    [TestMethod]
    public void ScaledPointsUseOriginalFrameAtEverySizeWithoutDpiDoubleScalingOrDrift()
    {
        var point = Saved with { ScaleWithWindow = true };
        Assert.AreEqual((400, 300), point.Resolve(0, 0, 1600, 1200, 192));
        Assert.AreEqual((-1820, -25), point.Resolve(-1920, -100, 400, 300, 96));
        for (int i = 0; i < 1000; i++)
        {
            Assert.AreEqual((333, 194), point.Resolve(0, 0, 1333, 777, 144));
            Assert.AreEqual((200, 150), point.Resolve(0, 0, 800, 600, 96));
        }
        var edge = new ClientPoint(799, 599, 800, 600, 96) with { ScaleWithWindow = true };
        Assert.AreEqual((0, 0), edge.Resolve(0, 0, 1, 1, 96));
        Assert.IsTrue(edge.InFrame(65536, 65536, 960).IsValid);
        Assert.IsFalse(point.Matches(0, 600, 96)); Assert.IsFalse(point.Matches(800, 600, 0));
        Assert.IsFalse((point with { Space = CoordinateSpace.Desktop }).IsValid);
        Assert.ThrowsExactly<ArgumentException>(() => Saved.Resolve(0, 0, 1600, 1200, 96));
    }

    [TestMethod]
    public void ScalePolicyPreservesMacroAndRandomTimingWhileFixedModeKeepsOffsets()
    {
        var source = new SequenceDefinition([Click() with { LeaveCursorFree = true, Timing = new(Ms(40), Ms(80)) }], Repetition: new(10));
        var plan = SequenceCompiler.Compile(source);
        var scaled = plan.AdaptClickPoints(1600, 1200, 96, true).SampleTiming(() => 0.5);
        Assert.IsTrue(scaled.LeaveCursorFree); Assert.AreEqual(10, scaled.Repetition.Count);
        Assert.AreEqual(Ms(30), scaled.Inputs[1].Offset);
        foreach (var input in scaled.Inputs.Where(i => i.Down))
        {
            Assert.AreEqual((400, 300), input.Position!.Resolve(0, 0, 1600, 1200, 96));
            Assert.AreEqual((100, 75), input.Position.Resolve(0, 0, 400, 300, 96));
        }
        Assert.AreEqual(Saved, plan.Inputs[0].Position); Assert.AreEqual(Saved, source.Steps[0].Children![0].Position);
        var fixedPlan = plan.AdaptClickPoints(1600, 1200, 96, false);
        Assert.AreEqual((200, 150), fixedPlan.Inputs[0].Position!.Resolve(0, 0, 1600, 1200, 96));
        Assert.IsFalse(fixedPlan.Inputs[0].Position!.Matches(400, 300, 96));
    }

    [TestMethod]
    public void DragsDesktopPointsScrollAndColourAreasRemainStrictIncludingReturningDragPaths()
    {
        var drag = SequenceStep.Input("Drag", InputControl.Mouse(PointerButton.Right), Ms(100)) with { Position = Saved, EndPosition = Saved with { X = 500 } };
        var desktop = Click(Saved with { Space = CoordinateSpace.Desktop });
        var scroll = SequenceStep.Input("Wheel", new(InputKind.Scroll, 120), TimeSpan.Zero) with { Position = Saved };
        var colour = SequenceStep.WaitUntil("Colour", new(ConditionKind.RegionColor, new(Ms(500), Ms(2000)), new(Saved, 10, 10)));
        var plan = SequenceCompiler.Compile(new([drag, desktop, scroll, colour])).AdaptClickPoints(1600, 1200, 96, true);
        Assert.IsFalse(plan.Inputs.Any(i => i.Position?.ScaleWithWindow == true));
        Assert.IsFalse(plan.Conditions[0].Definition.Region!.Origin.Matches(1600, 1200, 96));
        Assert.ThrowsExactly<ArgumentException>(() => (new CaptureRegion(Saved with { ScaleWithWindow = true }, 10, 10)).Validate());
        var recorded = RecordingNormalizer.Normalize([
            new(TimeSpan.Zero, InputControl.Mouse(PointerButton.Left), true, Saved),
            new(Ms(30), new(InputKind.PointerMove, 0), true, Saved with { X = 300 }),
            new(Ms(60), InputControl.Mouse(PointerButton.Left), false, Saved)], Ms(60));
        var returningDrag = SequenceCompiler.Compile(recorded.Sequence).AdaptClickPoints(1600, 1200, 96, true);
        Assert.IsFalse(returningDrag.Inputs.Any(i => i.Position?.ScaleWithWindow == true));
    }

    [TestMethod]
    public void ScaledActionsNeverWriteRuntimePolicyIntoMacroCoordinates()
    {
        var definition = new SequenceDefinition([Click()]);
        var point = SequenceCompiler.Compile(definition).AdaptClickPoints(1200, 900, 96, true).Inputs[0].Position!;
        Assert.AreEqual((300, 225), point.Resolve(0, 0, 1200, 900, 96));
        Assert.AreEqual(800, point.Width);
        string json = JsonSerializer.Serialize(point);
        Assert.DoesNotContain("ScaleWithWindow", json);
        Assert.AreEqual(Saved, JsonSerializer.Deserialize<ClientPoint>(json));
    }

    [TestMethod]
    public void NewAndOlderPreferencesDefaultToScalingAndFixedChoiceSurvivesRestart()
    {
        string folder = Path.Combine(Path.GetTempPath(), "MEC-resize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new Preferences(folder);
            Assert.IsTrue(store.Load().ScaleClickPositions);
            File.WriteAllText(Path.Combine(folder, "preferences.json"), """{"SchemaVersion":1,"Appearance":2,"ToggleKey":119,"StopKey":120,"HoldTrigger":false,"Overlay":true,"ClicksPerSecond":8}""");
            var old = store.Load(); Assert.IsTrue(old.ScaleClickPositions); Assert.AreEqual(8, old.ClicksPerSecond); Assert.AreEqual(2, old.Appearance);
            store.Save(old with { ScaleClickPositions = false });
            Assert.IsFalse(store.Load().ScaleClickPositions); Assert.AreEqual(8, store.Load().ClicksPerSecond);
        }
        finally { Directory.Delete(folder, true); }
    }
}
