using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class ShortcutKeysTests
{
    [TestMethod]
    public void CommonKeyboardAndMouseKeysHaveStableNamesAndReservedValuesAreRejected()
    {
        foreach (int key in new[] { 1, 2, 4, 5, 6, 0x41, 0x30, 0x20, 0xC0, 0x7B, 0x87, 0x25 }) Assert.IsTrue(ShortcutKeys.Supported(key));
        foreach (int key in new[] { 0, 3, 7, 0x5B, 0x88, 0xFF, -1 }) Assert.IsFalse(ShortcutKeys.Supported(key));
        Assert.AreEqual("A", ShortcutKeys.Label(0x41)); Assert.AreEqual("F24", ShortcutKeys.Label(0x87));
        Assert.AreEqual("Mouse side 1", ShortcutKeys.Label(5)); Assert.AreEqual(0x77, UserPreferences.Default.ToggleKey);
        foreach (var mouse in Enum.GetValues<PointerButton>()) Assert.AreEqual(InputControl.Mouse(mouse), ShortcutKeys.MouseInput(ShortcutKeys.MouseKey(mouse)));
    }
    [TestMethod]
    public void NonFunctionShortcutsRoundTripWithoutEnablingOrStartingAnything()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MEC-shortcuts-" + Guid.NewGuid());
        try
        {
            var preferences = new Preferences(folder); var chosen = UserPreferences.Default with { ToggleKey = 0x41, StopKey = 5 };
            preferences.Save(chosen); Assert.AreEqual(chosen, preferences.Load());
            var bindings = new LocalBindings(folder); var values = new[] { new MacroBinding(Guid.NewGuid(), 0x4E, false), new MacroBinding(Guid.NewGuid(), 2, false) };
            bindings.Save(values); CollectionAssert.AreEqual(values, bindings.Load().ToArray());
            MacroBindings.Validate(values, 0x41, 5);
            Assert.ThrowsExactly<ArgumentException>(() => MacroBindings.Validate(values, 0x4E, 5));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
