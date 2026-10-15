using System.Text.Json;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class EmergencyStopPreferencesTests
{
    [TestMethod]
    public void OlderPreferencesGetTheBothButtonsStopAndTheChoiceRoundTrips()
    {
        // Settings saved before the choice existed open with the new default: left + right mouse button stop the Action List.
        var old = """{"SchemaVersion":1,"Appearance":0,"ToggleKey":119,"StopKey":120,"HoldTrigger":false,"Overlay":true,"RecordKey":121}""";
        var restored = JsonSerializer.Deserialize<UserPreferences>(old)!;
        Assert.AreEqual(EmergencyStopMode.BothMouseButtons, restored.EmergencyStop); Assert.IsTrue(restored.Valid);
        var anyInput = restored with { EmergencyStop = EmergencyStopMode.AnyInput };
        Assert.AreEqual(EmergencyStopMode.AnyInput, JsonSerializer.Deserialize<UserPreferences>(JsonSerializer.Serialize(anyInput))!.EmergencyStop);
        Assert.IsFalse((restored with { EmergencyStop = (EmergencyStopMode)7 }).Valid);
    }
}
