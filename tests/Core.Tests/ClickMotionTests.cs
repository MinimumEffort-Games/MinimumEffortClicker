using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;
[TestClass]
public sealed class ClickMotionTests
{
    [TestMethod]
    public void SampledPressLengthDoesNotBecomeMovementTimeAndDisabledPausesAreSkipped()
    {
        var point = new ClientPoint(100, 100, 800, 600, 96);
        var first = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(0), Ms(100), point)
            with { Timing = new(Ms(40), Ms(200)) };
        var pause = QuickActions.Pause(Ms(100)) with { Timing = new(MaximumPause: Ms(200)) };
        var ignored = QuickActions.Pause(Ms(500)) with { Enabled = false };
        var next = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(0), Ms(0), point with { X = 200 });
        var source = new SequenceDefinition([first, pause, ignored, next]);
        var definition = QuickActions.WithClickMovement(source, true);
        var plan = SequenceCompiler.Compile(definition).SampleTiming(() => 0.5);
        var input = plan.Inputs.First(i => i.Down && i.StepIndex == 3);
        Assert.AreEqual(Ms(330), input.Offset); Assert.AreEqual(Ms(300), input.PauseBefore);
        Assert.IsFalse(source.Steps.Any(s => s.Motion is not null));
        Assert.IsFalse(SequenceCompiler.Compile(QuickActions.WithClickMovement(definition, false)).Inputs.Any(i => i.Motion is not null));
    }
    private static TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);
    private static SequenceStep Click() => QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(80), Ms(20), new(1, 1, 800, 600, 96));
    [TestMethod]
    public void VariationClampsToFrameAndSupportsNegativeDesktopOrigin()
    {
        var motion = new ClickMotion(Ms(200), 3);
        Assert.AreEqual(new ClientPoint(0, 0, 800, 600, 96), motion.Offset(Click().Children![0].Position!, () => 0));
        var desktop = new ClientPoint(-1919, -199, 1920, 1080, 96, CoordinateSpace.Desktop);
        Assert.AreEqual(desktop with { X = -1920, Y = -200 }, motion.Offset(desktop, () => 0, -1920, -200));
        Assert.ThrowsExactly<InvalidOperationException>(() => motion.Offset(desktop, () => double.NaN));
    }
    [TestMethod]
    public void MotionValidatesRangesDragAndCursorFreeAndRoundTrips()
    {
        var step = Click() with { Motion = new(Ms(200), 3) };
        var profile = new ProfileDefinition(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Motion", [new(Guid.NewGuid(), "Click", null, Sequence: new([step]))]);
        var file = Path.Combine(Path.GetTempPath(), "MEC-motion-" + Guid.NewGuid() + ".mecprofile");
        try
        {
            HelperProfiles.Export(file, profile);
            Assert.AreEqual(step.Motion, HelperProfiles.Open(file).Macros[0].Sequence!.Steps[0].Motion);
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!; json["schemaVersion"] = 13;
            json["macros"]![0]!["sequence"]!["steps"]![0]!.AsObject().Remove("motion"); File.WriteAllText(file, json.ToJsonString());
            Assert.IsNull(HelperProfiles.Open(file).Macros[0].Sequence!.Steps[0].Motion);
        }
        finally { File.Delete(file); }
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([step with { LeaveCursorFree = true }])));
        Assert.ThrowsExactly<ArgumentException>(() => new ClickMotion(Ms(1), 3).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => new ClickMotion(default, 26).Validate());
        SequenceCompiler.Compile(new([step with { LeaveCursorFree = true, Motion = new(default, 3) }]));
        var drag = SequenceStep.Input("Drag", InputControl.Mouse(PointerButton.Left), Ms(200)) with
        { Position = new(0, 0, 800, 600, 96), EndPosition = new(100, 100, 800, 600, 96), Motion = new(Ms(200), 3) };
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([drag])));
    }
    [TestMethod]
    public void RecordedClickCanUseRandomTimingAndPauseMovesFollowingAction()
    {
        var step = SequenceStep.Input("Recorded click", InputControl.Mouse(PointerButton.Left), Ms(20)) with
        { Position = new(10, 20, 800, 600, 96), EndPosition = new(10, 20, 800, 600, 96), Timing = new(Ms(40), Ms(20)) };
        var pause = QuickActions.Pause(Ms(100)) with { Timing = new(MaximumPause: Ms(200)) };
        var sampled = SequenceCompiler.Compile(new([pause, step])).SampleTiming(() => 0.5);
        Assert.AreEqual(Ms(150), sampled.Inputs[0].Offset);
        Assert.AreEqual(Ms(180), sampled.Inputs[1].Offset);
    }

    [TestMethod]
    public void PauseMovementRoundTripsAndVersion14StillLoadsStopped()
    {
        var step = Click() with { Motion = new(UsePreviousPause: true) };
        var profile = new ProfileDefinition(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Pause motion", [new(Guid.NewGuid(), "Click", null, Sequence: new([step]))]);
        var file = Path.Combine(Path.GetTempPath(), "MEC-pause-motion-" + Guid.NewGuid() + ".mecprofile");
        try
        {
            HelperProfiles.Export(file, profile); Assert.IsTrue(HelperProfiles.Open(file).Macros[0].Sequence!.Steps[0].Motion!.UsePreviousPause);
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!; json["schemaVersion"] = 14;
            json["macros"]![0]!["sequence"]!["steps"]![0]!["motion"]!.AsObject().Remove("usePreviousPause"); File.WriteAllText(file, json.ToJsonString());
            Assert.IsFalse(HelperProfiles.Open(file).Macros[0].Sequence!.Steps[0].Motion!.UsePreviousPause);
        }
        finally { File.Delete(file); }
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([step with { LeaveCursorFree = true }])));
    }
}
