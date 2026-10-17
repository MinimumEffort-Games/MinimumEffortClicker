using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class ControllerControlsTests
{
    [TestMethod]
    public void DefaultsAreAStartBStopYRecordXScreenshot()
    {
        var controls = ControllerControls.Default;
        Assert.AreEqual(ControllerCommand.StartStop, controls.CommandFor(ControllerButton.A));
        Assert.AreEqual(ControllerCommand.StopAll, controls.CommandFor(ControllerButton.B));
        Assert.AreEqual(ControllerCommand.Record, controls.CommandFor(ControllerButton.Y));
        Assert.AreEqual(ControllerCommand.Screenshot, controls.CommandFor(ControllerButton.X));
        Assert.IsNull(controls.CommandFor(ControllerButton.L4));
        Assert.IsTrue(controls.Valid);
    }

    [TestMethod]
    public void ChoosingAButtonInUseSwapsSoNoControlIsLost()
    {
        // Stop All takes A: Start/Stop, which had A, gets B.
        var controls = ControllerControls.Default.With(ControllerCommand.StopAll, ControllerButton.A);
        Assert.AreEqual(ControllerButton.A, controls.StopAll);
        Assert.AreEqual(ControllerButton.B, controls.StartStop);
        Assert.IsTrue(controls.Valid);
        // A free button just moves.
        controls = controls.With(ControllerCommand.Record, ControllerButton.R4);
        Assert.AreEqual(ControllerButton.R4, controls.Record);
        Assert.IsNull(controls.CommandFor(ControllerButton.Y));
    }

    [TestMethod]
    public void AControlCanBeLeftWithoutAButton()
    {
        var controls = ControllerControls.Default.With(ControllerCommand.Screenshot, null);
        Assert.IsNull(controls.Screenshot);
        Assert.IsNull(controls.CommandFor(ControllerButton.X));
        Assert.IsTrue(controls.Valid);
        Assert.IsFalse((controls with { Record = ControllerButton.A }).Valid, "one button for two controls");
        Assert.IsFalse((controls with { Record = (ControllerButton)99 }).Valid, "unknown button");
    }

    [TestMethod]
    public void EveryButtonHasALabel()
    {
        foreach (var button in Enum.GetValues<ControllerButton>())
            Assert.IsFalse(string.IsNullOrWhiteSpace(ControllerButtons.Label(button)), button.ToString());
        Assert.AreEqual("LT", ControllerButtons.Label(ControllerButton.LeftTrigger));
        Assert.AreEqual("R4", ControllerButtons.Label(ControllerButton.R4));
    }

    [TestMethod]
    public void ControllerButtonsAreSavedAndOldSettingsGetTheDefaults()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mec-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Preferences(directory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "preferences.json"),
                """{"SchemaVersion":1,"Appearance":0,"ToggleKey":119,"StopKey":120,"HoldTrigger":false,"Overlay":true}""");
            Assert.IsNull(store.Load().Controller, "older files have no controller buttons; the app uses the defaults");
            var chosen = ControllerControls.Default.With(ControllerCommand.StartStop, ControllerButton.L5).With(ControllerCommand.Screenshot, null);
            store.Save(UserPreferences.Default with { Controller = chosen });
            Assert.AreEqual(chosen, store.Load().Controller);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
