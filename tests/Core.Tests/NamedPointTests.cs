using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class NamedPointTests
{
    private static readonly ClientPoint Position = new(100, 200, 800, 600, 96);
    private static SequenceStep Click(Guid targetId) => SequenceStep.Input("Click", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(20)) with { TargetId = targetId };
    [TestMethod]
    public void SharedPointUpdateAffectsFutureCompilationsButNotActiveSnapshot()
    {
        var point = new NamedPoint(Guid.NewGuid(), "Confirm", Position);
        var definition = new SequenceDefinition([Click(point.Id), Click(point.Id)]);
        var original = SequenceCompiler.Compile(definition, [point]);
        var changed = point with { Name = "Continue", Position = Position with { X = 300 } };
        var next = SequenceCompiler.Compile(definition, [changed]);
        Assert.AreEqual(100, original.Inputs[0].Position!.X); Assert.AreEqual(100, original.Inputs[2].Position!.X);
        Assert.AreEqual(300, next.Inputs[0].Position!.X); Assert.AreEqual(300, next.Inputs[2].Position!.X);
        Assert.AreEqual(point.Id, changed.Id);
    }
    [TestMethod]
    public void MissingAndAmbiguousTargetsAreRejectedEvenInDisabledActions()
    {
        var point = new NamedPoint(Guid.NewGuid(), "Point", Position);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([Click(point.Id)])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([Click(point.Id) with { Enabled = false }])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([Click(point.Id) with { Position = Position }]), [point]));
        Assert.ThrowsExactly<ArgumentException>(() => NamedPoint.Index([point, point with { Id = Guid.NewGuid(), Name = "POINT" }]));
    }
    [TestMethod]
    public void HistoryAndTimingEditsPreserveNamedReferences()
    {
        var point = new NamedPoint(Guid.NewGuid(), "Point", Position);
        var source = new SequenceDefinition([Click(point.Id), SequenceStep.Wait(TimeSpan.FromSeconds(1))]);
        var history = new SequenceHistory(source, [point]);
        history.Commit(SequenceEdits.ChangeIdleGaps(source, _ => TimeSpan.FromMilliseconds(100), [point]));
        history.Undo(); history.Redo(); Assert.AreEqual(point.Id, history.Current.Steps[0].TargetId);
        Assert.HasCount(2, SequenceCompiler.Compile(history.Current, [point]).Inputs);
    }
}
