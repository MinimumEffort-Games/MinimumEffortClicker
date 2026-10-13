using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class ScreenshotTests
{
    [TestMethod]
    public void ScreenshotNamesUseTargetCaptureDateTimeAndOptionalDailyFolders()
    {
        WithFolder(folder =>
        {
            var at = new DateTime(2026, 10, 10, 14, 35, 22, 123);
            string daily = ScreenshotFiles.Save(folder, s => s.WriteByte(1), default, "Cookie Clicker", at, true);
            Assert.AreEqual(Path.Combine(folder, "2026-10-10", "Cookie Clicker 2026-10-10 14-35-22-123.png"), daily);
            string flat = ScreenshotFiles.Save(folder, s => s.WriteByte(2), default, null, at);
            Assert.AreEqual(Path.Combine(folder, "Desktop 2026-10-10 14-35-22-123.png"), flat);
            string tomorrow = ScreenshotFiles.Save(folder, s => s.WriteByte(3), default, "Cookie Clicker", at.AddDays(1), true);
            Assert.AreEqual("2026-10-11", Path.GetFileName(Path.GetDirectoryName(tomorrow)));
        });
    }

    [TestMethod]
    public void ScreenshotNamesSanitizeWindowsCharactersDevicesAndLongTitles()
    {
        WithFolder(folder =>
        {
            foreach (string title in new[] { "../bad\\name:<>\"|?*\n", "CON.txt", "COM1", "LPT¹.txt", new string('a', 99) + "😀extra", " ... " })
            {
                string path = ScreenshotFiles.Save(folder, s => s.WriteByte(1), default, title);
                Assert.AreEqual(folder, Path.GetDirectoryName(path));
                Assert.IsTrue(Path.GetFileName(path).Length < 150);
                Assert.IsFalse(Path.GetFileName(path).Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)));
                Assert.IsTrue(File.Exists(path));
            }
        });
    }

    [TestMethod]
    public void SameTimestampNeverOverwritesOrReencodesAndParallelSavesRemainUnique()
    {
        WithFolder(folder =>
        {
            var at = new DateTime(2026, 10, 10, 12, 0, 0);
            int encoded = 0;
            string first = ScreenshotFiles.Save(folder, s => { encoded++; s.WriteByte(42); }, default, "Test", at);
            string second = ScreenshotFiles.Save(folder, s => { encoded++; s.WriteByte(99); }, default, "Test", at);
            Assert.AreEqual("Test 2026-10-10 12-00-00-000 (2).png", Path.GetFileName(second));
            Assert.AreEqual(2, encoded); CollectionAssert.AreEqual(new byte[] { 42 }, File.ReadAllBytes(first));
            Parallel.For(0, 8, _ => ScreenshotFiles.Save(folder, s => s.WriteByte(7), default, "Test", at));
            Assert.AreEqual(10, Directory.GetFiles(folder, "*.png").Length);
            Assert.IsEmpty(Directory.GetFiles(folder, "*.tmp"));
        });
    }

    [TestMethod]
    public void DailyFoldersDefaultOnForOldSettingsAndOffChoicePersists()
    {
        WithFolder(folder =>
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<ScreenshotSettings>("""{"Mode":0,"CopyToClipboard":false}""")!;
            Assert.IsTrue(settings.DailyFolders);
            var prefs = new Preferences(folder);
            prefs.Save(UserPreferences.Default with { Screenshots = settings with { DailyFolders = false } });
            Assert.IsFalse(prefs.Load().Screenshots!.DailyFolders);
        });
    }

    [TestMethod]
    public void ScreenshotActionEnforcesPauseAndRejectsInputOrRandomTiming()
    {
        var step = SequenceStep.Screenshot();
        Assert.AreEqual(TimeSpan.FromSeconds(1), step.Duration);
        foreach (var invalid in new[] { step with { Duration = TimeSpan.FromMilliseconds(999) }, step with { Control = InputControl.Key(0x11) },
            step with { LeaveCursorFree = true }, step with { Timing = new(MaximumPause: TimeSpan.FromSeconds(2)) } })
            Assert.Throws<ArgumentException>(() => SequenceCompiler.Compile(new([invalid])));
        Assert.IsEmpty(SequenceCompiler.Compile(new([step with { Enabled = false }])).Screenshots);
    }

    [TestMethod]
    public void ScreenshotOrderSurvivesRandomTimingKeyBindingAndClickAdaptation()
    {
        var pause = SequenceStep.Wait(TimeSpan.FromMilliseconds(20)) with { Timing = new(MaximumPause: TimeSpan.FromMilliseconds(60)) };
        var plan = SequenceCompiler.Compile(new([pause, SequenceStep.Screenshot(), SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20))]));
        plan = plan.AdaptClickPoints(800, 600, 96, true).SampleTiming(() => 0.5);
        Assert.AreEqual(TimeSpan.FromMilliseconds(40), plan.Screenshots.Single().Offset);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1040), plan.Inputs[0].Offset);
        Assert.AreEqual(1, plan.Screenshots.Single().StepIndex);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1060), plan.Duration);
    }

    [TestMethod]
    public void RepeatedScreenshotsKeepTheirOrderAndHistory()
    {
        var source = new SequenceDefinition([SequenceStep.Screenshot(), SequenceStep.Screenshot()]);
        var plan = SequenceCompiler.Compile(source);
        Assert.HasCount(2, plan.Screenshots);
        Assert.AreEqual(TimeSpan.FromSeconds(1), plan.Screenshots[1].Offset);
        var history = new SequenceHistory(source);
        history.Commit(source with { Steps = [SequenceStep.Screenshot(TimeSpan.FromSeconds(3))] });
        history.Undo(); Assert.HasCount(2, SequenceCompiler.Compile(history.Current).Screenshots);
        history.Redo(); Assert.AreEqual(TimeSpan.FromSeconds(3), history.Current.Steps[0].Duration);
    }

    [TestMethod]
    public void ScreenshotMacroRoundTripsAndVersionTwelveStillLoads()
    {
        WithFolder(folder =>
        {
            string path = Path.Combine(folder, "capture.mecprofile");
            var step = SequenceStep.Screenshot(TimeSpan.FromSeconds(2));
            var profile = new ProfileDefinition(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Capture", [new(Guid.NewGuid(), "Capture", null, Sequence: new([step], Repetition: new(3)))]);
            HelperProfiles.Export(path, profile);
            var loaded = HelperProfiles.Open(path);
            Assert.AreEqual(step, loaded.Macros[0].Sequence!.Steps[0]);
            Assert.AreEqual(3, loaded.Macros[0].Sequence!.Repetition!.Count);
            Assert.HasCount(1, SequenceCompiler.Compile(HelperProfiles.Import(path).Macros[0].Sequence!).Screenshots);
            HelperProfiles.Export(path, profile with { Macros = [profile.Macros[0] with { Sequence = new([SequenceStep.Wait(TimeSpan.FromSeconds(1))]) }] });
            File.WriteAllText(path, File.ReadAllText(path).Replace($"\"schemaVersion\": {ProfileDefinition.CurrentVersion}", "\"schemaVersion\": 12"));
            Assert.AreEqual(ProfileDefinition.CurrentVersion, HelperProfiles.Open(path).SchemaVersion);
        });
    }

    [TestMethod]
    public void SelectionUsesLocalPixelsWithNegativeDesktopOriginAndEitherDragDirection()
    {
        var desktop = new ScreenshotBounds(-1920, -100, 3840, 1180);
        Assert.AreEqual(new ScreenshotBounds(20, 50, 400, 300), desktop.Selection(-1900, -50, -1500, 250));
        Assert.AreEqual(desktop.Selection(-1900, -50, -1500, 250), desktop.Selection(-1500, 250, -1900, -50));
    }

    [TestMethod]
    public void SelectionClipsExtremeCoordinatesAndRejectsEmptyDrags()
    {
        var desktop = new ScreenshotBounds(-1920, 0, 3840, 1080);
        Assert.AreEqual(new ScreenshotBounds(0, 0, 3840, 1080), desktop.Selection(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue));
        Assert.IsNull(desktop.Selection(-1920, 10, -1920, 800));
        Assert.IsNull(desktop.Selection(2000, 10, 3000, 50));
    }

    [TestMethod]
    public void CaptureAllocationIsBoundedBeforeReadingDesktopPixels()
    {
        new ScreenshotBounds(-3840, 0, 7680, 4320).Validate();
        Assert.Throws<ArgumentException>(() => new ScreenshotBounds(0, 0, 8000, 8000).Validate());
        Assert.Throws<ArgumentException>(() => new ScreenshotBounds(int.MaxValue, 0, 1, 1).Validate());
        Assert.Throws<ArgumentException>(() => new ScreenshotBounds(0, 0, 0, 1080).Validate());
    }

    [TestMethod]
    public void ScreenshotPreferencesMigrateAndRoundTripWithoutEnablingAnUnchosenKey()
    {
        WithFolder(folder =>
        {
            var store = new Preferences(folder);
            File.WriteAllText(Path.Combine(folder, "preferences.json"), """{"SchemaVersion":1,"Appearance":2,"ToggleKey":119,"StopKey":120,"HoldTrigger":false,"Overlay":false}""");
            var old = store.Load();
            Assert.IsNull(old.Screenshots);
            var defaults = new ScreenshotSettings();
            Assert.IsTrue(defaults.Valid);
            Assert.IsFalse(defaults.CopyToClipboard);
            Assert.IsFalse(defaults.HotkeyEnabled);
            Assert.IsFalse((defaults with { HotkeyEnabled = true }).Valid);
            var updated = old with { Screenshots = new(0, true, folder, 0x75, true) };
            store.Save(updated);
            Assert.AreEqual(updated, store.Load());
            Assert.AreEqual(2, store.Load().Appearance);
        });
    }

    [TestMethod]
    public void ScreenshotSettingsRejectInvalidModesPathsAndReservedInputs()
    {
        foreach (int key in new[] { 0, 1, 2, 27, 0xA0, 0xA1, 0xA2, 0xA5, 999 })
            Assert.IsFalse(ScreenshotSettings.SupportedKey(key));
        Assert.IsTrue(ScreenshotSettings.SupportedKey(0x75));
        Assert.IsTrue(ScreenshotSettings.SupportedKey(0x50));
        Assert.IsFalse(new ScreenshotSettings(Mode: 2).Valid);
        Assert.IsFalse(new ScreenshotSettings(Folder: "relative").Valid);
    }

    [TestMethod]
    public void SaveUsesUniquePngNamesAndPreservesExistingFiles()
    {
        WithFolder(folder =>
        {
            string owner = Path.Combine(folder, "keep.png"); File.WriteAllText(owner, "owner");
            string one = ScreenshotFiles.Save(folder, stream => stream.Write([1, 2, 3]), default);
            string two = ScreenshotFiles.Save(folder, stream => stream.Write([4, 5, 6]), default);
            Assert.AreNotEqual(one, two);
            Assert.AreEqual(".png", Path.GetExtension(one));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(one));
            Assert.AreEqual("owner", File.ReadAllText(owner));
            Assert.AreEqual(3, Directory.GetFiles(folder).Length);
        });
    }

    [TestMethod]
    public void FailedOrCancelledEncodingLeavesNoPartialImage()
    {
        WithFolder(folder =>
        {
            string owner = Path.Combine(folder, "keep.png.tmp"); File.WriteAllText(owner, "owner");
            Assert.Throws<IOException>(() => ScreenshotFiles.Save(folder, stream => { stream.WriteByte(1); throw new IOException("encoder failed"); }, default));
            using var cancel = new CancellationTokenSource();
            Assert.Throws<OperationCanceledException>(() => ScreenshotFiles.Save(folder, stream => { stream.WriteByte(2); cancel.Cancel(); }, cancel.Token));
            Assert.AreEqual(1, Directory.GetFiles(folder).Length);
            Assert.AreEqual("owner", File.ReadAllText(owner));
        });
    }

    [TestMethod]
    public void AlreadyCancelledRequestDoesNotCreateADirectoryOrCallEncoder()
    {
        WithFolder(folder =>
        {
            string absent = Path.Combine(folder, "absent");
            Assert.Throws<OperationCanceledException>(() => ScreenshotFiles.Save(absent, _ => Assert.Fail("Must not encode"), new(true)));
            Assert.IsFalse(Directory.Exists(absent));
            Assert.Throws<ArgumentException>(() => ScreenshotFiles.Save("relative", _ => Assert.Fail("Must not encode"), default));
        });
    }

    private static void WithFolder(Action<string> test)
    {
        string folder = Path.Combine(Path.GetTempPath(), "MEC-screenshots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { test(folder); }
        finally { Directory.Delete(folder, true); }
    }
}
