using System.Text.Json;
using System.Text.RegularExpressions;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Storage;

/// <summary>
/// Moves macros between the Macros folder and the Workshop. Subscribed macros are copied into Macros\Workshop so the
/// normal Load Macro picker shows them; each copy is named "<macro name> - <item id>.mecprofile", and only files with
/// that shape are ever replaced or removed there.
/// </summary>
public static partial class WorkshopMacros
{
    public const string FolderName = "Workshop";
    public const string UploadFileName = "macro.mecprofile";

    public static string Folder(string macroDirectory) => Path.Combine(macroDirectory, FolderName);

    public static bool IsWorkshopCopy(string macroDirectory, string path) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(Folder(macroDirectory)), StringComparison.OrdinalIgnoreCase);

    /// <summary>A folder holding only this macro file, which is what Steam uploads as the item's content.</summary>
    public static string PrepareUpload(string macroFile, string workRoot)
    {
        HelperProfiles.Open(macroFile); // Never share a file the app itself could not open again.
        string folder = Path.Combine(workRoot, "upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.Copy(macroFile, Path.Combine(folder, UploadFileName));
        return folder;
    }

    /// <summary>
    /// Brings Macros\Workshop in line with the subscribed items: new and updated macros are copied in, macros of items
    /// no longer subscribed are removed. An item whose download holds no macro the app can open is skipped.
    /// </summary>
    public static WorkshopSyncResult Sync(IReadOnlyList<WorkshopInstalledItem> items, string macroDirectory)
    {
        string folder = Folder(macroDirectory);
        Directory.CreateDirectory(folder);
        var copies = Directory.GetFiles(folder, "*.mecprofile")
            .Select(path => (Path: path, Match: CopyName().Match(Path.GetFileName(path))))
            .Where(c => c.Match.Success && ulong.TryParse(c.Match.Groups["id"].Value, out _))
            .Select(c => (c.Path, Id: ulong.Parse(c.Match.Groups["id"].Value)))
            .ToList();
        int added = 0, updated = 0, removed = 0, skipped = 0;
        foreach (var item in items)
        {
            string? source = Directory.Exists(item.Folder) ? Directory.GetFiles(item.Folder, "*.mecprofile").OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault() : null;
            MacroDefinition? macro = null;
            if (source is not null)
                try { macro = HelperProfiles.Open(source).Macros.FirstOrDefault(m => !m.IsToggle); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { }
            if (source is null || macro is null) { skipped++; continue; }
            string target = Path.Combine(folder, $"{MacroFiles.SafeName(macro.Name)} - {item.Id}.mecprofile");
            var old = copies.Where(c => c.Id == item.Id).ToList();
            foreach (var stale in old.Where(c => !string.Equals(c.Path, target, StringComparison.OrdinalIgnoreCase))) File.Delete(stale.Path); // renamed by its author
            bool existed = File.Exists(target);
            if (existed && File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(source))) continue;
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(source, temporary); File.Move(temporary, target, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            if (existed || old.Count > 0) updated++; else added++;
        }
        var subscribed = items.Select(i => i.Id).ToHashSet();
        foreach (var copy in copies.Where(c => !subscribed.Contains(c.Id) && File.Exists(c.Path))) { File.Delete(copy.Path); removed++; }
        return new(added, updated, removed, skipped);
    }

    [GeneratedRegex(@"^.+ - (?<id>\d{1,20})\.mecprofile$", RegexOptions.IgnoreCase)]
    private static partial Regex CopyName();
}
