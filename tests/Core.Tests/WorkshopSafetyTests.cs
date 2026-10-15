using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class WorkshopSafetyTests
{
    private static TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);
    private static SequenceStep Press(params string[] keys) => QuickActions.Keys(keys.Select(k => SequenceKeys.Supported[k]).ToArray(), Ms(50), Ms(50), hold: false);
    private static SequenceStep Click() => QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(80), Ms(20), new(1, 1, 800, 600, 96));
    private static IReadOnlyList<string> Shortcuts(params SequenceStep[] steps) => SystemShortcuts.Find(SequenceCompiler.Compile(new SequenceDefinition(steps)));

    private string root = "";
    [TestInitialize] public void Folder() { root = Path.Combine(Path.GetTempPath(), "MEC-safety-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
    [TestCleanup] public void Clean() => Directory.Delete(root, true);
    private string Write(string folder, MacroDefinition macro)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "macro.mecprofile");
        HelperProfiles.Export(path, new(ProfileDefinition.CurrentVersion, Guid.NewGuid(), macro.Name, [macro]));
        return path;
    }

    [TestMethod]
    public void ShortcutsThatReachTheSystemAreFoundAndOrdinaryOnesAreNot()
    {
        CollectionAssert.AreEqual(new[] { "Ctrl+Esc (Start menu)" }, Shortcuts(Press("Left Ctrl", "Escape")).ToArray());
        CollectionAssert.AreEqual(new[] { "Ctrl+Shift+Esc (Task Manager)" }, Shortcuts(Press("Right Ctrl", "Left Shift", "Escape")).ToArray());
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+T (Linux terminal)" }, Shortcuts(Press("Left Ctrl", "Left Alt", "T")).ToArray());
        CollectionAssert.AreEqual(new[] { "Alt+F4 (closes windows; on the desktop, shuts down)", "Alt+F2 (Linux run command)" },
            Shortcuts(Press("Left Alt", "F4"), Click(), Press("Right Alt", "F2")).ToArray());
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+F-key (Linux console switch)" }, Shortcuts(Press("Left Ctrl", "Left Alt", "F3")).ToArray());
        // Copy, a plain Escape and keys pressed one after another are fine.
        Assert.AreEqual(0, Shortcuts(Press("Left Ctrl", "C"), Press("Escape"), Press("Left Alt"), Press("F4"), Click()).Count);
    }

    [TestMethod]
    public void AWorkshopMacroWithASystemShortcutIsRefusedAndItsCopyRemoved()
    {
        string macros = Path.Combine(root, "Macros"), steam = Path.Combine(root, "steam");
        var ledger = new WorkshopLedger(root);
        var safe = new MacroDefinition(Guid.NewGuid(), "Farm", null, Sequence: new([Click(), Press("E")]));
        Write(Path.Combine(steam, "1"), safe);
        Write(Path.Combine(steam, "2"), new MacroDefinition(Guid.NewGuid(), "Helper", null, Sequence: new([Click()])));
        WorkshopMacros.Sync([new(1, Path.Combine(steam, "1")), new(2, Path.Combine(steam, "2"))], macros, ledger);
        Assert.AreEqual(2, Directory.GetFiles(WorkshopMacros.Folder(macros)).Length);

        // The author of item 2 updates it to open a terminal and type into it.
        Write(Path.Combine(steam, "2"), new MacroDefinition(Guid.NewGuid(), "Helper", null, Sequence: new([Press("Left Ctrl", "Left Alt", "T"), Press("R"), Press("M")])));
        var result = WorkshopMacros.Sync([new(1, Path.Combine(steam, "1")), new(2, Path.Combine(steam, "2"))], macros, ledger);
        Assert.AreEqual(1, result.Refused.Count);
        StringAssert.Contains(result.Refused[0], "Ctrl+Alt+T");
        Assert.AreEqual(1, result.Removed, "The earlier, harmless copy of the item is gone too.");
        CollectionAssert.AreEqual(new[] { "Farm - 1.mecprofile" }, Directory.GetFiles(WorkshopMacros.Folder(macros)).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public void WorkshopActionsStayMarkedAndOnlyTheirKeysNeedAWindowTarget()
    {
        string macros = Path.Combine(root, "Macros"), steam = Path.Combine(root, "steam");
        var ledger = new WorkshopLedger(root);
        var shared = new MacroDefinition(Guid.NewGuid(), "Farm", null, Sequence: new([Click(), Press("E")]));
        Write(Path.Combine(steam, "1"), shared);
        WorkshopMacros.Sync([new(1, Path.Combine(steam, "1"))], macros, ledger);
        var origins = new WorkshopLedger(root).OriginSteps();
        Assert.IsTrue(shared.Sequence!.Steps.All(s => origins.Contains(s.Id)), "The marks survive in workshop.json, also for copies saved under another name.");

        var ownKeys = Press("E");
        Assert.IsTrue(WorkshopMacros.HasWorkshopKeys(new([shared.Sequence.Steps[1]]), origins), "A Workshop key press.");
        Assert.IsFalse(WorkshopMacros.HasWorkshopKeys(new([shared.Sequence.Steps[0], ownKeys]), origins), "A Workshop click and the user's own keys.");
        // Marks are only ever added.
        new WorkshopLedger(root).AddOriginSteps([ownKeys.Id]);
        Assert.IsTrue(new WorkshopLedger(root).OriginSteps().IsSupersetOf(origins));
    }
}
