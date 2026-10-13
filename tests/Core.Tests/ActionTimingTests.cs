using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class ActionTimingTests
{
    private static TimeSpan Ms(double value) => TimeSpan.FromMilliseconds(value);
    private static SequenceStep Click(QuickClickKind kind = QuickClickKind.Single) =>
        QuickActions.Click(PointerButton.Left, kind, Ms(20), Ms(80), Ms(20), new(20, 30, 800, 600, 96));
    [TestMethod]
    public void MaximizedActionsKeepClientOffsetsWithoutChangingStoredPointsOrDetectionCalibration()
    {
        var step = Click() with { LeaveCursorFree = true, Timing = new(Ms(40), Ms(80)) };
        var plan = SequenceCompiler.Compile(new([step]));
        var maximized = plan.AdaptClickPoints(1920, 1080, 96);
        Assert.AreEqual(new ClientPoint(20, 30, 1920, 1080, 96), maximized.Inputs[0].Position);
        Assert.AreEqual(new ClientPoint(20, 30, 800, 600, 96), plan.Inputs[0].Position);
        Assert.AreEqual(Ms(30), maximized.SampleTiming(() => 0.5).Inputs[1].Offset);
        Assert.AreEqual(new ClientPoint(30, 45, 1920, 1080, 144), plan.AdaptClickPoints(1920, 1080, 144).Inputs[0].Position);
        Assert.ThrowsExactly<ArgumentException>(() => plan.AdaptClickPoints(10, 10, 96));
        var drag = SequenceStep.Input("Drag", InputControl.Mouse(PointerButton.Left), Ms(200)) with { Position = new(20, 30, 800, 600, 96), EndPosition = new(100, 100, 800, 600, 96) };
        Assert.AreEqual(800, SequenceCompiler.Compile(new([drag])).AdaptClickPoints(1920, 1080, 96).Inputs[0].Position!.Width);
    }
    [TestMethod]
    public void DoubleClickRangesPreserveGapAndShiftFollowingConditionAndKey()
    {
        var click = Click(QuickClickKind.Double) with { Timing = new(Ms(40), Ms(60)) };
        var condition = SequenceStep.WaitUntil("Colour", new(ConditionKind.RegionColor, new(Ms(500), Ms(1000)), new(new(0, 0, 800, 600, 96), 1, 1)));
        var plan = SequenceCompiler.Compile(new([click, condition, QuickActions.Keys([0x1D, 0x11], Ms(20), Ms(20), false)]));
        var sampled = plan.SampleTiming(() => 0.5);
        CollectionAssert.AreEqual(new double[] { 0, 30, 110, 140, 180, 180, 200, 200 }, sampled.Inputs.Select(i => i.Offset.TotalMilliseconds).ToArray());
        Assert.AreEqual(Ms(180), sampled.Conditions[0].Offset);
        Assert.AreEqual(Ms(220), sampled.Duration);
        Assert.AreEqual(Ms(140), plan.Conditions[0].Offset);
        Assert.AreEqual(2, sampled.Inputs[2].ClickCount);
    }
    [TestMethod]
    public void ChordRandomizationKeepsAllKeysTogetherAndBoundsWorstCase()
    {
        var step = QuickActions.Keys([0x1D, 0x2A, 0x11], Ms(20), Ms(20), false) with { Timing = new(Ms(40), Ms(80)) };
        var plan = SequenceCompiler.Compile(new([step]));
        var sampled = plan.SampleTiming(() => 0.5);
        Assert.IsTrue(sampled.Inputs.Where(i => !i.Down).All(i => i.Offset == Ms(30)));
        Assert.AreEqual(Ms(80), sampled.Duration);
        Assert.AreEqual(Ms(120), plan.WorstCaseDuration);
        Assert.AreEqual(Ms(40), plan.SampleTiming(() => 0).Duration);
        Assert.ThrowsExactly<InvalidOperationException>(() => plan.SampleTiming(() => double.NaN));
    }
    [TestMethod]
    public void InvalidRangesMixedDeliveryAndUnboundedPressAreRejected()
    {
        foreach (var timing in new[] { new ActionTiming(Ms(10)), new ActionTiming(MaximumPause: Ms(10)),
            new ActionTiming(Ms(100), Interval: Ms(50)), new ActionTiming(MaximumInterval: Ms(200)),
            new ActionTiming(MaximumPause: TimeSpan.FromDays(1)) })
            Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([Click() with { Timing = timing }])));
        var free = Click() with { LeaveCursorFree = true };
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([free, Click()])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([free, QuickActions.Keys([0x11], Ms(20), Ms(20), false)])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(80), Ms(20)) with { LeaveCursorFree = true }])));
        Assert.IsTrue(SequenceCompiler.Compile(new([free, QuickActions.Pause(Ms(20))])).LeaveCursorFree);
    }
    [TestMethod]
    public void LegacyRandomCursorFreeHelpersBecomeEditableLoopWithOriginalInterval()
    {
        var helper = new HelperPlan(InputControl.Mouse(PointerButton.Left), HelperMode.Repeat, Ms(200), Ms(20), 3)
        { MaximumInterval = Ms(400), MaximumPressDuration = Ms(40), Position = new(20, 30, 800, 600, 96), LeaveCursorFree = true };
        var draft = SequenceCompiler.FromHelper(helper);
        var plan = SequenceCompiler.Compile(draft).SampleTiming(() => 0.5);
        Assert.AreEqual(1, draft.Steps.Count); Assert.AreEqual(3, plan.Repetition.Count);
        Assert.AreEqual(Ms(30), plan.Inputs[1].Offset); Assert.AreEqual(Ms(300), plan.Duration);
        Assert.IsTrue(plan.LeaveCursorFree); Assert.AreEqual(helper.Position, plan.Inputs[0].Position);
        var timed = SequenceCompiler.FromHelper(helper with { Count = 0, ActiveDuration = Ms(750) });
        Assert.AreEqual(Ms(750), timed.Repetition!.ActiveDuration);
    }
    [TestMethod]
    public void RandomCursorFreeSequenceRoundTripsAndOldSchemaStillLoads()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MEC-r15-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var step = Click() with { Timing = new(Ms(40), Ms(80)), LeaveCursorFree = true };
            var profile = new ProfileDefinition(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Ranges",
                [new(Guid.NewGuid(), "Click", null, Sequence: new([step], Repetition: new(3)))]);
            var file = Path.Combine(directory, "ranges.mecprofile"); HelperProfiles.Export(file, profile);
            var restored = HelperProfiles.Open(file).Macros[0].Sequence!;
            Assert.AreEqual(step.Timing, restored.Steps[0].Timing);
            Assert.IsTrue(restored.Steps[0].LeaveCursorFree);
            Assert.AreEqual(step.Id, restored.Steps[0].Id);
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
            json["schemaVersion"] = 11;
            var oldStep = json["macros"]![0]!["sequence"]!["steps"]!.AsArray()[0]!.AsObject();
            oldStep.Remove("timing"); oldStep.Remove("leaveCursorFree");
            File.WriteAllText(file, json.ToJsonString());
            var old = HelperProfiles.Open(file);
            Assert.AreEqual(ProfileDefinition.CurrentVersion, old.SchemaVersion); Assert.IsNull(old.Macros[0].Sequence!.Steps[0].Timing);
            Assert.IsFalse(SequenceCompiler.Compile(old.Macros[0].Sequence!).LeaveCursorFree);
        }
        finally { Directory.Delete(directory, true); }
    }
    [TestMethod]
    public void ReusableBodyRetainsRandomTimingAndDeliveryRestrictions()
    {
        var id = Guid.NewGuid();
        var step = Click() with { Timing = new(Ms(40), Ms(80)), LeaveCursorFree = true };
        var source = new SequenceDefinition([SequenceStep.Call("Call", id)], Subsequences: [new(id, "Body", [step])]);
        var plan = SequenceCompiler.Compile(source).SampleTiming(() => 0.5);
        Assert.AreEqual(Ms(30), plan.Inputs[1].Offset); Assert.IsTrue(plan.LeaveCursorFree);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(source with { Steps = [SequenceStep.Call("Call", id), Click()] }));
    }
}

