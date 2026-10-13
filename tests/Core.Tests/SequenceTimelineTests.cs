using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class SequenceTimelineTests
{
    [TestMethod]
    public void TimelineKeepsOverlappingHoldsAndOriginalStepIndexes()
    {
        var w = InputControl.Key(0x11); var space = InputControl.Key(0x39);
        var group = SequenceStep.Group("Overlap", TimeSpan.FromSeconds(2), [
            new(Guid.NewGuid(), TimeSpan.Zero, w, TimeSpan.FromSeconds(2)),
            new(Guid.NewGuid(), TimeSpan.FromMilliseconds(500), space, TimeSpan.FromMilliseconds(100)),
            new(Guid.NewGuid(), TimeSpan.FromMilliseconds(700), space, TimeSpan.FromMilliseconds(100))]);
        var plan = SequenceCompiler.Compile(new([SequenceStep.Wait(TimeSpan.FromSeconds(1)) with { Enabled = false }, SequenceStep.Wait(TimeSpan.FromSeconds(1)), group]));
        var rows = SequenceTimeline.Create(plan);
        Assert.HasCount(3, rows);
        Assert.IsTrue(rows.All(r => r.StepIndex == 2));
        Assert.AreEqual(w, rows[0].Control);
        Assert.AreEqual(TimeSpan.FromSeconds(1), rows[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(2), rows[0].Duration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1500), rows[1].Start);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), rows[1].Duration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1700), rows[2].Start);
    }

    [TestMethod]
    public void ConditionsRemainUnknownDurationMarkersAndInstantInputsAreNotHolds()
    {
        var condition = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2)));
        var plan = SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Wait", condition),
            SequenceStep.Input("Scroll", new(InputKind.Scroll, 120), TimeSpan.Zero), SequenceStep.Wait(TimeSpan.FromSeconds(1))]));
        var rows = SequenceTimeline.Create(plan);
        Assert.HasCount(2, rows);
        Assert.AreEqual(condition, rows[0].Condition);
        Assert.AreEqual(TimeSpan.Zero, rows[0].Duration);
        Assert.AreEqual(1, rows[1].StepIndex);
        Assert.AreEqual(TimeSpan.Zero, rows[1].Start);
        Assert.AreEqual(TimeSpan.Zero, rows[1].Duration);
        Assert.AreEqual(TimeSpan.FromSeconds(1), plan.Duration);
    }
}
