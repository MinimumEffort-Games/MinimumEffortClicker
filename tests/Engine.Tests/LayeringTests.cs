using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class LayeringTests
{
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

    /// <summary>Core and Engine stay free of WPF and native code so every default test runs without desktop input.</summary>
    [TestMethod]
    public void DomainAndEngineHaveNoDesktopDependencies()
    {
        foreach (var assembly in new[] { typeof(HelperPlan).Assembly, typeof(ClickExecutor).Assembly })
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
