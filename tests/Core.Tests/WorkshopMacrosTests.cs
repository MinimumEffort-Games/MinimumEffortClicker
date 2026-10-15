using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class WorkshopMacrosTests
{
    private static TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);
    private static string NewFolder() { var path = Path.Combine(Path.GetTempPath(), "MEC-workshop-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static MacroDefinition Macro(string name, params SequenceStep[] steps) => new(Guid.NewGuid(), name, null, Sequence: new(steps));
    private static SequenceStep Click() => QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(80), Ms(20), new(1, 1, 800, 600, 96));
    private static string Write(string folder, string file, MacroDefinition macro)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, file);
        HelperProfiles.Export(path, new(ProfileDefinition.CurrentVersion, Guid.NewGuid(), macro.Name, [macro]));
        return path;
    }

    private static (int, int, int, int, int) Counts(WorkshopSyncResult r) => (r.Added, r.Updated, r.Removed, r.Skipped, r.Refused.Count);

    [TestMethod]
    public void SubscribedMacrosAreCopiedUpdatedAndRemovedWithoutTouchingTheUsersFiles()
    {
        string root = NewFolder();
        try
        {
            string macros = Path.Combine(root, "Macros"), steam = Path.Combine(root, "steam");
            Write(Path.Combine(steam, "11"), "macro.mecprofile", Macro("Next level", Click()));
            Write(Path.Combine(steam, "22"), "macro.mecprofile", Macro("Farm", Click()));
            Directory.CreateDirectory(Path.Combine(steam, "33")); // downloaded, but no macro in it
            string own = Write(WorkshopMacros.Folder(macros), "My own - notes.mecprofile", Macro("Mine", Click()));
            WorkshopInstalledItem Item(ulong id) => new(id, Path.Combine(steam, id.ToString()));

            var ledger = new WorkshopLedger(root);
            var first = WorkshopMacros.Sync([Item(11), Item(22), Item(33)], macros, ledger);
            Assert.AreEqual((2, 0, 0, 1, 0), Counts(first));
            Assert.IsTrue(File.Exists(Path.Combine(WorkshopMacros.Folder(macros), "Next level - 11.mecprofile")));
            Assert.AreEqual((0, 0, 0, 1, 0), Counts(WorkshopMacros.Sync([Item(11), Item(22), Item(33)], macros, ledger)), "Nothing changed, nothing copied.");

            // The author renames and updates item 11; the user unsubscribes from 22.
            Write(Path.Combine(steam, "11"), "macro.mecprofile", Macro("Next level v2", Click(), Click()));
            var second = WorkshopMacros.Sync([Item(11)], macros, ledger);
            Assert.AreEqual((0, 1, 1, 0, 0), Counts(second));
            var left = Directory.GetFiles(WorkshopMacros.Folder(macros)).Select(Path.GetFileName).Order().ToArray();
            CollectionAssert.AreEqual(new[] { "My own - notes.mecprofile", "Next level v2 - 11.mecprofile" }, left);
            Assert.IsTrue(File.Exists(own));
            Assert.IsTrue(WorkshopMacros.IsWorkshopCopy(macros, Path.Combine(WorkshopMacros.Folder(macros), "Next level v2 - 11.mecprofile")));
            Assert.IsFalse(WorkshopMacros.IsWorkshopCopy(macros, Path.Combine(macros, "Mine.mecprofile")));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void AnUploadHoldsOnlyTheMacroAndAnUnreadableFileIsNeverShared()
    {
        string root = NewFolder();
        try
        {
            string file = Write(root, "Farm.mecprofile", Macro("Farm", Click()));
            string upload = WorkshopMacros.PrepareUpload(file, Path.Combine(root, "work"));
            CollectionAssert.AreEqual(new[] { WorkshopMacros.UploadFileName }, Directory.GetFiles(upload).Select(Path.GetFileName).ToArray());
            string broken = Path.Combine(root, "broken.mecprofile"); File.WriteAllText(broken, "{ not a macro");
            Assert.Throws<System.Text.Json.JsonException>(() => WorkshopMacros.PrepareUpload(broken, Path.Combine(root, "work")));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void SharesRememberTheirItemAndOlderOrForeignFilesAreSafe()
    {
        string root = NewFolder();
        try
        {
            var shares = new WorkshopLedger(root);
            var macro = Guid.NewGuid();
            Assert.IsNull(shares.Find(macro));
            var share = new WorkshopShare(3_141_592_653UL, "Farm", "Loops the farm.", WorkshopVisibility.FriendsOnly);
            shares.Set(macro, share);
            Assert.AreEqual(share, new WorkshopLedger(root).Find(macro));
            shares.Set(macro, null);
            Assert.IsNull(shares.Find(macro));
            // A file from a newer app version is not misread, and a broken file reads as empty.
            File.WriteAllText(Path.Combine(root, "workshop.json"), """{"Version":9,"Items":[{"MacroId":"5f1c7c43-08d1-4ad6-9d43-0d0f2b1c2a10","Share":{"ItemId":5,"Title":"A","Description":"","Visibility":0}}]}""");
            Assert.IsNull(shares.Find(Guid.Parse("5f1c7c43-08d1-4ad6-9d43-0d0f2b1c2a10"))); Assert.AreEqual(0, shares.OriginSteps().Count);
            File.WriteAllText(Path.Combine(root, "workshop.json"), "nonsense");
            Assert.IsNull(shares.Find(Guid.Parse("5f1c7c43-08d1-4ad6-9d43-0d0f2b1c2a10"))); Assert.AreEqual(0, shares.OriginSteps().Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void AHandEditedMacroWithTheWindowsKeyIsRefused()
    {
        // Shared macros come from strangers. The app cannot build a Windows-key action, and a file edited to hold one
        // must not open either, or a Workshop macro could open the Run box and type into it.
        string root = NewFolder();
        try
        {
            string file = Write(root, "Keys.mecprofile", Macro("Keys", QuickActions.Keys([SequenceKeys.Supported["R"]], Ms(50), Ms(50), hold: false)));
            string json = File.ReadAllText(file);
            string edited = System.Text.RegularExpressions.Regex.Replace(json, $@"(""code""\s*:\s*){SequenceKeys.Supported["R"]}\b", "${1}347", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Assert.AreNotEqual(json, edited, "The key code was found in the file.");
            File.WriteAllText(file, edited);
            Assert.ThrowsExactly<ArgumentException>(() => HelperProfiles.Open(file));
        }
        finally { Directory.Delete(root, true); }
    }
}
