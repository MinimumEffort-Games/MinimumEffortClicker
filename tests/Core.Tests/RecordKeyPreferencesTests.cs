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
    public void OlderPreferencesGetF6AsThePauseKeyAndAMouseButtonIsRefused()
    {
        var old = """{"SchemaVersion":1,"Appearance":0,"ToggleKey":119,"StopKey":120,"HoldTrigger":false,"Overlay":true}""";
        var restored = JsonSerializer.Deserialize<UserPreferences>(old)!;
        Assert.AreEqual(0x75, restored.PauseKey); Assert.IsTrue(restored.Valid);
        var custom = restored with { PauseKey = 0x13 };
        Assert.AreEqual(0x13, JsonSerializer.Deserialize<UserPreferences>(JsonSerializer.Serialize(custom))!.PauseKey);
        Assert.IsFalse((custom with { PauseKey = 5 }).Valid, "Pause needs a keyboard key.");
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
