using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class RegionComparisonTests
{
    [TestMethod]
    public void ColourDisappearsOnlyWhenNoPixelStillMatchesIncludingTolerance()
    {
        Assert.IsFalse(RegionComparison.MatchesColorCondition(Row(10, 20, 30), ConditionKind.ColorDisappears, 0x141414, 0));
        Assert.IsTrue(RegionComparison.MatchesColorCondition(Row(10, 21, 30), ConditionKind.ColorDisappears, 0x141414, 0));
        Assert.IsFalse(RegionComparison.MatchesColorCondition(Row(10, 21, 30), ConditionKind.ColorDisappears, 0x141414, 1));
        Assert.IsTrue(RegionComparison.MatchesColorCondition(Row(22), ConditionKind.ColorDisappears, 0x141414, 1));
        Assert.IsTrue(RegionComparison.MatchesColorCondition(Row(20), ConditionKind.PixelColor, 0x141414, 0));
        Assert.ThrowsExactly<ArgumentException>(() => RegionComparison.MatchesColorCondition(Row(20), ConditionKind.ImageAppears, 0x141414, 0));
    }
    [TestMethod]
    public void ColourRegionFindsAnyPixelWithinTolerance()
    {
        Assert.IsTrue(RegionComparison.ContainsColor(Row(10, 20, 30), 0x141414, 0));
        Assert.IsFalse(RegionComparison.ContainsColor(Row(10, 20, 30), 0x151515, 0));
        Assert.IsTrue(RegionComparison.ContainsColor(Row(10, 20, 30), 0x151515, 1));
        var region = new CaptureRegion(new(0, 0, 100, 100, 96), 10, 10);
        new ConditionDefinition(ConditionKind.RegionColor, new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30)), region, 0x141414).Validate();
        Assert.ThrowsExactly<ArgumentException>(() => new ConditionDefinition(ConditionKind.PixelColor, new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30)), region).Validate());
    }
    private static RasterSample Row(params byte[] values) => new(values.Length, 1, values.SelectMany(v => new byte[] { v, v, v, 255 }).ToArray());
    [TestMethod]
    public void MatchingRejectsAmbiguityInsteadOfChoosingFirstLocation()
    {
        Assert.AreEqual(VisualMatch.Met, RegionComparison.Find(Row(10, 20, 30, 40), Row(20, 30), 0));
        Assert.AreEqual(VisualMatch.NotMet, RegionComparison.Find(Row(10, 20, 30, 40), Row(20, 31), 0));
        Assert.AreEqual(VisualMatch.Met, RegionComparison.Find(Row(10, 20, 30, 40), Row(20, 31), 1));
        Assert.AreEqual(VisualMatch.Ambiguous, RegionComparison.Find(Row(10, 20, 10, 20), Row(10, 20), 0));
    }
    [TestMethod]
    public void ChangedRegionHonorsPerChannelToleranceAndPixelFraction()
    {
        Assert.IsFalse(RegionComparison.Changed(Row(10, 10, 10, 10), Row(12, 12, 12, 12), 2, 0.25));
        Assert.IsTrue(RegionComparison.Changed(Row(10, 10, 10, 10), Row(13, 10, 10, 10), 2, 0.25));
        Assert.IsFalse(RegionComparison.Changed(Row(10, 10, 10, 10), Row(13, 10, 10, 10), 2, 0.5));
        Assert.ThrowsExactly<ArgumentException>(() => RegionComparison.Changed(Row(1), Row(1, 1), 0, 0.5));
        Assert.ThrowsExactly<ArgumentException>(() => RegionComparison.Changed(Row(1), Row(1), 0, double.NaN));
    }
    [TestMethod]
    public void SnapshotsAndWorkBudgetsAreBounded()
    {
        byte[] source = [10, 10, 10, 255]; var sample = new RasterSample(1, 1, source); source[0] = 99;
        Assert.AreEqual((byte)10, sample.Pixels[0]);
        Assert.ThrowsExactly<ArgumentException>(() => new RasterSample(257, 1, []));
        Assert.ThrowsExactly<ArgumentException>(() => RegionComparison.Find(new(256, 256, new byte[256 * 256 * 4]), new(128, 128, new byte[128 * 128 * 4]), 0));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => RegionComparison.Find(Row(1), Row(1), 0, stop.Token));
    }
}
