using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class QuickActionsTests
{
    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
    [TestMethod]
    public void DoubleClickCompilesBothClicksAtSavedPointWithDelayAfterRelease()
    {
        var point = new ClientPoint(10, 20, 800, 600, 96);
        var click = QuickActions.Click(PointerButton.Right, QuickClickKind.Double, Ms(20), Ms(80), Ms(230), point);
        var plan = SequenceCompiler.Compile(new([click, QuickActions.Pause(Ms(500))], Repetition: new(3)));
        CollectionAssert.AreEqual(new[] { 0d, 20d, 100d, 120d }, plan.Inputs.Select(i => i.Offset.TotalMilliseconds).ToArray());
        Assert.IsTrue(plan.Inputs.Where(i => i.Down).All(i => i.Position == point));
        Assert.IsTrue(plan.Inputs.Where(i => !i.Down).All(i => i.Position is null));
        Assert.AreEqual(Ms(850), plan.Duration); Assert.AreEqual(3, plan.Repetition.Count);
    }
    [TestMethod]
    public void ChordAndHoldReleaseBeforeNextAction()
    {
        var chord = QuickActions.Keys([0x1D, 0x2A, 0x11], Ms(1000), Ms(200), true);
        var click = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(80), Ms(230));
        var plan = SequenceCompiler.Compile(new([chord, click]));
        Assert.IsTrue(SequenceKeys.IsModifier(plan.Inputs[0].Control));
        Assert.AreEqual(0x11, plan.Inputs[3].Control.Code); Assert.IsFalse(plan.Inputs[3].Down);
        Assert.AreEqual(Ms(1200), plan.Inputs[6].Offset);
        Assert.AreEqual(4, plan.Inputs.Count(i => i.Down));
    }
    [TestMethod]
    public void OptionalRecordingPausesPreserveDurationsPositionsAndOverlaps()
    {
        var recorded = RecordingNormalizer.Normalize([
            new(Ms(100), InputControl.Key(0x11), true), new(Ms(200), InputControl.Key(0x39), true),
            new(Ms(240), InputControl.Key(0x39), false), new(Ms(500), InputControl.Key(0x11), false)], Ms(900)).Sequence with { KeyboardLayout = "04090409" };
        var withPauses = QuickActions.RecordedActions(recorded, true);
        // The wait after the last input (until recording stopped) is kept when pauses are recorded.
        Assert.AreEqual(2, withPauses.Steps.Count); Assert.AreEqual(recorded.Steps[1], withPauses.Steps[0]); Assert.AreEqual(Ms(400), withPauses.Steps[1].Duration);
        var without = QuickActions.RecordedActions(recorded, false);
        Assert.AreEqual(1, without.Steps.Count); Assert.AreEqual(recorded.Steps[1], without.Steps[0]);
        Assert.AreEqual("04090409", without.KeyboardLayout);
        var plan = SequenceCompiler.Compile(without);
        CollectionAssert.AreEqual(new[] { 0d, 100d, 140d, 400d }, plan.Inputs.Select(i => i.Offset.TotalMilliseconds).ToArray());
    }
    [TestMethod]
    public void InvalidDurationsAndDuplicateChordKeysAreRejectedBeforePlayback()
    {
        Assert.ThrowsExactly<ArgumentException>(() => QuickActions.Pause(Ms(0)));
        Assert.ThrowsExactly<ArgumentException>(() => QuickActions.Keys([0x11, 0x11], Ms(20), Ms(230), false));
        Assert.ThrowsExactly<ArgumentException>(() => QuickActions.Click(PointerButton.Left, QuickClickKind.Double, Ms(20), Ms(-1), Ms(230)));
        Assert.ThrowsExactly<ArgumentException>(() => QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(80), Ms(-1)));
    }
    [TestMethod]
    public void AnActionThatRunsSeveralTimesSendsBackToBackRunsUnderItsOwnRow()
    {
        var click = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(80), Ms(30)) with { Times = 3 };
        var key = QuickActions.Keys([0x11], Ms(10), Ms(0), false);
        var plan = SequenceCompiler.Compile(new([click, key]));
        CollectionAssert.AreEqual(new[] { 0d, 20d, 50d, 70d, 100d, 120d, 150d, 160d }, plan.Inputs.Select(i => i.Offset.TotalMilliseconds).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0, 0, 1, 1 }, plan.Inputs.Select(i => i.ListStep).ToArray());
        Assert.AreEqual(4, plan.Inputs.Select(i => i.StepIndex).Distinct().Count());
        Assert.AreEqual(3, plan.Inputs.Where(i => i.Down && i.ListStep == 0).Select(i => i.ActionId).Distinct().Count());
        Assert.AreEqual("Left click (2/3)", plan.Inputs[2].Name);
        Assert.AreEqual(Ms(160), plan.Duration);
    }
    [TestMethod]
    public void OnlyClicksAndKeysRunSeveralTimesAndTheCountIsBounded()
    {
        var scroll = QuickActions.Scroll(ScrollDirection.Down, 3, Ms(30), Ms(0)) with { Times = 2 };
        Assert.IsFalse(scroll.CanRunSeveralTimes);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([scroll])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([QuickActions.Pause(Ms(10)) with { Times = 2 }])));
        var click = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(1), Ms(0), Ms(0));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([click with { Times = 0 }])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([click with { Times = SequenceStep.MaximumTimes + 1 }])));
        Assert.AreEqual(2 * SequenceStep.MaximumTimes, SequenceCompiler.Compile(new([click with { Times = SequenceStep.MaximumTimes }])).Inputs.Count);
        // Rows that each run 9,999 times add up; one loop stays within the input budget.
        var many = Enumerable.Range(0, 11).Select(_ => SequenceEdits.CopyWithNewIds([click with { Times = SequenceStep.MaximumTimes }])[0]).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new(many)));
        // A skipped row sends nothing, whatever its count.
        Assert.AreEqual(0, SequenceCompiler.Compile(new(many.Select(s => s with { Enabled = false }).ToArray())).Inputs.Count);
    }
    [TestMethod]
    public void AOnceActionRunsAllItsTimesInTheFirstLoopOnly()
    {
        var click = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(10), Ms(0), Ms(10)) with { Times = 4, Once = true };
        var plan = SequenceCompiler.Compile(new([click, QuickActions.Pause(Ms(50))], Repetition: new(0)));
        Assert.AreEqual(4, plan.Inputs.Count(i => i.Down));
        Assert.AreEqual(0, plan.LaterLoops!.Inputs.Count);
    }
}
