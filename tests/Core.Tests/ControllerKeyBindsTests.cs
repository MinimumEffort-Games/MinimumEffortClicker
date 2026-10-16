using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class ControllerKeyBindsTests
{
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "mec-ctk-" + Guid.NewGuid().ToString("N"));

    [TestMethod]
    public void BindsRoundTripAndAMissingFileIsAnEmptyList()
    {
        string directory = NewDirectory();
        try
        {
            var store = new ControllerKeyBinds(directory);
            Assert.AreEqual(0, store.Load()!.Count);
            ControllerKeyBind[] binds =
            [
                new(Guid.NewGuid(), "Autorun", ControllerButton.L4, InputControl.Key(SequenceKeys.Supported["W"])),
                new(Guid.NewGuid(), "Turbo click", ControllerButton.RightTrigger, InputControl.Mouse(PointerButton.Left), Active: false)
            ];
            store.Save(binds);
            CollectionAssert.AreEqual(binds, store.Load()!.ToArray());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void AnUnreadableFileIsLeftAloneNotTreatedAsEmpty()
    {
        string directory = NewDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var store = new ControllerKeyBinds(directory);
            File.WriteAllText(store.FilePath, "{ not json");
            Assert.IsNull(store.Load(), "null tells the app not to save over the user's file");
            Assert.AreEqual("{ not json", File.ReadAllText(store.FilePath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void EachButtonServesOneBindAndOutputsMustBeKeysOrMouseButtons()
    {
        var a = new ControllerKeyBind(Guid.NewGuid(), "One", ControllerButton.R4, InputControl.Key(SequenceKeys.Supported["W"]));
        var b = a with { Id = Guid.NewGuid(), Name = "Two" };
        Assert.IsFalse(ControllerKeyBinds.Valid([a, b]), "two binds on R4");
        Assert.IsTrue(ControllerKeyBinds.Valid([a, b with { Button = ControllerButton.R5 }]));
        Assert.IsFalse((a with { Output = new InputControl(InputKind.Scroll, 120) }).Valid);
        Assert.IsFalse((a with { Name = " " }).Valid);
        Assert.IsFalse((a with { Button = (ControllerButton)99 }).Valid);
    }
}
