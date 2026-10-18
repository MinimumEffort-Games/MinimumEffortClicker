using System.Text.Json;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class RecordKeyPreferencesTests
{
    [TestMethod]
    public void OlderPreferencesDefaultToF10AndSelectedRecordKeyRoundTrips()
    {
        var old = """{"SchemaVersion":1,"Appearance":0,"ToggleKey":119,"StopKey":120,"HoldTrigger":false,"Overlay":true}""";
        var restored = JsonSerializer.Deserialize<UserPreferences>(old)!;
        Assert.AreEqual(0x79, restored.RecordKey); Assert.IsTrue(restored.Valid);
        var custom = restored with { RecordKey = 0x76 };
        Assert.AreEqual(0x76, JsonSerializer.Deserialize<UserPreferences>(JsonSerializer.Serialize(custom))!.RecordKey);
        Assert.IsFalse((custom with { RecordKey = 0 }).Valid);
    }
    [TestMethod]
    public void OlderPreferencesGetAFreePauseKeyAndAMouseButtonIsRefused()
    {
        var old = """{"SchemaVersion":1,"Appearance":0,"ToggleKey":119,"StopKey":120,"HoldTrigger":false,"Overlay":true}""";
        var restored = JsonSerializer.Deserialize<UserPreferences>(old)!;
        Assert.AreEqual(0x76, restored.PauseKey); Assert.IsTrue(restored.Valid);
        // That file's screenshot key was the old default F7: Pause takes the first free key instead.
        Assert.AreEqual(0x75, MinimumEffortClicker.Core.ShortcutKeys.FreePauseKey(restored.PauseKey, [0x77, 0x78, 0x79, 0x76]));
        Assert.AreEqual(0x76, MinimumEffortClicker.Core.ShortcutKeys.FreePauseKey(restored.PauseKey, [0x75, 0x77, 0x78, 0x79]));
        var custom = restored with { PauseKey = 0x13 };
        Assert.AreEqual(0x13, JsonSerializer.Deserialize<UserPreferences>(JsonSerializer.Serialize(custom))!.PauseKey);
        Assert.IsFalse((custom with { PauseKey = 5 }).Valid, "Pause needs a keyboard key.");
    }
    [TestMethod]
    public void ResetToTheNewDefaultsIsOneValidSetEvenWhenTheOldKeysSwapRoles()
    {
        // Old layout: Start F8, Stop F9, Record F10, Pause F6, Screenshot F7. The new defaults move F6 to Stop All and F7 to
        // Pause; checked as a set they are all different, so Reset keybinds is not refused (found on KDE, 2026-10-18).
        Assert.IsTrue(MinimumEffortClicker.Core.ShortcutKeys.AllDifferent(0x77, 0x75, 0x79, 0x76, 0x78));
        Assert.IsTrue(MinimumEffortClicker.Core.ShortcutKeys.AllDifferent(0x77, 0x78, 0x79, 0x75, 0x76), "The old layout is valid too.");
        Assert.IsFalse(MinimumEffortClicker.Core.ShortcutKeys.AllDifferent(0x77, 0x75, 0x79, 0x75, 0x78), "Stop All and Pause on one key.");
        Assert.IsTrue(MinimumEffortClicker.Core.ShortcutKeys.AllDifferent(0x77, 0x75, 0x79, 0x76, null), "Screenshot key off.");
    }
    [TestMethod]
    public void CapturedKeyboardAndMouseRecordShortcutsPersist()
    {
        foreach (int key in new[] { 0x41, 0x7B, 5 })
        {
            var saved = UserPreferences.Default with { RecordKey = key };
            var restored = JsonSerializer.Deserialize<UserPreferences>(JsonSerializer.Serialize(saved))!;
            Assert.IsTrue(restored.Valid); Assert.AreEqual(key, restored.RecordKey);
        }
    }
}
