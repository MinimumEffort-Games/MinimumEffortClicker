using System.Text.Json;
using System.Text.Json.Serialization;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Storage;

public sealed record HelperProfile(int SchemaVersion, Guid Id, string Name, HelperPlan Plan);
public sealed record ProfileLoad(IReadOnlyList<ProfileDefinition> Profiles, string? Warning);

/// <summary>Small versioned data-only helper profiles. No handles, executable paths, hotkeys or enabled state.</summary>
public sealed class HelperProfiles(string directory)
{
    public const int MaximumProfileBytes = 2 * 1024 * 1024;
    private const int Limit = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16
    };
    private string PathName => Path.Combine(directory, "profiles.json");
    public string DirectoryPath => directory;

    /// <summary>Explicit library reset, including this store's retained recording backups. No recursion or unrelated files.</summary>
    public int DeleteAllProfileData()
    {
        if (!Directory.Exists(directory)) return 0;
        var files = Directory.EnumerateFiles(directory, "profiles.json*", SearchOption.TopDirectoryOnly)
            .Where(path => IsProfileDataFile(Path.GetFileName(path))).ToArray();
        int removed = 0;
        foreach (var file in files) { File.Delete(file); removed++; }
        return removed;
    }
    private static bool IsProfileDataFile(string name)
    {
        if (name is "profiles.json" or "profiles.json.bak") return true;
        const string corrupt = "profiles.json.corrupt-", temporary = "profiles.json.";
        if (name.StartsWith(corrupt, StringComparison.Ordinal)) return Guid.TryParseExact(name[corrupt.Length..], "N", out _);
        return name.StartsWith(temporary, StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal)
            && Guid.TryParseExact(name[temporary.Length..^4], "N", out _);
    }

    public ProfileLoad Load()
    {
        if (!File.Exists(PathName) && !File.Exists(PathName + ".bak")) return new([], null);
        try { return new(ReadAll(PathName), null); }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            try { return new(ReadAll(PathName + ".bak"), "Recovered the last good backup. The primary file is missing or unreadable; any existing file is retained until the next successful save."); }
            catch (Exception backup) when (backup is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
            { return new([], "Profiles could not be loaded. Original files were preserved; review the data folder before saving."); }
        }
    }

    public void Save(IReadOnlyList<ProfileDefinition> profiles)
    {
        ValidateAll(profiles);
        Directory.CreateDirectory(directory);
        string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(profiles, Options);
            if (bytes.Length > Limit) throw new ArgumentException("Profile collection exceeds 8 MiB.");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            if (File.Exists(PathName))
            {
                bool valid;
                try { ReadAll(PathName); valid = true; }
                catch (Exception ex) when (ex is JsonException or ArgumentException) { valid = false; }
                if (valid) File.Replace(temporary, PathName, PathName + ".bak");
                else
                {
                    File.Copy(PathName, PathName + ".corrupt-" + Guid.NewGuid().ToString("N"));
                    File.Replace(temporary, PathName, null); // Atomic replacement; retain the last good backup.
                }
            }
            else File.Move(temporary, PathName);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ProfileDefinition Import(string path)
    {
        var profile = Open(path);
        return profile with { Id = Guid.NewGuid(), Macros = profile.Macros.Select(m => m with { Id = Guid.NewGuid() }).ToArray() };
    }
    /// <summary>Read a validated macro file for editing, preserving its identity. Contains no runtime bindings.</summary>
    public static ProfileDefinition Open(string path)
    {
        byte[] data = ReadBounded(path, MaximumProfileBytes);
        using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 16 });
        return ParseProfile(document.RootElement);
    }
    private static ProfileDefinition ParseProfile(JsonElement element)
    {
        RequireFields(element, ["schemaVersion", "id", "name"]);
        var versionElement = element.GetProperty("schemaVersion");
        if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out int version)) throw new ArgumentException("Invalid version.");
        ProfileDefinition profile;
        if (version == 1)
        {
            RequireFields(element, ["schemaVersion", "id", "name", "plan"]);
            ValidatePlanFields(element.GetProperty("plan"));
            var legacy = element.Deserialize<HelperProfile>(Options) ?? throw new ArgumentException("Empty profile.");
            profile = new(ProfileDefinition.CurrentVersion, legacy.Id, legacy.Name, [new(legacy.Id, legacy.Name, legacy.Plan)]);
        }
        else if (version is 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or ProfileDefinition.CurrentVersion)
        {
            RequireFields(element, ["schemaVersion", "id", "name", "macros"]);
            if (version >= 4) RequireFields(element, ["points"]);
            if (element.TryGetProperty("points", out var points) && points.ValueKind != JsonValueKind.Null)
            {
                if (points.ValueKind != JsonValueKind.Array) throw new ArgumentException("Expected named points.");
                foreach (var point in points.EnumerateArray())
                {
                    RequireFields(point, ["id", "name", "position"]);
                    RequireFields(point.GetProperty("position"), ["x", "y", "width", "height", "dpi"]);
                }
            }
            var macros = element.GetProperty("macros");
            if (macros.ValueKind != JsonValueKind.Array) throw new ArgumentException("Expected macros.");
            foreach (var macro in macros.EnumerateArray())
            {
                RequireFields(macro, ["id", "name", "plan", "notes"]);
                if (version >= 3) RequireFields(macro, ["sequence"]);
                if (version >= 9) RequireFields(macro, ["isToggle"]);
                if (macro.GetProperty("plan").ValueKind != JsonValueKind.Null) ValidatePlanFields(macro.GetProperty("plan"));
                if (macro.TryGetProperty("sequence", out var sequence) && sequence.ValueKind != JsonValueKind.Null)
                    ValidateSequenceFields(sequence);
            }
            profile = element.Deserialize<ProfileDefinition>(Options) ?? throw new ArgumentException("Empty profile.");
            profile = profile with { SchemaVersion = ProfileDefinition.CurrentVersion };
        }
        else throw new ArgumentException("Unsupported profile version.");
        Validate(profile);
        return profile;
    }
    private static void ValidatePlanFields(JsonElement plan)
    {
        RequireFields(plan, ["control", "mode", "interval", "pressDuration", "count", "activeDuration"]);
        RequireFields(plan.GetProperty("control"), ["kind", "code"]);
    }
    private static void ValidateSequenceFields(JsonElement sequence)
    {
        RequireFields(sequence, ["steps"]);
        if (sequence.TryGetProperty("repetition", out var repetition) && repetition.ValueKind != JsonValueKind.Null)
            RequireFields(repetition, ["count", "delayBetweenLoops", "activeDuration"]);
        if (sequence.TryGetProperty("subsequences", out var library) && library.ValueKind != JsonValueKind.Null)
        {
            if (library.ValueKind != JsonValueKind.Array) throw new ArgumentException("Expected reusable subsequences.");
            foreach (var body in library.EnumerateArray())
            { RequireFields(body, ["id", "name", "steps"]); ValidateSequenceFields(body); }
        }
        var steps = sequence.GetProperty("steps");
        if (steps.ValueKind != JsonValueKind.Array) throw new ArgumentException("Expected action steps.");
        foreach (var step in steps.EnumerateArray())
        {
            RequireFields(step, ["id", "name", "enabled", "kind", "duration", "control", "children"]);
            ValidatePositions(step);
            if (step.TryGetProperty("motion", out var motion) && motion.ValueKind != JsonValueKind.Null)
                RequireFields(motion, ["moveDuration", "offsetPixels"]);
            if (step.TryGetProperty("flow", out var flow) && flow.ValueKind != JsonValueKind.Null)
                RequireFields(flow, ["kind", "bodyId", "elseId", "maximumIterations"]);
            if (step.TryGetProperty("condition", out var condition) && condition.ValueKind != JsonValueKind.Null)
            {
                RequireFields(condition, ["kind", "policy", "region", "color", "tolerance", "changedFraction"]);
                RequireFields(condition.GetProperty("policy"), ["sampleInterval", "timeout", "stableSamples", "timeoutAction", "retries"]);
                if (condition.TryGetProperty("template", out var template) && template.ValueKind != JsonValueKind.Null) RequireFields(template, ["width", "height", "bgra"]);
                if (condition.GetProperty("region").ValueKind != JsonValueKind.Null)
                {
                    var region = condition.GetProperty("region"); RequireFields(region, ["origin", "width", "height"]);
                    RequireFields(region.GetProperty("origin"), ["x", "y", "width", "height", "dpi"]);
                }
            }
            if (step.GetProperty("control").ValueKind != JsonValueKind.Null) RequireFields(step.GetProperty("control"), ["kind", "code"]);
            var children = step.GetProperty("children");
            if (children.ValueKind == JsonValueKind.Null) continue;
            if (children.ValueKind != JsonValueKind.Array) throw new ArgumentException("Expected timed children.");
            foreach (var child in children.EnumerateArray())
            {
                RequireFields(child, ["id", "offset", "control", "duration"]);
                ValidatePositions(child);
                RequireFields(child.GetProperty("control"), ["kind", "code"]);
            }
        }
    }
    private static void ValidatePositions(JsonElement element)
    {
        foreach (string name in new[] { "position", "endPosition" })
            if (element.TryGetProperty(name, out var point) && point.ValueKind != JsonValueKind.Null)
                RequireFields(point, ["x", "y", "width", "height", "dpi"]);
    }
    private static void RequireFields(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a profile object.");
        var fields = element.EnumerateObject().Select(p => p.Name).ToArray();
        if (fields.Length != fields.Distinct(StringComparer.Ordinal).Count() || !names.All(n => fields.Contains(n, StringComparer.Ordinal)))
            throw new ArgumentException("Profile contains missing or duplicate fields.");
    }
    public static void Export(string path, ProfileDefinition profile)
    {
        Validate(profile);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(profile, Options);
        if (bytes.Length > MaximumProfileBytes) throw new ArgumentException("Export exceeds the 2 MiB profile size limit.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static IReadOnlyList<ProfileDefinition> ReadAll(string path)
    {
        using var document = JsonDocument.Parse(ReadBounded(path, Limit), new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("Expected a profile collection.");
        var profiles = document.RootElement.EnumerateArray().Select(ParseProfile).ToArray();
        ValidateAll(profiles);
        return profiles;
    }
    private static byte[] ReadBounded(string path, int limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > limit) throw new ArgumentException("Profile file exceeds the size limit.");
        var data = new byte[(int)stream.Length];
        stream.ReadExactly(data);
        return data;
    }
    private static void ValidateAll(IReadOnlyList<ProfileDefinition> profiles)
    {
        if (profiles.Count > 500) throw new ArgumentException("At most 500 helper profiles are supported.");
        if (profiles.Select(x => x?.Id).Distinct().Count() != profiles.Count) throw new ArgumentException("Duplicate profile IDs.");
        foreach (var profile in profiles) Validate(profile);
        var macros = profiles.SelectMany(x => x.Macros).ToArray();
        if (macros.Select(x => x.Id).Distinct().Count() != macros.Length) throw new ArgumentException("Duplicate macro IDs across profiles.");
    }
    private static void Validate(ProfileDefinition? profile)
    {
        if (profile is null) throw new ArgumentException("Empty profile.");
        var errors = profile.Validate();
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors));
    }
}
