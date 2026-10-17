using MinimumEffortClicker.Linux;

namespace Linux.Tests;

[TestClass]
public sealed class PlatformTests
{
    [TestMethod]
    public void CommonLayoutsMatchTheWindowsHandles()
    {
        Assert.AreEqual("04090409", KeyboardLayouts.FromGnome("xkb:us"));
        Assert.AreEqual("08090809", KeyboardLayouts.FromGnome("xkb:gb"));
        Assert.AreEqual("04070407", KeyboardLayouts.FromGnome("xkb:de"));
        Assert.AreEqual("linux:xkb:us+colemak", KeyboardLayouts.FromGnome("xkb:us+colemak"));
        Assert.IsNull(KeyboardLayouts.FromGnome(""));
    }

    [TestMethod]
    public void TheDecksSilentBuiltInDevicesDoNotCountAsInputAccess()
    {
        // Valve's controller (vendor 28DE) and the EC's AT keyboard (BUS_I8042) carry nothing while Steam runs.
        Assert.IsTrue(EvdevMonitor.DeckBuiltIn(0x03, 0x28DE));
        Assert.IsTrue(EvdevMonitor.DeckBuiltIn(0x11, 0x0001));
        Assert.IsFalse(EvdevMonitor.DeckBuiltIn(0x03, 0x046D)); // A USB mouse.
        Assert.IsFalse(EvdevMonitor.DeckBuiltIn(0x05, 0x05AC)); // A Bluetooth keyboard.
    }

    [TestMethod]
    public void MainMonitorComesFirstThenLeftToRight()
    {
        // The helper lists GNOME's primary monitor first; it is not at the desktop origin here.
        var monitors = DesktopMonitor.Order([new(1920, 0, 2560, 1440, 1), new(4480, 200, 1920, 1080, 1), new(0, 300, 1920, 1080, 1)]);
        CollectionAssert.AreEqual(new[] { "Monitor 1 (main) · 2560 × 1440", "Monitor 2 (left) · 1920 × 1080", "Monitor 3 (right) · 1920 × 1080" },
            monitors.Select(m => m.ToString()).ToArray());
        Assert.IsTrue(monitors[1].Contains(0, 300) && !monitors[1].Contains(1920, 300));
        Assert.AreEqual(0, DesktopMonitor.Order([]).Count);
    }

    [TestMethod]
    public void RgbaRowsBecomePackedBgra()
    {
        // Two pixels per row with four padding bytes per row: red, green / blue, white.
        byte[] rows = [255, 0, 0, 255, 0, 255, 0, 255, 9, 9, 9, 9, 0, 0, 255, 255, 255, 255, 255, 128, 9, 9, 9, 9];
        var pixels = RegionCapture.ToBgra(new HelperImage(2, 2, 12, 4, rows), 2, 2);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 128 }, pixels);
    }

    [TestMethod]
    public void RgbWithoutAlphaIsOpaque()
    {
        var pixels = RegionCapture.ToBgra(new HelperImage(1, 1, 4, 3, [10, 20, 30, 0]), 1, 1);
        CollectionAssert.AreEqual(new byte[] { 30, 20, 10, 255 }, pixels);
    }

    [TestMethod]
    public void DoubleScaleCapturesAreSampledBackToLogicalPixels()
    {
        // A 2×2 capture of a 1×1 logical region on a scale-2 monitor.
        byte[] rows = [1, 2, 3, 255, 9, 9, 9, 255, 9, 9, 9, 255, 9, 9, 9, 255];
        CollectionAssert.AreEqual(new byte[] { 3, 2, 1, 255 }, RegionCapture.ToBgra(new HelperImage(2, 2, 8, 4, rows), 1, 1));
    }

    [TestMethod]
    public void TruncatedCapturesAreRejected()
        => Assert.ThrowsExactly<InvalidOperationException>(() => RegionCapture.ToBgra(new HelperImage(2, 2, 8, 4, new byte[10]), 2, 2));
}
