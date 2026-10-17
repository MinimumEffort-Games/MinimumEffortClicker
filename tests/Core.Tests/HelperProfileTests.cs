using System.Text.Json;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class HelperProfileTests
{
    private string directory = null!;
    private static ProfileDefinition Profile(string name = "Test") => new(ProfileDefinition.CurrentVersion, Guid.NewGuid(), name,
        [new(Guid.NewGuid(), "Tap W", new(InputControl.Key(0x11), HelperMode.Repeat, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 3))]);
    [TestInitialize] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "MEC-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
    [TestCleanup] public void CleanUp() => Directory.Delete(directory, true);

    [TestMethod]
    public void LoopSettingsRoundTripWithoutChangingTheirSemantics()
    {
        var sequence = new SequenceDefinition([SequenceStep.Wait(TimeSpan.FromMilliseconds(50))], Repetition: new(3, TimeSpan.FromSeconds(1)));
        var profile = Profile() with { Macros = [new(Guid.NewGuid(), "Pause", null, Sequence: sequence)] };
        string file = Path.Combine(directory, "loop.mecprofile"); HelperProfiles.Export(file, profile);
        var imported = HelperProfiles.Import(file).Macros[0].Sequence!;
        Assert.AreEqual(sequence.Repetition, imported.Repetition);
        Assert.AreEqual(StepKind.Wait, imported.Steps[0].Kind);
        Assert.AreNotEqual(profile.Id, HelperProfiles.Import(file).Id);
    }

    [TestMethod]
    public void DesktopPointsRoundTripWithoutBecomingWindowRelative()
    {
        var point = new NamedPoint(Guid.NewGuid(), "Fixed", new(-400, 100, 800, 600, 96, CoordinateSpace.Desktop));
        var profile = Profile() with { Points = [point] };
        string file = Path.Combine(directory, "modes.mecprofile"); HelperProfiles.Export(file, profile);
        var imported = HelperProfiles.Import(file);
        Assert.AreEqual(CoordinateSpace.Desktop, imported.Points![0].Position.Space);
    }

    [TestMethod]
    public void VersionFifteenTextSubsequenceAndBranchStepsAreDroppedWithACount()
    {
        // A schema-15 file as the previous release wrote it: a text step, a subsequence call, a branch, and a plain pause.
        var pause = SequenceStep.Wait(TimeSpan.FromMilliseconds(50));
        var profile = Profile() with { Macros = [new(Guid.NewGuid(), "Mixed", null, Sequence: new([pause]))] };
        string file = Path.Combine(directory, "v15.mecprofile"); HelperProfiles.Export(file, profile);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["schemaVersion"] = 15;
        var sequence = json["macros"]![0]!["sequence"]!.AsObject();
        var steps = sequence["steps"]!.AsArray();
        var template = steps[0]!.AsObject();
        var bodyId = Guid.NewGuid();
        foreach (var (kind, extra) in new (int, string)[] { (6, "text"), (4, "subsequenceId"), (5, "flow") })
        {
            var removed = System.Text.Json.Nodes.JsonNode.Parse(template.ToJsonString())!.AsObject();
            removed["id"] = Guid.NewGuid(); removed["kind"] = kind;
            removed["subsequenceId"] = null; removed["flow"] = null; removed["text"] = null;
            removed[extra] = extra == "text" ? "hello" : extra == "subsequenceId" ? bodyId : new System.Text.Json.Nodes.JsonObject { ["kind"] = 0, ["bodyId"] = bodyId, ["elseId"] = null, ["maximumIterations"] = 1 };
            steps.Add(removed);
        }
        template["subsequenceId"] = null; template["flow"] = null; template["text"] = null;
        sequence["subsequences"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["id"] = bodyId, ["name"] = "Body", ["steps"] = new System.Text.Json.Nodes.JsonArray() });
        File.WriteAllText(file, json.ToJsonString());
        var imported = HelperProfiles.Open(file, out int dropped);
        Assert.AreEqual(3, dropped);
        Assert.AreEqual(ProfileDefinition.CurrentVersion, imported.SchemaVersion);
        Assert.HasCount(1, imported.Macros[0].Sequence!.Steps);
        Assert.AreEqual(StepKind.Wait, imported.Macros[0].Sequence!.Steps[0].Kind);
        // The library load reports the same removal once.
        File.WriteAllText(Path.Combine(directory, "profiles.json"), "[" + File.ReadAllText(file) + "]");
        var load = new HelperProfiles(directory).Load();
        Assert.IsNotNull(load.Warning); StringAssert.Contains(load.Warning, "3");
        Assert.HasCount(1, load.Profiles[0].Macros[0].Sequence!.Steps);
    }

    [TestMethod]
    public void VersionSixPointsWithoutSpaceRemainWindowRelative()
    {
        var profile = Profile() with { Points = [new(Guid.NewGuid(), "Point", new(20, 30, 800, 600, 96))] };
        string file = Path.Combine(directory, "v6.mecprofile"); HelperProfiles.Export(file, profile);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["schemaVersion"] = 6; json["points"]![0]!["position"]!.AsObject().Remove("space");
        File.WriteAllText(file, json.ToJsonString());
        var imported = HelperProfiles.Import(file);
        Assert.AreEqual(ProfileDefinition.CurrentVersion, imported.SchemaVersion); Assert.AreEqual(CoordinateSpace.WindowClient, imported.Points![0].Position.Space);
        Assert.AreEqual((120, 230), imported.Points[0].Position.Resolve(100, 200, 800, 600, 96));
    }

    [TestMethod]
    public void NamedPointsRoundTripWithScopedReferences()
    {
        var point = new NamedPoint(Guid.NewGuid(), "Centre", new(400, 300, 800, 600, 96));
        var step = SequenceStep.Input("Click", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(20)) with { TargetId = point.Id };
        var profile = Profile() with { Points = [point], Macros = [new(Guid.NewGuid(), "Named click", null, Sequence: new([step]))] };
        string file = Path.Combine(directory, "named.mecprofile"); HelperProfiles.Export(file, profile);
        var imported = HelperProfiles.Import(file);
        Assert.AreNotEqual(profile.Id, imported.Id); Assert.AreEqual(point, imported.Points![0]);
        Assert.AreEqual(point.Position, SequenceCompiler.Compile(imported.Macros[0].Sequence!, imported.Points).Inputs[0].Position);
    }

    [TestMethod]
    public void VersionThreeMigratesWithoutNamedPoints()
    {
        string file = Path.Combine(directory, "v3.mecprofile"); HelperProfiles.Export(file, Profile());
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["schemaVersion"] = 3; json.Remove("points"); File.WriteAllText(file, json.ToJsonString());
        var imported = HelperProfiles.Import(file);
        Assert.AreEqual(ProfileDefinition.CurrentVersion, imported.SchemaVersion); Assert.IsNull(imported.Points);
    }

    [TestMethod]
    public void ExplicitLibraryDeletionRemovesRecoveryDataAndPreservesUnrelatedFiles()
    {
        var store = new HelperProfiles(directory); store.Save([Profile()]); store.Save([Profile()]);
        string corrupt = Path.Combine(directory, "profiles.json.corrupt-" + Guid.NewGuid().ToString("N"));
        string temporary = Path.Combine(directory, "profiles.json." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(corrupt, "recording"); File.WriteAllText(temporary, "recording");
        string ownerFile = Path.Combine(directory, "profiles.json.owner-notes"); File.WriteAllText(ownerFile, "keep");
        string preferences = Path.Combine(directory, "preferences.json"); File.WriteAllText(preferences, "keep");
        Assert.AreEqual(4, store.DeleteAllProfileData()); Assert.HasCount(0, store.Load().Profiles);
        Assert.AreEqual("keep", File.ReadAllText(ownerFile)); Assert.AreEqual("keep", File.ReadAllText(preferences));
        Assert.AreEqual(0, store.DeleteAllProfileData());
    }

    [TestMethod]
    public void PositionedSequenceRoundTripsAndMissingCalibrationIsRejected()
    {
        var point = new ClientPoint(10, 20, 800, 600, 144);
        var step = SequenceStep.Input("Click", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(50)) with { Position = point, EndPosition = point };
        var profile = Profile() with { Macros = [new(Guid.NewGuid(), "Click", null, Sequence: new([step], "04090409"))] };
        string file = Path.Combine(directory, "position.mecprofile"); HelperProfiles.Export(file, profile);
        Assert.AreEqual(point, HelperProfiles.Import(file).Macros[0].Sequence!.Steps[0].Position);
        Assert.AreEqual("04090409", HelperProfiles.Import(file).Macros[0].Sequence!.KeyboardLayout);
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"dpi\": 144", "\"dpi\": 0"));
        Assert.ThrowsExactly<ArgumentException>(() => HelperProfiles.Import(file));
    }

    [TestMethod]
    public void SaveLoadAndExportPreserveDataWithoutLocalActivation()
    {
        var store = new HelperProfiles(directory); var profile = Profile();
        store.Save([profile]); AssertEquivalent(profile, store.Load().Profiles.Single());
        string file = Path.Combine(directory, "helper.mecprofile"); HelperProfiles.Export(file, profile);
        var imported = HelperProfiles.Import(file);
        Assert.AreNotEqual(profile.Id, imported.Id);
        Assert.AreNotEqual(profile.Macros[0].Id, imported.Macros[0].Id);
        Assert.AreEqual(profile.Macros[0].Plan, imported.Macros[0].Plan);
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        CollectionAssert.AreEquivalent(new[] { "schemaVersion", "id", "name", "macros", "points" }, json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public void DamagedPrimaryRecoversBackupAndPreservesBothOnNextSave()
    {
        var store = new HelperProfiles(directory); var original = Profile("Original");
        store.Save([original]); store.Save([Profile("Later")]);
        File.WriteAllText(Path.Combine(directory, "profiles.json"), "broken");
        var load = store.Load(); Assert.IsNotNull(load.Warning); AssertEquivalent(original, load.Profiles.Single());
        var backup = File.ReadAllText(Path.Combine(directory, "profiles.json.bak"));
        store.Save([original, Profile("New")]);
        Assert.AreEqual(backup, File.ReadAllText(Path.Combine(directory, "profiles.json.bak")));
        Assert.AreEqual("broken", File.ReadAllText(Directory.GetFiles(directory, "*.corrupt-*").Single()));
        Assert.HasCount(2, store.Load().Profiles);
    }

    [TestMethod]
    public void MissingPrimaryRecoversBackupWithoutChangingIt()
    {
        var store = new HelperProfiles(directory); var original = Profile("Original");
        store.Save([original]); store.Save([Profile("Later")]);
        string path = Path.Combine(directory, "profiles.json");
        var backup = File.ReadAllBytes(path + ".bak"); File.Delete(path);
        var load = store.Load(); Assert.IsNotNull(load.Warning); AssertEquivalent(original, load.Profiles.Single());
        store.Save(load.Profiles);
        CollectionAssert.AreEqual(backup, File.ReadAllBytes(path + ".bak"));
        AssertEquivalent(original, store.Load().Profiles.Single());
    }

    [TestMethod]
    public void LockedPrimaryRejectsSaveWithoutRenamingOrLosingExistingData()
    {
        var store = new HelperProfiles(directory); store.Save([Profile("Original")]);
        string path = Path.Combine(directory, "profiles.json"); var original = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsExactly<IOException>(() => store.Save([Profile("Replacement")]));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        Assert.IsEmpty(Directory.GetFiles(directory, "*.corrupt-*"));
        store.Save([Profile("After unlock")]); Assert.AreEqual("After unlock", store.Load().Profiles.Single().Name);
    }

    [TestMethod]
    public void ReadOnlyDestinationPreservesPrimaryAndBackupOnFailure()
    {
        var store = new HelperProfiles(directory); store.Save([Profile("Backup")]); store.Save([Profile("Primary")]);
        string path = Path.Combine(directory, "profiles.json");
        var original = File.ReadAllBytes(path); var backup = File.ReadAllBytes(path + ".bak");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try { Assert.Throws<Exception>(() => store.Save([Profile("Must not replace")])); }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(backup, File.ReadAllBytes(path + ".bak"));
        Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
    }

    [TestMethod]
    public void DuplicateIdsCannotOverwriteCollection()
    {
        var store = new HelperProfiles(directory); var profile = Profile(); store.Save([profile]);
        Assert.ThrowsExactly<ArgumentException>(() => store.Save([profile, profile]));
        Assert.HasCount(1, store.Load().Profiles);
    }

    [TestMethod]
    [DataRow("version")][DataRow("unknown")][DataRow("missing")][DataRow("duplicate")][DataRow("input")]
    public void UnsupportedOrAmbiguousImportIsRejected(string mutation)
    {
        string path = Path.Combine(directory, "import.mecprofile"); HelperProfiles.Export(path, Profile());
        string json = File.ReadAllText(path);
        json = mutation switch
        {
            "version" => json.Replace($"\"schemaVersion\": {ProfileDefinition.CurrentVersion}", "\"schemaVersion\": 900"),
            "unknown" => json.Replace("\"name\":", "\"enabled\": true, \"name\":"),
            "missing" => json.Replace("\"count\": 3,", ""),
            "duplicate" => json.Replace("\"count\": 3", "\"count\": 3, \"count\": 0"),
            _ => json.Replace("\"code\": 17", "\"code\": 999")
        };
        File.WriteAllText(path, json);
        try { HelperProfiles.Import(path); Assert.Fail("Invalid import was accepted."); }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { }
    }

    [TestMethod]
    public void OversizedImportIsRejectedBeforeDeserialization()
    {
        string path = Path.Combine(directory, "large.mecprofile"); File.WriteAllBytes(path, new byte[HelperProfiles.MaximumProfileBytes + 1]);
        Assert.ThrowsExactly<ArgumentException>(() => HelperProfiles.Import(path));
    }

    [TestMethod]
    public void LegacyHelperMigratesWithoutLosingOriginalBackup()
    {
        var profile = Profile();
        var legacy = new HelperProfile(1, profile.Id, profile.Name, profile.Macros[0].Plan!);
        string source = JsonSerializer.Serialize(new[] { legacy }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(directory, "profiles.json"), source);
        var store = new HelperProfiles(directory); var load = store.Load();
        Assert.IsNull(load.Warning); Assert.AreEqual(ProfileDefinition.CurrentVersion, load.Profiles[0].SchemaVersion);
        Assert.AreEqual(legacy.Plan, load.Profiles[0].Macros[0].Plan);
        store.Save(load.Profiles);
        Assert.AreEqual(source, File.ReadAllText(Path.Combine(directory, "profiles.json.bak")));
        AssertEquivalent(load.Profiles[0], store.Load().Profiles[0]);
    }

    [TestMethod]
    public void LocalAmbiguousCountCannotBecomeUnbounded()
    {
        var store = new HelperProfiles(directory); store.Save([Profile()]);
        string path = Path.Combine(directory, "profiles.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"count\": 3,", ""));
        Assert.IsNotNull(store.Load().Warning); Assert.HasCount(0, store.Load().Profiles);
    }

    [TestMethod]
    public void MacroIdentityCannotBeSharedAcrossProfiles()
    {
        var profile = Profile(); var other = profile with { Id = Guid.NewGuid() };
        Assert.ThrowsExactly<ArgumentException>(() => new HelperProfiles(directory).Save([profile, other]));
    }

    [TestMethod]
    public void SavedToggleKeepsItsIdentityAndStaysSeparateFromAnOrdinaryHold()
    {
        var hold = new HelperPlan(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0);
        var toggle = new MacroDefinition(Guid.NewGuid(), "Toggle W", hold, IsToggle: true);
        var profile = Profile() with { Macros = [toggle, toggle with { Id = Guid.NewGuid(), IsToggle = false }] };
        var store = new HelperProfiles(directory); store.Save([profile]);
        AssertEquivalent(profile, store.Load().Profiles.Single());
        var file = Path.Combine(directory, "toggle.mecprofile"); HelperProfiles.Export(file, profile);
        var imported = HelperProfiles.Import(file);
        Assert.IsTrue(imported.Macros[0].IsToggle); Assert.IsFalse(imported.Macros[1].IsToggle);
        Assert.AreNotEqual(toggle.Id, imported.Macros[0].Id);
        Assert.ThrowsExactly<ArgumentException>(() => store.Save([profile with { Macros = [toggle with { Plan = hold with { Count = 1, ActiveDuration = TimeSpan.FromSeconds(1) } }] }]));
        store.Save([profile with { Macros = [toggle with { Plan = hold with { Control = InputControl.Mouse(PointerButton.Left) } }] }]);
        Assert.AreEqual(InputControl.Mouse(PointerButton.Left), store.Load().Profiles.Single().Macros.Single().Plan!.Control);
    }

    [TestMethod]
    public void VersionEightHoldRemainsAnOrdinaryMacroDuringUpgrade()
    {
        var profile = Profile(); var file = Path.Combine(directory, "v8.mecprofile");
        HelperProfiles.Export(file, profile);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["schemaVersion"] = 8; json["macros"]![0]!.AsObject().Remove("isToggle");
        File.WriteAllText(file, json.ToJsonString());
        var imported = HelperProfiles.Import(file);
        Assert.AreEqual(ProfileDefinition.CurrentVersion, imported.SchemaVersion); Assert.IsFalse(imported.Macros[0].IsToggle);
        Assert.AreEqual(profile.Macros[0].Plan, imported.Macros[0].Plan);
    }

    [TestMethod]
    public void GraveBindingPersistsLocallyAndConflictsWithGlobalOrOtherBindings()
    {
        var binding = new MacroBinding(Guid.NewGuid(), 0xC0, false);
        var store = new LocalBindings(directory); store.Save([binding]);
        Assert.AreEqual(binding, store.Load().Single());
        MacroBindings.Validate([binding], 0x77, 0x78);
        Assert.ThrowsExactly<ArgumentException>(() => MacroBindings.Validate([binding], 0xC0, 0x78));
        Assert.ThrowsExactly<ArgumentException>(() => MacroBindings.Validate([binding, binding with { MacroId = Guid.NewGuid() }], 0x77, 0x78));
    }

    [TestMethod]
    public void LocalBindingsRoundtripContainsNoActivationState()
    {
        var binding = new MacroBinding(Guid.NewGuid(), 0x70, false);
        var store = new LocalBindings(directory); store.Save([binding]);
        Assert.AreEqual(binding, store.Load().Single());
        Assert.IsFalse(File.ReadAllText(Path.Combine(directory, "bindings.json")).Contains("Enabled", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ToggleCanUseAControllerButtonInsteadOfOrBesideAKey()
    {
        var pad = new MacroBinding(Guid.NewGuid(), 0, false, ControllerButton.LeftBumper);
        var both = new MacroBinding(Guid.NewGuid(), 0x70, false, ControllerButton.RightBumper);
        MacroBindings.Validate([pad, both], 0x77, 0x78);
        var store = new LocalBindings(directory); store.Save([pad, both]);
        CollectionAssert.AreEqual(new[] { pad, both }, store.Load().ToArray());
        // Neither a key nor a controller button, or the same controller button twice, is refused.
        Assert.ThrowsExactly<ArgumentException>(() => MacroBindings.Validate([pad with { Controller = null }], 0x77, 0x78));
        Assert.ThrowsExactly<ArgumentException>(() => MacroBindings.Validate([pad, both with { Controller = ControllerButton.LeftBumper }], 0x77, 0x78));
        // Two controller-only toggles do not clash over their missing key.
        MacroBindings.Validate([pad, pad with { MacroId = Guid.NewGuid(), Controller = ControllerButton.A }], 0x77, 0x78);
    }

    [TestMethod]
    public void BindingsSavedBeforeControllerButtonsStillLoad()
    {
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(directory, "bindings.json"), $$"""{"Version":1,"Bindings":[{"MacroId":"{{id}}","Key":112,"HoldTrigger":false}]}""");
        Assert.AreEqual(new MacroBinding(id, 0x70, false), new LocalBindings(directory).Load().Single());
    }

    [TestMethod]
    public void ConflictingMacroBindingsAreRejected()
    {
        var first = new MacroBinding(Guid.NewGuid(), 0x70, false);
        MacroBindings.Validate([first], 0x77, 0x78);
        Assert.ThrowsExactly<ArgumentException>(() => MacroBindings.Validate([first, first with { MacroId = Guid.NewGuid() }], 0x77, 0x78));
        Assert.ThrowsExactly<ArgumentException>(() => MacroBindings.Validate([first with { Key = 0x78 }], 0x77, 0x78));
        MacroBindings.Validate([first with { Key = 0x7B }], 0x77, 0x78);
        Assert.ThrowsExactly<ArgumentException>(() => MacroBindings.Validate([first with { Key = 0xFF }], 0x77, 0x78));
    }

    [TestMethod]
    public void MoreThanTenIndependentShortcutsValidateAndPersist()
    {
        var bindings = Enumerable.Range(0x41, 26).Select(key => new MacroBinding(Guid.NewGuid(), key, false)).ToArray();
        MacroBindings.Validate(bindings, 0x77, 0x78);
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalBindings(directory); store.Save(bindings);
            CollectionAssert.AreEqual(bindings, store.Load().ToArray());
            Assert.ThrowsExactly<ArgumentException>(() => MacroBindings.Validate([.. bindings, bindings[0] with { MacroId = Guid.NewGuid() }], 0x77, 0x78));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void SpamToggleModeAndIntervalSurviveProfileStorage()
    {
        var spam = new MacroDefinition(Guid.NewGuid(), "Spam E",
            new(InputControl.Key(0x12), HelperMode.Repeat, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(2), 0), IsToggle: true);
        var profile = new ProfileDefinition(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Toggles", [spam]);
        var store = new HelperProfiles(directory); store.Save([profile]);
        var restored = store.Load().Profiles.Single().Macros.Single();
        Assert.IsTrue(restored.IsToggle); Assert.AreEqual(HelperMode.Repeat, restored.Plan!.Mode);
        Assert.AreEqual(TimeSpan.FromMilliseconds(10), restored.Plan.Interval);
        Assert.AreEqual(TimeSpan.FromMilliseconds(2), restored.Plan.PressDuration);
    }

    private static void AssertEquivalent(ProfileDefinition expected, ProfileDefinition actual)
    {
        Assert.AreEqual(expected.Id, actual.Id); Assert.AreEqual(expected.Name, actual.Name);
        CollectionAssert.AreEqual(expected.Macros.ToArray(), actual.Macros.ToArray());
    }

    [TestMethod]
    public void SequenceRoundtripRetainsOverlapAndUsesNewImportIdentities()
    {
        var group = SequenceStep.Group("Overlap", TimeSpan.FromSeconds(2),
            [new(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(0x11), TimeSpan.FromSeconds(2)),
             new(Guid.NewGuid(), TimeSpan.FromMilliseconds(200), InputControl.Key(0x39), TimeSpan.FromMilliseconds(50))]);
        var profile = Profile(); profile = profile with { Macros = [profile.Macros[0] with { Plan = null, Sequence = new([group]) }] };
        string path = Path.Combine(directory, "sequence.mecprofile"); HelperProfiles.Export(path, profile);
        var imported = HelperProfiles.Import(path);
        CollectionAssert.AreEqual(SequenceCompiler.Compile(profile.Macros[0].Sequence!).Inputs.ToArray(),
            SequenceCompiler.Compile(imported.Macros[0].Sequence!).Inputs.ToArray());
        Assert.IsNull(imported.Macros[0].Plan);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"enabled\": true,", ""));
        Assert.ThrowsExactly<ArgumentException>(() => HelperProfiles.Import(path));
    }

    [TestMethod]
    public void VersionTwoGroupedHelpersUpgradeWithoutChangingIdsOrTiming()
    {
        var profile = Profile();
        var json = JsonSerializer.Serialize(new { schemaVersion = 2, id = profile.Id, name = profile.Name,
            macros = profile.Macros.Select(m => new { id = m.Id, name = m.Name, plan = m.Plan, notes = m.Notes }) },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(directory, "profiles.json"), "[" + json + "]");
        var loaded = new HelperProfiles(directory).Load();
        Assert.IsNull(loaded.Warning); AssertEquivalent(profile, loaded.Profiles[0]);
    }
}
