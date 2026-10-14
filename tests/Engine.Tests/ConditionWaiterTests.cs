using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class ConditionWaiterTests
{
    private sealed class Wait(FakeClock clock) : IWaitStrategy
    {
        public void WaitUntil(TimeSpan at, CancellationToken stop) { stop.ThrowIfCancellationRequested(); if (at > clock.Elapsed) clock.Advance(at - clock.Elapsed); }
    }
    private sealed class Probe(Func<ConditionObservation> sample) : IConditionProbe
    { public ConditionObservation Observe(CancellationToken stop) => sample(); }
    private static ConditionPolicy Policy(int stable = 1) => new(TimeSpan.FromMilliseconds(100), stable);

    [TestMethod]
    public void ColourAbsenceScansAtChosenIntervalAndWaitsAfterSlowCapture()
    {
        var clock = new FakeClock(); int index = 0; var at = new List<TimeSpan>();
        var policy = new ConditionPolicy(TimeSpan.FromMilliseconds(750));
        var result = new ConditionWaiter(clock, new Wait(clock)).Wait(policy, new Probe(() =>
        {
            at.Add(clock.Elapsed); clock.Advance(TimeSpan.FromMilliseconds(200));
            byte value = ++index < 3 ? (byte)20 : (byte)40;
            bool met = RegionComparison.MatchesColorCondition(new(1, 1, new byte[] { value, value, value, 255 }), ConditionKind.ColorDisappears, 0x141414, 0);
            return new(met ? ConditionObservationState.Met : ConditionObservationState.NotMet, 1, index);
        }), default);
        Assert.AreEqual(ConditionOutcome.Matched, result.Outcome); Assert.AreEqual(3, result.Samples);
        CollectionAssert.AreEqual(new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(950), TimeSpan.FromMilliseconds(1900) }, at);
    }

    [TestMethod]
    public void KeepsScanningAndReportsFalseThenTrue()
    {
        var clock = new FakeClock(); int index = 0; var observed = new List<ConditionObservationState>();
        var result = new ConditionWaiter(clock, new Wait(clock)).Wait(Policy(2),
            new Probe(() => new(index++ < 8 ? ConditionObservationState.NotMet : ConditionObservationState.Met, 1, index)),
            default, observed: o => observed.Add(o.State));
        Assert.AreEqual(ConditionOutcome.Matched, result.Outcome); Assert.AreEqual(10, result.Samples);
        Assert.AreEqual(TimeSpan.FromMilliseconds(900), clock.Elapsed);
        Assert.AreEqual(8, observed.Count(s => s == ConditionObservationState.NotMet));
    }
    [TestMethod]
    public void AuthoringTestCancelsWithoutMatchAndRejectsAmbiguousResults()
    {
        var clock = new FakeClock(); int index = 0; using var stop = new CancellationTokenSource();
        Assert.ThrowsExactly<OperationCanceledException>(() => new ConditionWaiter(clock, new Wait(clock)).Wait(Policy(),
            new Probe(() => { if (++index == 12) stop.Cancel(); return new(ConditionObservationState.NotMet, 1, index); }), stop.Token));
        Assert.AreEqual(12, index);
        var result = new ConditionWaiter(clock, new Wait(clock)).Wait(Policy(), new Probe(() => new(ConditionObservationState.Ambiguous, 1, 1)), default);
        Assert.AreEqual(ConditionOutcome.Ambiguous, result.Outcome);
    }

    [TestMethod]
    public void StabilityNeedsFreshSamplesAndResetsAcrossContextChanges()
    {
        var clock = new FakeClock(); int index = 0;
        ConditionObservation[] samples = [new(ConditionObservationState.Met, 1, 1), new(ConditionObservationState.Met, 1, 1),
            new(ConditionObservationState.Met, 2, 1), new(ConditionObservationState.Met, 2, 2)];
        var result = new ConditionWaiter(clock, new Wait(clock)).Wait(Policy(2), new Probe(() => samples[index++]), default);
        Assert.AreEqual(ConditionOutcome.Matched, result.Outcome); Assert.AreEqual(4, result.Samples);
        Assert.AreEqual(TimeSpan.FromMilliseconds(300), clock.Elapsed);
    }
    [TestMethod]
    public void WaitHasNoTimeLimit()
    {
        // Roy's case: the button to wait for appears when a level ends, 5–10 minutes later. Twenty minutes still matches.
        var clock = new FakeClock(); long sample = 0;
        var result = new ConditionWaiter(clock, new Wait(clock)).Wait(Policy(), new Probe(() =>
            new(clock.Elapsed < TimeSpan.FromMinutes(20) ? ConditionObservationState.NotMet : ConditionObservationState.Met, 1, sample++)), default);
        Assert.AreEqual(ConditionOutcome.Matched, result.Outcome);
        Assert.AreEqual(TimeSpan.FromMinutes(20), clock.Elapsed);
        Assert.AreEqual(12001, result.Samples);
    }
    [TestMethod]
    public void AmbiguousAndUnavailableObservationsFailWithoutGuessing()
    {
        foreach (var state in new[] { ConditionObservationState.Ambiguous, ConditionObservationState.Unavailable })
        {
            var clock = new FakeClock();
            var result = new ConditionWaiter(clock, new Wait(clock)).Wait(Policy(), new Probe(() => new(state, 1, 1)), default);
            Assert.AreEqual(1, result.Samples); Assert.AreNotEqual(ConditionOutcome.Matched, result.Outcome);
        }
    }
    [TestMethod]
    public void CancellationPrecedesMatch()
    {
        var clock = new FakeClock();
        using var stop = new CancellationTokenSource();
        var cancelling = new Probe(() => { stop.Cancel(); return new(ConditionObservationState.Met, 1, 1); });
        Assert.ThrowsExactly<OperationCanceledException>(() => new ConditionWaiter(clock, new Wait(clock)).Wait(Policy(), cancelling, stop.Token));
    }
}
