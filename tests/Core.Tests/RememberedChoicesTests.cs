using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class RememberedChoicesTests
{
    [TestMethod]
    public void OlderPreferencesOpenWithTheOldCheckboxDefaultsAndNoActiveToggles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mec-remembered-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "preferences.json"),
                """{"SchemaVersion":1,"Appearance":0,"ToggleKey":119,"StopKey":120,"HoldTrigger":false,"Overlay":true}""");
            var restored = new Preferences(directory).Load();
            Assert.IsNull(restored.Actions); Assert.IsNull(restored.ActiveToggles);
            // What the page shows for a file without the new fields: the defaults it had before they were remembered.
            Assert.AreEqual(new ActionsChoices(Loop: false, MinimizeOnStart: true, RecordPauses: true, MinimizeOnRecord: true, SimulateMovement: false), restored.Actions ?? new());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void CheckboxesAndActiveTogglesRoundTrip()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mec-remembered-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Preferences(directory);
            Guid first = Guid.NewGuid(), second = Guid.NewGuid();
            var choices = new ActionsChoices(Loop: true, MinimizeOnStart: false, RecordPauses: false, MinimizeOnRecord: false, SimulateMovement: true);
            store.Save(UserPreferences.Default with { Actions = choices, ActiveToggles = [first, second] });
            var loaded = store.Load();
            Assert.AreEqual(choices, loaded.Actions);
            CollectionAssert.AreEqual(new[] { first, second }, loaded.ActiveToggles);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void ActiveTogglesAreBounded()
    {
        Assert.IsFalse((UserPreferences.Default with { ActiveToggles = [Guid.Empty] }).Valid);
        var tooMany = Enumerable.Range(0, UserPreferences.MaxActiveToggles + 1).Select(_ => Guid.NewGuid()).ToArray();
        Assert.IsFalse((UserPreferences.Default with { ActiveToggles = tooMany }).Valid);
        var most = tooMany[..UserPreferences.MaxActiveToggles];
        Assert.IsTrue((UserPreferences.Default with { ActiveToggles = most }).Valid);
        // The most that can be remembered still fits under the settings file's size limit.
        string directory = Path.Combine(Path.GetTempPath(), "mec-remembered-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Preferences(directory);
            store.Save(UserPreferences.Default with { Actions = new(), ActiveToggles = most });
            Assert.AreEqual(UserPreferences.MaxActiveToggles, store.Load().ActiveToggles?.Length);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
