using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class ClickPlanTests
{
    private static ClickDefinition Definition => new(PointerButton.Left, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20));

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(1_000_001)]
    public void RepeatCountMustBeFiniteAndBounded(int count) =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ClickPlan.Compile(Definition, count));

    [TestMethod]
    public void InvalidClickCannotBecomeExecutable() =>
        Assert.ThrowsExactly<ArgumentException>(() => ClickPlan.Compile(Definition with { PressDuration = TimeSpan.Zero }, 1));

    [TestMethod]
    public void CompiledSnapshotIsIndependentOfEditedDefinition()
    {
        var draft = Definition;
        var plan = ClickPlan.Compile(draft, ClickPlan.MaximumCount);
        draft = draft with { Interval = TimeSpan.FromSeconds(2) };
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), plan.Interval);
        Assert.AreEqual(ClickPlan.MaximumCount, plan.Count);
        Assert.AreNotEqual(draft.Interval, plan.Interval);
    }
}
