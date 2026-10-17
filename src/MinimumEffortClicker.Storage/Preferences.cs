using System.Text.Json;
using System.Text.Json.Serialization;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Storage;

public sealed record UserPreferences(int SchemaVersion, int Appearance, int ToggleKey, int StopKey, bool HoldTrigger, bool Overlay,
    OverlayOptions? OverlayOptions = null, bool MainTopmost = false, int ClicksPerSecond = 20, bool KeepAwake = false, bool ScaleClickPositions = true,
    bool CloseToTray = true, bool TrayNoticeShown = false, ScreenshotSettings? Screenshots = null, int RecordKey = ShortcutKeys.DefaultRecordKey,
    EmergencyStopMode EmergencyStop = EmergencyStopMode.OnlyStopAll, bool InputAccessNotice = true,
    ActionsChoices? Actions = null, Guid[]? ActiveToggles = null, ControllerControls? Controller = null)
{
    /// <summary>The most toggles that can be remembered as active; far more than the Toggles page is made for.</summary>
    public const int MaxActiveToggles = 200;
    public static UserPreferences Default => new(1, 0, ShortcutKeys.DefaultStartKey, ShortcutKeys.DefaultStopKey, false, true);
    public bool Valid => SchemaVersion == 1 && Appearance is >= 0 and <= 2 && ShortcutKeys.Supported(ToggleKey)
        && ShortcutKeys.Supported(StopKey) && ToggleKey != StopKey && (OverlayOptions?.Valid ?? true)
        && (Screenshots?.Valid ?? true) && ClicksPerSecond is >= 1 and <= 100 && ShortcutKeys.Supported(RecordKey) && Enum.IsDefined(EmergencyStop)
        && (ActiveToggles is null || ActiveToggles.Length <= MaxActiveToggles && !ActiveToggles.Contains(Guid.Empty))
        && (Controller?.Valid ?? true);
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
