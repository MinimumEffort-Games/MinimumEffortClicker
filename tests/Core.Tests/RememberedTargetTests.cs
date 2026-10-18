using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class RememberedTargetTests
{
    [TestMethod]
    public void TheSameWindowIsPickedAndAGuessIsNot()
    {
        var game = new RememberedTarget("Game", "Game - Level 3");
        Assert.AreEqual(1, game.Pick([("Notepad", "Untitled"), ("game", "Game - Level 3"), ("Game", "Launcher")]));
        Assert.AreEqual(1, game.Pick([("Notepad", "Untitled"), ("Game", "Game - Level 4")]), "the program's only window, its title changed");
        Assert.AreEqual(-1, game.Pick([("Game", "Launcher"), ("Game", "Game - Level 4")]), "two windows of the program, neither with the title");
        Assert.AreEqual(-1, game.Pick([("Notepad", "Game - Level 3")]), "the title alone is not the program");
        Assert.IsFalse(new RememberedTarget("", "x").Valid);
        Assert.IsFalse(new RememberedTarget("Game", new string('x', 513)).Valid);
    }

    [TestMethod]
    public void TheLastTargetIsSavedAndADamagedOneIsForgottenAlone()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mec-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Preferences(directory);
            store.Save(UserPreferences.Default with { Appearance = 2, LastTarget = new("Game", "Game - Level 3") });
            Assert.AreEqual(new RememberedTarget("Game", "Game - Level 3"), store.Load().LastTarget);
            File.WriteAllText(Path.Combine(directory, "preferences.json"),
                """{"SchemaVersion":1,"Appearance":2,"ToggleKey":119,"StopKey":117,"HoldTrigger":false,"Overlay":true,"LastTarget":{"Title":"no program"}}""");
            var loaded = store.Load();
            Assert.IsNull(loaded.LastTarget);
            Assert.AreEqual(2, loaded.Appearance);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
