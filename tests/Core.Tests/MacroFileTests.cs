using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class MacroFileTests
{
    private string directory = null!;
    [TestInitialize] public void Initialize() { directory = Path.Combine(Path.GetTempPath(), "MEC-macrofiles-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
    [TestCleanup] public void Cleanup() => Directory.Delete(directory, true);
    private static ProfileDefinition Profile(string name = "Test") => new(ProfileDefinition.CurrentVersion, Guid.NewGuid(), name,
        [new(Guid.NewGuid(), name, null, Sequence: new([SequenceStep.Wait(TimeSpan.FromSeconds(1))]))]);

    [TestMethod]
    public void FileNamesCannotEscapeFolderOrOverwriteUnrelatedData()
    {
        var first = Profile("../Test:Colour?"); var second = Profile(first.Name);
        string path = MacroFiles.SavePath(directory, first.Macros[0]); HelperProfiles.Export(path, first);
        string other = MacroFiles.SavePath(directory, second.Macros[0]);
        Assert.AreEqual(directory, Path.GetDirectoryName(Path.GetFullPath(path))); Assert.AreNotEqual(path, other);
        Assert.AreEqual(path, MacroFiles.SavePath(directory, first.Macros[0]));
        Assert.AreEqual(first.Id, HelperProfiles.Open(path).Id);
        File.WriteAllText(other, "Owner file that is not a macro");
        Assert.AreNotEqual(other, MacroFiles.SavePath(directory, second.Macros[0]));
        Assert.AreEqual("Owner file that is not a macro", File.ReadAllText(other));
        Assert.AreEqual("Macro CON.mecprofile", Path.GetFileName(MacroFiles.SavePath(directory, Profile("CON").Macros[0])));
    }
    [TestMethod]
    public void ExistingLibraryBecomesFilesWithoutOverwritingEditsOrExportingToggles()
    {
        var profile = Profile();
        var toggle = new MacroDefinition(Guid.NewGuid(), "Hold W", new(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(20), 0, null), IsToggle: true);
        var library = profile with { Macros = [profile.Macros[0], toggle] };
        Assert.AreEqual(1, MacroFiles.EnsureLibraryFiles(directory, [library]));
        string path = Directory.GetFiles(directory).Single();
        var edited = HelperProfiles.Open(path) with { Name = "Edited on disk" }; HelperProfiles.Export(path, edited);
        Assert.AreEqual(0, MacroFiles.EnsureLibraryFiles(directory, [library]));
        Assert.AreEqual("Edited on disk", HelperProfiles.Open(path).Name);
    }
    [TestMethod]
    public void ColourAbsenceAndScanIntervalRoundTripAndOldSchemaTenStillOpens()
    {
        var policy = new ConditionPolicy(TimeSpan.FromMilliseconds(750));
        var condition = new ConditionDefinition(ConditionKind.ColorDisappears, policy, new(new(2, 3, 100, 100, 96), 1, 1), 0x123456);
        var profile = Profile() with { Macros = [new(Guid.NewGuid(), "Wait", null, Sequence: new([SequenceStep.WaitUntil("Background gone", condition)]))] };
        string path = Path.Combine(directory, "wait.mecprofile"); HelperProfiles.Export(path, profile);
        var opened = HelperProfiles.Open(path);
        Assert.AreEqual(condition, opened.Macros[0].Sequence!.Steps[0].Condition); Assert.AreEqual(profile.Id, opened.Id);
        Assert.AreNotEqual(profile.Id, HelperProfiles.Import(path).Id);
        foreach (int ms in new[] { 100, 5000 }) (condition with { Policy = policy with { SampleInterval = TimeSpan.FromMilliseconds(ms) } }).Validate();
        foreach (int ms in new[] { 99, 5001 }) Assert.ThrowsExactly<ArgumentException>(() => (condition with { Policy = policy with { SampleInterval = TimeSpan.FromMilliseconds(ms) } }).Validate());
        HelperProfiles.Export(path, Profile());
        File.WriteAllText(path, File.ReadAllText(path).Replace($"\"schemaVersion\": {ProfileDefinition.CurrentVersion}", "\"schemaVersion\": 10"));
        Assert.AreEqual(ProfileDefinition.CurrentVersion, HelperProfiles.Open(path).SchemaVersion);
    }
    [TestMethod]
    public void SchemaSixteenWaitWithTimeLimitOpensWithoutTheLimit()
    {
        var condition = new ConditionDefinition(ConditionKind.RegionColor, new(TimeSpan.FromMilliseconds(500)), new(new(2, 3, 100, 100, 96), 1, 1), 0x123456);
        var profile = Profile() with { Macros = [new(Guid.NewGuid(), "Wait", null, Sequence: new([SequenceStep.WaitUntil("Next button", condition)]))] };
        string path = Path.Combine(directory, "old-wait.mecprofile"); HelperProfiles.Export(path, profile);
        // Rewrite it the way schema 16 saved a wait: with a 30-second limit that stopped the run.
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["schemaVersion"] = 16;
        var policy = root["macros"]![0]!["sequence"]!["steps"]![0]!["condition"]!["policy"]!.AsObject();
        policy["timeout"] = "00:00:30"; policy["timeoutAction"] = 0; policy["retries"] = 0;
        File.WriteAllText(path, root.ToJsonString());
        var opened = HelperProfiles.Open(path, out int dropped);
        Assert.AreEqual(ProfileDefinition.CurrentVersion, opened.SchemaVersion); Assert.AreEqual(0, dropped);
        Assert.AreEqual(condition, opened.Macros[0].Sequence!.Steps[0].Condition);
        // A current file never carries the old fields.
        root["schemaVersion"] = ProfileDefinition.CurrentVersion; File.WriteAllText(path, root.ToJsonString());
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => HelperProfiles.Open(path));
    }
    [TestMethod]
    public void InvalidSaveDoesNotDamageExistingMacroFile()
    {
        var profile = Profile(); string path = Path.Combine(directory, "safe.mecprofile"); HelperProfiles.Export(path, profile);
        var before = File.ReadAllBytes(path);
        Assert.ThrowsExactly<ArgumentException>(() => HelperProfiles.Export(path, profile with { Name = "" }));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        Assert.HasCount(1, Directory.GetFiles(directory));
    }
}
