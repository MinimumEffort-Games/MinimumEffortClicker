using System.Text.Json;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Storage;

/// <summary>Individual files for the Actions Save/Load workflow; the older library is preserved.</summary>
public static class MacroFiles
{
    public static string SavePath(string directory, MacroDefinition macro)
    {
        Directory.CreateDirectory(directory);
        string name = SafeName(macro.Name);
        string path = Path.Combine(directory, name + ".mecprofile");
        if (Available(path, macro.Id)) return path;
        path = Path.Combine(directory, name + " - " + macro.Id.ToString("N") + ".mecprofile");
        if (Available(path, macro.Id)) return path;
        // Preserve unrelated or unreadable owner files even if both conventional names are taken.
        do { path = Path.Combine(directory, name + " - " + Guid.NewGuid().ToString("N") + ".mecprofile"); } while (File.Exists(path));
        return path;
    }
    /// <summary>A macro name made safe as a Windows file name (no reserved device names or invalid characters).</summary>
    public static string SafeName(string macroName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string name = new(macroName.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0) name = "Macro";
        string stem = name.Split('.')[0].TrimEnd();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "Macro " + name;
        return name;
    }
    private static bool Available(string path, Guid macroId)
    {
        if (!File.Exists(path)) return true;
        try { var file = HelperProfiles.Open(path); return file.Macros.Count == 1 && file.Macros[0].Id == macroId; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return false; }
    }
    public static int EnsureLibraryFiles(string directory, IReadOnlyList<ProfileDefinition> profiles)
    {
        Directory.CreateDirectory(directory);
        int created = 0;
        foreach (var profile in profiles)
            foreach (var macro in profile.Macros.Where(m => !m.IsToggle))
            {
                string path = SavePath(directory, macro);
                if (File.Exists(path)) continue;
                HelperProfiles.Export(path, profile with { Name = macro.Name, Macros = [macro] }); created++;
            }
        return created;
    }
}
