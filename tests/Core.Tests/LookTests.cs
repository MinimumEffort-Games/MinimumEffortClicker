using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class LookTests
{
    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);

    [TestMethod]
    public void ALookStepKeepsBothDirectionsAndRefusesNoMovement()
    {
        Assert.AreEqual((-120, 35), InputControl.Look(-120, 35).LookStep);
        Assert.AreEqual((32767, -32768), InputControl.Look(32767, -32768).LookStep);
        Assert.AreEqual((0, -1), InputControl.Look(0, -1).LookStep);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InputControl.Look(0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InputControl.Look(40000, 0));
        Assert.IsTrue(new HelperPlan(InputControl.Look(5, 0), HelperMode.Repeat, Ms(100), Ms(20), 0).Validate().Count > 0, "a toggle never holds a look");
    }

    [TestMethod]
    public void OneContinuousMovementBecomesOneLookAndARestStartsAnother()
    {
        RecordedEdge Move(int at, int dx, int dy) => new(Ms(at), InputControl.Look(dx, dy), true);
        var draft = RecordingNormalizer.Normalize([Move(100, 10, 0), Move(116, 12, -2), Move(132, 8, 1), Move(600, -5, 0), Move(616, -5, 0)], Ms(900));
        var steps = draft.Sequence.Steps;
        CollectionAssert.AreEqual(new[] { "Wait", "Look", "Wait", "Look", "Wait" }, steps.Select(s => s.Name).ToArray());
        var first = QuickActions.ReadLook(steps[1])!;
        Assert.AreEqual(3, first.Moves); Assert.AreEqual(Ms(32), first.Length); Assert.AreEqual((30, -1), (first.TotalX, first.TotalY));
        Assert.AreEqual((-10, 0), (QuickActions.ReadLook(steps[3])!.TotalX, QuickActions.ReadLook(steps[3])!.TotalY));
        // Played back, every step goes out at its recorded moment and holds nothing.
        var plan = SequenceCompiler.Compile(draft.Sequence);
        Assert.AreEqual(5, plan.Inputs.Count);
        Assert.IsTrue(plan.Inputs.All(i => i.Down && i.Position is null));
        CollectionAssert.AreEqual(new[] { 100d, 116d, 132d, 600d, 616d }, plan.Inputs.Select(i => i.Offset.TotalMilliseconds).ToArray());
    }

    [TestMethod]
    public void AClickWhileTurningStaysWithTheLook()
    {
        var left = InputControl.Mouse(PointerButton.Left);
        var draft = RecordingNormalizer.Normalize([new(Ms(0), InputControl.Look(4, 0), true), new(Ms(10), left, true),
            new(Ms(16), InputControl.Look(4, 0), true), new(Ms(60), left, false), new(Ms(70), InputControl.Look(4, 0), true)], Ms(100));
        var look = draft.Sequence.Steps.Single(s => s.Kind != StepKind.Wait);
        Assert.AreEqual("Look + Left", look.Name);
        Assert.IsNull(QuickActions.ReadLook(look), "a look with a click is not a plain Look");
        Assert.IsTrue(look.Children!.Single(c => c.Control == left).Position is null, "the click lands where the game has the cursor");
    }

    [TestMethod]
    public void ALookHasNoSpotAndOnlyItsPauseIsEdited()
    {
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Input("Look", InputControl.Look(3, 3), TimeSpan.Zero)
            with { Position = new ClientPoint(1, 1, 100, 100, 96) }])));
        var single = SequenceStep.Input("Look", InputControl.Look(3, 3), TimeSpan.Zero);
        var paused = QuickActions.WithLookPause(single, Ms(250));
        Assert.AreEqual(StepKind.TimedGroup, paused.Kind); Assert.AreEqual(single.Id, paused.Id);
        Assert.AreEqual(Ms(250), QuickActions.ReadLook(paused)!.After);
        var group = SequenceStep.Group("Look", Ms(40), [new(Guid.NewGuid(), Ms(0), InputControl.Look(1, 0), TimeSpan.Zero), new(Guid.NewGuid(), Ms(40), InputControl.Look(2, 0), TimeSpan.Zero)]);
        var longer = QuickActions.WithLookPause(group, Ms(500));
        Assert.AreEqual(Ms(540), longer.Duration); CollectionAssert.AreEqual(group.Children!.ToArray(), longer.Children!.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => QuickActions.WithLookPause(QuickActions.Pause(Ms(10)), Ms(5)));
        Assert.IsFalse(group.CanRunSeveralTimes);
    }
}
