using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class RehearsalTests
{
    private static readonly Guid TargetId = Guid.Parse("64175c14-80ba-4b22-ae07-9cb582b1f32e");
    private static readonly ClickDefinition Click = new(PointerButton.Left, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20));

    [TestMethod]
    public void RehearsalUsesFakeTimeAndDescribesOrderedTransitionsWithoutDispatch()
    {
        var clock = new FakeClock();
        clock.Advance(TimeSpan.FromSeconds(42));
        var targets = new FakeForegroundProvider { Current = new(TargetId, true) };
        var preview = new ClickRehearsal(clock, targets).Describe(Click, TargetId);
        Assert.AreEqual(TimeSpan.FromSeconds(42), preview.CreatedAt);
        Assert.AreEqual(TargetId, preview.TargetInstanceId);
        CollectionAssert.AreEqual(new[]
        {
            new PreviewStep(TimeSpan.Zero, new(PointerButton.Left, InputEdge.Down)),
            new PreviewStep(TimeSpan.FromMilliseconds(20), new(PointerButton.Left, InputEdge.Up))
        }, preview.Steps.ToArray());
        // This type deliberately has no IInputSink dependency, so rehearsal cannot dispatch.
        Assert.IsFalse(typeof(ClickRehearsal).GetConstructors().SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(IInputSink)));
    }

    [TestMethod]
    public void RehearsalSnapshotDoesNotChangeWithTargetOrClock()
    {
        var clock = new FakeClock();
        var targets = new FakeForegroundProvider { Current = new(TargetId, true) };
        var rehearsal = new ClickRehearsal(clock, targets);
        var preview = rehearsal.Describe(Click, TargetId);
        clock.Advance(TimeSpan.FromMinutes(1));
        targets.Current = null;
        Assert.AreEqual(TimeSpan.Zero, preview.CreatedAt);
        Assert.HasCount(2, preview.Steps);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<PreviewStep>)preview.Steps).Clear());
        Assert.ThrowsExactly<InvalidOperationException>(() => rehearsal.Describe(Click, TargetId));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("unavailable")]
    [DataRow("different")]
    [DataRow("empty")]
    public void MissingOrChangedTargetIsRejected(string scenario)
    {
        var targets = new FakeForegroundProvider
        {
            Current = scenario switch
            {
                "missing" => null,
                "unavailable" => new(TargetId, false),
                "different" => new(Guid.Parse("49e8140a-014f-4f43-95cf-dd443204f64f"), true),
                _ => new(TargetId, true)
            }
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => new ClickRehearsal(new FakeClock(), targets)
            .Describe(Click, scenario == "empty" ? Guid.Empty : TargetId));
    }

    [TestMethod]
    public void InvalidDefinitionIsRejectedBeforeRehearsal() =>
        Assert.ThrowsExactly<ArgumentException>(() => new ClickRehearsal(new FakeClock(), new FakeForegroundProvider())
            .Describe(Click with { PressDuration = TimeSpan.Zero }, TargetId));

    [TestMethod]
    public void FakeClockCannotMoveBackward() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FakeClock().Advance(TimeSpan.FromTicks(-1)));

    [TestMethod]
    public void FakeSinkPreservesAcceptedOrderAndSimulatesFailureInMemory()
    {
        var sink = new FakeInputSink(2);
        var down = new InputTransition(PointerButton.Right, InputEdge.Down);
        var up = new InputTransition(PointerButton.Right, InputEdge.Up);
        Assert.IsTrue(sink.TrySend(down));
        Assert.IsTrue(sink.TrySend(up));
        Assert.IsFalse(sink.TrySend(down));
        CollectionAssert.AreEqual(new[] { down, up }, sink.Accepted.ToArray());
    }

    [TestMethod]
    public void DomainAndEngineHaveNoDesktopDependencies()
    {
        foreach (var assembly in new[] { typeof(ClickDefinition).Assembly, typeof(ClickRehearsal).Assembly })
        {
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
            Assert.IsFalse(references.Any(n => n.Contains("Windows", StringComparison.Ordinal)
                || n.StartsWith("Presentation", StringComparison.Ordinal)
                || n.StartsWith("MinimumEffortClicker.App", StringComparison.Ordinal)));
            Assert.IsFalse(assembly.GetTypes().SelectMany(t => t.GetMethods())
                .Any(m => (m.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0));
        }
    }
}
