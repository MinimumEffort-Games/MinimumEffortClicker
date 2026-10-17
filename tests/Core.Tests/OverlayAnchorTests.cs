using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class OverlayAnchorTests
{
    [TestMethod]
    public void AnchorsPlaceTheOverlayInsideTheTargetAndBack()
    {
        // A 1000 x 600 target at 100, 50 and a 200 x 40 overlay: 12 px in from each edge, centred at 0.5.
        Assert.AreEqual((112.0, 62.0), OverlayAnchor.Place(100, 50, 1000, 600, 200, 40, 0, 0));
        Assert.AreEqual((888.0, 598.0), OverlayAnchor.Place(100, 50, 1000, 600, 200, 40, 1, 1));
        Assert.AreEqual((500.0, 330.0), OverlayAnchor.Place(100, 50, 1000, 600, 200, 40, 0.5, 0.5));
        // A drop beside the target keeps that place relative to it, also outside 0 to 1.
        var dropped = OverlayAnchor.From(100, 50, 1000, 600, 200, 40, 1200, 20, 1, 0);
        var back = OverlayAnchor.Place(100, 50, 1000, 600, 200, 40, dropped.X, dropped.Y);
        Assert.AreEqual(1200, back.X, 1e-9); Assert.AreEqual(20, back.Y, 1e-9);
        Assert.IsTrue(dropped.X > 1 && dropped.Y < 0);
        // No room across: that axis keeps its anchor.
        Assert.AreEqual(0.25, OverlayAnchor.From(0, 0, 200, 600, 200, 40, 30, 100, 0.25, 0).X);
    }

    [TestMethod]
    public void ClicksOnThePictureLandOnTheNearestEdgeAndSnap()
    {
        Assert.AreEqual((1.0, 0.0), OverlayAnchor.OnEdge(0.97, 0.03));
        Assert.AreEqual((0.5, 1.0), OverlayAnchor.OnEdge(0.53, 0.9));
        Assert.AreEqual((0.0, 0.3), OverlayAnchor.OnEdge(0.1, 0.3));
        Assert.AreEqual((1.0, 0.7), OverlayAnchor.OnEdge(1.4, 0.7));
    }

    [TestMethod]
    public void ArrowKeysMoveTheAnchorTheWayTheyPoint()
    {
        Assert.AreEqual((0.5, 1.0), OverlayAnchor.Step(1, 1, -1, 0));
        Assert.AreEqual((0.0, 1.0), OverlayAnchor.Step(0.5, 1, -1, 0));
        Assert.AreEqual((0.0, 0.5), OverlayAnchor.Step(0, 1, 0, -1));
        Assert.AreEqual((0.0, 0.0), OverlayAnchor.Step(0, 0, 0, -1)); // Already at the top: stays.
        // Across the middle: from the top middle, Down goes to the bottom middle.
        Assert.AreEqual((0.5, 1.0), OverlayAnchor.Step(0.5, 0, 0, 1));
        Assert.AreEqual((1.0, 0.5), OverlayAnchor.Step(0, 0.5, 1, 0));
        // From part-way along an edge, the nearest point is the start.
        Assert.AreEqual((1.0, 0.0), OverlayAnchor.Step(0.6, 0, 1, 0));
    }

    [TestMethod]
    public void OverlayAnchorIsSavedAndOlderPreferencesGetTheTopRight()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mec-anchor-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Preferences(directory);
            var value = UserPreferences.Default with { OverlayOptions = new(AnchorX: 0.5, AnchorY: 1, Unlocked: true) };
            store.Save(value); Assert.AreEqual(value, store.Load());
            Assert.ThrowsExactly<ArgumentException>(() => store.Save(value with { OverlayOptions = new(AnchorX: double.NaN) }));
            Assert.ThrowsExactly<ArgumentException>(() => store.Save(value with { OverlayOptions = new(AnchorY: 40) }));
            // Preferences saved before the anchor existed load with the overlay top-right and locked, as before.
            File.WriteAllText(Path.Combine(directory, "preferences.json"), "{\"SchemaVersion\":1,\"Appearance\":0,\"ToggleKey\":119,\"StopKey\":120,\"HoldTrigger\":false,\"Overlay\":true,\"OverlayOptions\":{\"Expanded\":false,\"KeepIdle\":true,\"Opacity\":0.8,\"Scale\":1}}");
            var loaded = store.Load().OverlayOptions!;
            Assert.AreEqual((1.0, 0.0, false), (loaded.AnchorX, loaded.AnchorY, loaded.Unlocked));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
