using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class BoundedProbeTests
{
    private sealed class Sink : IInputSink
    {
        public List<InputTransition> Sent { get; } = [];
        public ManualResetEventSlim Down { get; } = new(false);
        public bool RejectDown { get; set; }
        public bool RejectUp { get; set; }
        public bool TrySend(InputTransition value)
        {
            if (value.Edge == InputEdge.Down && RejectDown || value.Edge == InputEdge.Up && RejectUp) return false;
            Sent.Add(value);
            if (value.Edge == InputEdge.Down) Down.Set();
            return true;
        }
    }
    [TestMethod]
    public void StopBeforeStartPreventsOutput()
    {
        var sink = new Sink(); using var probe = new BoundedClickProbe(sink);
        probe.Stop(); Assert.IsFalse(probe.Press(TimeSpan.FromMilliseconds(1))); Assert.HasCount(0, sink.Sent);
    }
    [TestMethod]
    public async Task StopInterruptsLongHoldAndCleansUpExactlyOnce()
    {
        var sink = new Sink(); using var probe = new BoundedClickProbe(sink);
        var press = Task.Run(() => probe.Press(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(sink.Down.Wait(TimeSpan.FromSeconds(2)));
        probe.Stop(); probe.Stop();
        Assert.IsFalse(await press.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.HasCount(2, sink.Sent); Assert.AreEqual(InputEdge.Up, sink.Sent[1].Edge);
        Assert.IsFalse(probe.HasOwnedInput);
    }
    [TestMethod]
    public void FailedDownDoesNotCreateOwnershipOrCleanup()
    {
        var sink = new Sink { RejectDown = true }; using var probe = new BoundedClickProbe(sink);
        Assert.IsFalse(probe.Press(TimeSpan.FromMilliseconds(1))); probe.Stop(); Assert.HasCount(0, sink.Sent);
    }
    [TestMethod]
    public void FailedUpRemainsOwnedAndCanBeRetriedByStop()
    {
        var sink = new Sink { RejectUp = true }; using var probe = new BoundedClickProbe(sink);
        Assert.IsFalse(probe.Press(TimeSpan.FromMilliseconds(1))); Assert.IsTrue(probe.HasOwnedInput);
        sink.RejectUp = false; probe.Stop(); probe.Stop();
        Assert.IsFalse(probe.HasOwnedInput); Assert.HasCount(2, sink.Sent);
    }
    [TestMethod]
    public void ProbeRejectsUnboundedOrInvalidHold()
    {
        using var probe = new BoundedClickProbe(new Sink());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => probe.Press(TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => probe.Press(TimeSpan.FromSeconds(11)));
    }
}
