using System.Text.Json;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class InputAccessNoticePreferencesTests
{
    [TestMethod]
    public void OlderSettingsShowTheNoticeAndUntickingItIsRemembered()
    {
        var old = """{"SchemaVersion":1,"Appearance":0,"ToggleKey":119,"StopKey":120,"HoldTrigger":false,"Overlay":true}""";
        var restored = JsonSerializer.Deserialize<UserPreferences>(old)!;
        Assert.IsTrue(restored.InputAccessNotice);
        var off = restored with { InputAccessNotice = false };
        Assert.IsFalse(JsonSerializer.Deserialize<UserPreferences>(JsonSerializer.Serialize(off))!.InputAccessNotice);
    }
}
