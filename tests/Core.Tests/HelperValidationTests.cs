using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class HelperValidationTests
{
    [TestMethod]
    public void CursorFreeClicksRequireAnExplicitCalibratedMousePoint()
    {
        var click = new HelperPlan(InputControl.Mouse(PointerButton.Left), HelperMode.Repeat, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(20), 5)
        { LeaveCursorFree = true };
        Assert.IsNotEmpty(click.Validate());
        click = click with { Position = new(20, 30, 800, 600, 96) };
        Assert.IsEmpty(click.Validate());
        Assert.IsNotEmpty((click with { Control = InputControl.Key(0x11) }).Validate());
        Assert.IsNotEmpty((click with { Position = new(20, 30, 800, 600, 96, CoordinateSpace.Desktop) }).Validate());
        Assert.IsTrue((UserPreferences.Default with { ToggleKey = 0xC0 }).Valid);
    }
    [TestMethod]
    public void HoldCountCannotSilentlyBecomeAnInfiniteHold()
    {
        var plan = new HelperPlan(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 3);
        Assert.IsNotEmpty(plan.Validate());
        Assert.HasCount(0, (plan with { Count = 0 }).Validate());
        Assert.HasCount(0, (plan with { Count = 1, ActiveDuration = TimeSpan.FromSeconds(3) }).Validate());
    }
    [TestMethod]
    public void UnknownInputsAndAmbiguousRepeatLimitsAreInvalid()
    {
        var plan = new HelperPlan(InputControl.Key(999), HelperMode.Repeat, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 3);
        Assert.IsNotEmpty(plan.Validate());
        Assert.IsNotEmpty((plan with { Control = InputControl.Key(0x11), ActiveDuration = TimeSpan.FromSeconds(1) }).Validate());
    }
    [TestMethod]
    public void OverlayPreferencesAreBoundedAndOlderPreferencesLoadLockedDefaults()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MEC-overlay-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var store = new Preferences(directory);
            File.WriteAllText(Path.Combine(directory, "preferences.json"), "{\"SchemaVersion\":1,\"Appearance\":0,\"ToggleKey\":119,\"StopKey\":120,\"HoldTrigger\":false,\"Overlay\":true}");
            Assert.AreEqual(UserPreferences.Default, store.Load());
            var value = UserPreferences.Default with { OverlayOptions = new(true, true, true, 0.75, 1.5, -1920, 80), MainTopmost = true };
            store.Save(value); Assert.AreEqual(value, store.Load());
            foreach (var invalid in new[] { new OverlayOptions(Opacity: double.NaN), new OverlayOptions(Scale: 2), new OverlayOptions(X: 2), new OverlayOptions(Y: int.MaxValue) })
                Assert.ThrowsExactly<ArgumentException>(() => store.Save(value with { OverlayOptions = invalid }));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [TestMethod]
    public void PreferencesPersistConfigurationWithoutPlaybackOrTargetState()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MEC-preferences-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Preferences(directory); var value = new UserPreferences(1, 2, 0x75, 0x76, true, false, ClicksPerSecond: 8, KeepAwake: true);
            store.Save(value); Assert.AreEqual(value, store.Load());
            Assert.ThrowsExactly<ArgumentException>(() => store.Save(value with { ToggleKey = value.StopKey }));
            Assert.ThrowsExactly<ArgumentException>(() => store.Save(value with { ClicksPerSecond = 0 }));
            Assert.ThrowsExactly<ArgumentException>(() => store.Save(value with { ClicksPerSecond = 101 }));
            File.WriteAllText(Path.Combine(directory, "preferences.json"), "broken");
            Assert.AreEqual(UserPreferences.Default, store.Load());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
