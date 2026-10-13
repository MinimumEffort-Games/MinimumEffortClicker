using System.Text.Json;
using System.Text.Json.Serialization;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Storage;

public sealed record UserPreferences(int SchemaVersion, int Appearance, int ToggleKey, int StopKey, bool HoldTrigger, bool Overlay,
    OverlayOptions? OverlayOptions = null, bool MainTopmost = false, int ClicksPerSecond = 20, bool KeepAwake = false, bool ScaleClickPositions = true,
    bool CloseToTray = true, bool TrayNoticeShown = false, ScreenshotSettings? Screenshots = null, int RecordKey = 0x79)
{
    public static UserPreferences Default => new(1, 0, 0x77, 0x78, false, true);
    public bool Valid => SchemaVersion == 1 && Appearance is >= 0 and <= 2 && ShortcutKeys.Supported(ToggleKey)
        && ShortcutKeys.Supported(StopKey) && ToggleKey != StopKey && (OverlayOptions?.Valid ?? true)
        && (Screenshots?.Valid ?? true) && ClicksPerSecond is >= 1 and <= 100 && ShortcutKeys.Supported(RecordKey);
}

public sealed class Preferences(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip, // Older versions may have written fields that no longer exist.
        IgnoreReadOnlyProperties = true
    };
    public UserPreferences Load()
    {
        try
        {
            string path = Path.Combine(directory, "preferences.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 32768) return UserPreferences.Default;
            var preferences = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllBytes(path), Options);
            return preferences?.Valid == true ? preferences : UserPreferences.Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return UserPreferences.Default; }
    }
    public void Save(UserPreferences preferences)
    {
        if (!preferences.Valid) throw new ArgumentException("Invalid preferences.");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "preferences.json");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, preferences, Options); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
