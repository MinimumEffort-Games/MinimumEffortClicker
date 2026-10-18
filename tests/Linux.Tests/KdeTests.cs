using MinimumEffortClicker.Linux;
using MinimumEffortClicker.Linux.Kde;

namespace Linux.Tests;

[TestClass]
public sealed class KdeTests
{
    /// <summary>Records what the guard asks KWin's fake input to do.</summary>
    private sealed class RecordingDevice : IInputDevice
    {
        public List<string> Sent { get; } = [];
        public void MoveTo(double x, double y) => Sent.Add($"move {x} {y}");
        public void MoveBy(double dx, double dy) => Sent.Add($"look {dx} {dy}");
        public void SetButton(uint code, bool pressed) => Sent.Add($"button {code:x} {(pressed ? "down" : "up")}");
        public void Scroll(uint axis, double value) => Sent.Add($"scroll {axis} {value}");
        public void SetKey(int evdev, bool pressed) => Sent.Add($"key {evdev} {(pressed ? "down" : "up")}");
        public void Roundtrip() { }
    }

    [TestMethod]
    public void TheDesktopChoosesTheHelper()
    {
        string? before = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", "KDE");
            Assert.IsTrue(LinuxSetup.IsKde);
            Assert.AreEqual("KDE helper", LinuxSetup.HelperName);
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", "ubuntu:GNOME");
            Assert.IsFalse(LinuxSetup.IsKde);
            Assert.IsTrue(LinuxSetup.IsGnome);
            Assert.AreEqual("GNOME helper", LinuxSetup.HelperName);
        }
        finally { Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", before); }
    }

    [TestMethod]
    public void NothingIsPressedOrMovedWhileTheScreenIsLocked()
    {
        var device = new RecordingDevice();
        HashSet<uint> buttons = []; HashSet<int> keys = [];
        Assert.AreEqual("locked", KdeInputGuard.Handle(device, "m 10 20", buttons, keys, locked: true));
        Assert.AreEqual("locked", KdeInputGuard.Handle(device, "b 272 1", buttons, keys, locked: true));
        Assert.AreEqual("locked", KdeInputGuard.Handle(device, "k 17 1", buttons, keys, locked: true));
        Assert.AreEqual("locked", KdeInputGuard.Handle(device, "s 0 10", buttons, keys, locked: true));
        Assert.AreEqual(0, device.Sent.Count);
        // Letting go is always allowed, so nothing stays held because the screen locked.
        Assert.AreEqual("ok", KdeInputGuard.Handle(device, "k 17 0", buttons, keys, locked: true));
        Assert.AreEqual("ok", KdeInputGuard.Handle(device, "r", buttons, keys, locked: true));
        CollectionAssert.AreEqual(new[] { "key 17 up" }, device.Sent);
    }

    [TestMethod]
    public void ALookStepIsARelativeMoveAndWaitsWhileTheScreenIsLocked()
    {
        var device = new RecordingDevice();
        HashSet<uint> buttons = []; HashSet<int> keys = [];
        Assert.AreEqual("locked", KdeInputGuard.Handle(device, "l 5 -3", buttons, keys, locked: true));
        Assert.AreEqual(0, device.Sent.Count);
        Assert.AreEqual("ok", KdeInputGuard.Handle(device, "l -12 7", buttons, keys, locked: false));
        CollectionAssert.AreEqual(new[] { "look -12 7" }, device.Sent);
    }

    [TestMethod]
    public void TheGuardLetsGoOfEverythingItStillHolds()
    {
        var device = new RecordingDevice();
        HashSet<uint> buttons = []; HashSet<int> keys = [];
        foreach (var line in new[] { "b 272 1", "k 42 1", "k 17 1", "k 17 0" })
            Assert.AreEqual("ok", KdeInputGuard.Handle(device, line, buttons, keys, locked: false));
        device.Sent.Clear();
        KdeInputGuard.ReleaseAll(device, buttons, keys); // What the guard does when the clicker's pipe closes.
        CollectionAssert.AreEquivalent(new[] { "key 42 up", "button 110 up" }, device.Sent);
        Assert.AreEqual(0, buttons.Count + keys.Count);
    }

    [TestMethod]
    public void ScrollsStartWithAZeroStepAndUnknownLinesAreRefused()
    {
        var device = new RecordingDevice();
        HashSet<uint> buttons = []; HashSet<int> keys = [];
        KdeInputGuard.Handle(device, "s 1 -30", buttons, keys, locked: false);
        CollectionAssert.AreEqual(new[] { "scroll 1 0", "scroll 1 -30" }, device.Sent);
        Assert.ThrowsExactly<FormatException>(() => KdeInputGuard.Handle(device, "x", buttons, keys, locked: false));
    }

    [TestMethod]
    public void HelperButtonsBecomeLinuxButtonCodes()
    {
        Assert.AreEqual(0x110u, KdeHelper.LinuxButton(1));
        Assert.AreEqual(0x112u, KdeHelper.LinuxButton(2));
        Assert.AreEqual(0x111u, KdeHelper.LinuxButton(3));
        Assert.AreEqual(0x113u, KdeHelper.LinuxButton(8));
        Assert.AreEqual(0x114u, KdeHelper.LinuxButton(9));
        Assert.ThrowsExactly<HelperException>(() => KdeHelper.LinuxButton(4));
    }

    [TestMethod]
    public void ShortcutNamesBecomeQtKeys()
    {
        Assert.AreEqual(0x01000037, KdeShortcuts.QtKey("F8"));
        Assert.AreEqual(0x01000030, KdeShortcuts.QtKey("F1"));
        Assert.AreEqual('Q', KdeShortcuts.QtKey("q"));
        Assert.AreEqual('7', KdeShortcuts.QtKey("7"));
        Assert.AreEqual(0x20000000 | '1', KdeShortcuts.QtKey("KP_1"));
        Assert.AreEqual(0x01000016, KdeShortcuts.QtKey("Page_Up"));
        Assert.IsNull(KdeShortcuts.QtKey("Hyper_L"));
        // Every key the shortcut picker can offer has a KDE name.
        for (int key = 0; key < 256; key++)
            if (LinuxKeys.Accelerator(key) is { } name) Assert.IsNotNull(KdeShortcuts.QtKey(name), name);
    }

    [TestMethod]
    public void TheLayoutVariantComesFromKxkbrc()
    {
        const string kxkbrc = "[Layout]\nLayoutList=de,us\nUse=true\nVariantList=nodeadkeys,dvorak\n";
        Assert.AreEqual("xkb:us+dvorak", KdeHelper.Layout("us", 1, kxkbrc));
        Assert.AreEqual("xkb:de+nodeadkeys", KdeHelper.Layout("de", 0, kxkbrc));
        Assert.AreEqual("xkb:gb", KdeHelper.Layout("gb", 0, null));
        Assert.AreEqual("04090409", KeyboardLayouts.FromGnome(KdeHelper.Layout("us", 0, "VariantList=\n")));
    }

    [TestMethod]
    public void KWinPixelsBecomeRgbaRows()
    {
        // One row with padding: a blue pixel stored B, G, R, A, then a red one.
        byte[] bgra = [0xCC, 0x66, 0x33, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 9, 9, 9, 9];
        var image = KWinCapture.ToImage(2, 1, 12, 6, bgra);
        CollectionAssert.AreEqual(new byte[] { 0x33, 0x66, 0xCC, 0xFF, 0xFF, 0x00, 0x00, 0xFF }, image.Pixels);
        Assert.AreEqual(4, image.Channels);
        // RGB32 has no alpha: the picture is opaque.
        Assert.AreEqual(255, KWinCapture.ToImage(1, 1, 4, 4, [1, 2, 3, 0]).Pixels[3]);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, KWinCapture.ToImage(1, 1, 4, 17, [1, 2, 3, 4]).Pixels);
        Assert.ThrowsExactly<HelperException>(() => KWinCapture.ToImage(1, 1, 4, 99, [1, 2, 3, 4]));
        Assert.ThrowsExactly<HelperException>(() => KWinCapture.ToImage(2, 2, 8, 6, [1, 2, 3, 4]));
    }

    [TestMethod]
    public void AWindowCaptureIsCroppedToTheRegion()
    {
        // 3 × 2 picture whose pixel values are their own index.
        byte[] pixels = Enumerable.Range(0, 6).SelectMany(i => new[] { (byte)i, (byte)i, (byte)i, (byte)255 }).ToArray();
        var cropped = KWinCapture.Crop(new HelperImage(3, 2, 12, 4, pixels), 1, 1, 2, 1);
        Assert.AreEqual(2, cropped.Width);
        CollectionAssert.AreEqual(new byte[] { 4, 4, 4, 255, 5, 5, 5, 255 }, cropped.Pixels);
        // A region reaching past the edge is cut at the edge.
        Assert.AreEqual(1, KWinCapture.Crop(new HelperImage(3, 2, 12, 4, pixels), 2, 0, 5, 1).Width);
    }

    [TestMethod]
    public void KWinWindowIdsGetStableNumbers()
    {
        var ids = new KdeHelper.WindowIds();
        ulong first = ids.For("{a}"), second = ids.For("{b}");
        Assert.AreNotEqual(first, second);
        Assert.AreEqual(first, ids.For("{a}"));
        Assert.AreEqual(0ul, ids.For(""));
        Assert.AreEqual("{b}", ids.Name(second));
        Assert.IsNull(ids.Name(999));
        Assert.IsNull(ids.Known("{c}"));
        Assert.AreEqual(second, ids.Known("{b}"));
    }

    [TestMethod]
    public void KdeAccessNamesExactlyThisExecutable()
    {
        string entry = LinuxSetup.KdeAccessEntry("/home/me/My Games/clicker/MinimumEffortClicker");
        StringAssert.Contains(entry, "Exec=\"/home/me/My Games/clicker/MinimumEffortClicker\"\n");
        StringAssert.Contains(entry, "X-KDE-DBUS-Restricted-Interfaces=org.kde.KWin.ScreenShot2\n");
        StringAssert.Contains(entry, "X-KDE-Wayland-Interfaces=org_kde_kwin_fake_input\n");
        StringAssert.Contains(entry, "NoDisplay=true\n");
        Assert.AreEqual(LinuxSetup.KdeAccessFile("/a/MinimumEffortClicker"), LinuxSetup.KdeAccessFile("/a/MinimumEffortClicker"));
        Assert.AreNotEqual(LinuxSetup.KdeAccessFile("/a/MinimumEffortClicker"), LinuxSetup.KdeAccessFile("/b/MinimumEffortClicker"));
    }

    [TestMethod]
    public void TheKWinScriptShipsWithTheAppAndOnlyAnswersItsClicker()
    {
        string source = KWinScript.Source;
        StringAssert.Contains(source, "const SERVICE = \"%SERVICE%\";");
        StringAssert.Contains(source, KWinScript.ObjectPath);
        foreach (var operation in new[] { "pointer", "monitors", "windows", "focus", "window", "target", "at", "activate" })
            StringAssert.Contains(source, $"    {operation}: ");
    }
}
