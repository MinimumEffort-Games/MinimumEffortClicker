using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class SequenceLibraryTests
{
    private static SequenceStep Tap() => SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20));
    [TestMethod]
    public void RepetitionIsValidatedAndPreservedThroughHistory()
    {
        var source = new SequenceDefinition([Tap()], Repetition: new(3, TimeSpan.FromMilliseconds(70)));
        var history = new SequenceHistory(source);
        history.Commit(source with { Repetition = new(0, ActiveDuration: TimeSpan.FromSeconds(2)) });
        history.Undo(); Assert.AreEqual(source.Repetition, history.Current.Repetition);
        Assert.AreEqual(3, SequenceCompiler.Compile(history.Current).Repetition.Count);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(source with { Repetition = new(-1) }));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(source with { Repetition = new(3, ActiveDuration: TimeSpan.FromSeconds(1)) }));
        Assert.AreEqual(1, SequenceCompiler.Compile(new([Tap()])).Repetition.Count);
    }
    [TestMethod]
    public void RepeatedCallsHaveIndependentOwnershipAndMapToTheirCallSite()
    {
        var body = new ReusableSequence(Guid.NewGuid(), "Tap", [Tap()]);
        var source = new SequenceDefinition([SequenceStep.Call("First", body.Id), SequenceStep.Call("Second", body.Id)], Subsequences: [body]);
        var plan = SequenceCompiler.Compile(source);
        Assert.HasCount(4, plan.Inputs);
        Assert.AreEqual(TimeSpan.FromMilliseconds(40), plan.Duration);
        Assert.AreNotEqual(plan.Inputs[0].ActionId, plan.Inputs[2].ActionId);
        CollectionAssert.AreEqual(new[] { 0, 1 }, SequenceTimeline.Create(plan).Select(r => r.StepIndex).ToArray());
        var history = new SequenceHistory(source);
        history.Commit(source with { Subsequences = [body with { Steps = [Tap() with { Duration = TimeSpan.FromMilliseconds(50) }] }] });
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), SequenceCompiler.Compile(history.Current).Duration);
        history.Undo(); Assert.AreEqual(TimeSpan.FromMilliseconds(40), SequenceCompiler.Compile(history.Current).Duration);
    }
    [TestMethod]
    public void MissingCyclesDepthAndDisabledInvalidBodiesAreRejected()
    {
        Guid id = Guid.NewGuid();
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Call("Missing", id) with { Enabled = false }])));
        var cyclic = new ReusableSequence(id, "Cycle", [SequenceStep.Call("Self", id)]);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([Tap()], Subsequences: [cyclic])));
        var invalid = cyclic with { Steps = [Tap() with { Duration = TimeSpan.FromSeconds(-1), Enabled = false }] };
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([Tap()], Subsequences: [invalid])));
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var nested = ids.Select((key, i) => new ReusableSequence(key, $"Level {i}", i == 4 ? [Tap()] : [SequenceStep.Call("Next", ids[i + 1])])).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Call("Deep", ids[0])], Subsequences: nested)));
    }
    [TestMethod]
    public void ExpandedActionBudgetIsEnforced()
    {
        var body = new ReusableSequence(Guid.NewGuid(), "Many", Enumerable.Range(0, 101).Select(_ => Tap()).ToArray());
        var calls = Enumerable.Range(0, 100).Select(_ => SequenceStep.Call("Call", body.Id)).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new(calls, Subsequences: [body])));
    }
    [TestMethod]
    public void ConditionalDependenciesArePreflightedAndBoundedIncludingUntakenPaths()
    {
        var body = new ReusableSequence(Guid.NewGuid(), "Wait", [SequenceStep.Wait(TimeSpan.FromSeconds(100))]);
        var check = new ConditionDefinition(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
        var flow = SequenceStep.WaitUntil("Repeat", check) with { Kind = StepKind.ConditionalFlow, Flow = new(FlowKind.RepeatUntil, body.Id, MaximumIterations: 1000) };
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([flow], Subsequences: [body])));
        flow = flow with { Flow = new(FlowKind.IfElse, body.Id, Guid.NewGuid()) };
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([flow], Subsequences: [body])));
        var cyclic = body with { Steps = [flow with { Flow = new(FlowKind.IfElse, body.Id) }] };
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([Tap()], Subsequences: [cyclic])));
        var hold = body with { Steps = [Tap()] };
        var plan = SequenceCompiler.Compile(new([flow with { Flow = new(FlowKind.IfElse, body.Id) }], Subsequences: [hold]));
        Assert.HasCount(0, plan.Inputs);
        Assert.AreEqual(2, plan.AllInputs().Count());
        Assert.AreEqual(TimeSpan.FromMilliseconds(1020), plan.WorstCaseDuration);
    }
    [TestMethod]
    public void SnapshotAndConditionOrderingSurviveReusableBodies()
    {
        var wait = SequenceStep.WaitUntil("Ready", new(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))));
        SequenceStep[] actions = [Tap(), wait, Tap()];
        var body = new ReusableSequence(Guid.NewGuid(), "Ready tap", actions);
        var source = new SequenceDefinition([SequenceStep.Call("Run", body.Id)], Subsequences: [body]);
        var snapshot = SequenceEdits.Snapshot(source); actions[0] = Tap() with { Duration = TimeSpan.FromSeconds(4) };
        var plan = SequenceCompiler.Compile(snapshot);
        Assert.AreEqual(TimeSpan.FromMilliseconds(40), plan.Duration);
        Assert.AreEqual(1, plan.Conditions[0].StepIndex);
        Assert.AreEqual(2, plan.Inputs[2].StepIndex);
        Assert.IsTrue(SequenceTimeline.Create(plan).All(r => r.StepIndex == 0));
    }
    [TestMethod]
    public void ExportCarriesReusableDependenciesAndImportKeepsInternalReferences()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mecprofile");
        try
        {
            var body = new ReusableSequence(Guid.NewGuid(), "Tap", [Tap()]);
            var sequence = new SequenceDefinition([SequenceStep.Call("Use", body.Id)], Subsequences: [body]);
            var profile = new ProfileDefinition(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Library", [new(Guid.NewGuid(), "Macro", null, Sequence: sequence)]);
            HelperProfiles.Export(path, profile);
            var loaded = HelperProfiles.Import(path);
            Assert.AreNotEqual(profile.Id, loaded.Id);
            var imported = loaded.Macros[0].Sequence!;
            Assert.AreEqual(imported.Subsequences![0].Id, imported.Steps[0].SubsequenceId);
            Assert.HasCount(2, SequenceCompiler.Compile(imported).Inputs);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
