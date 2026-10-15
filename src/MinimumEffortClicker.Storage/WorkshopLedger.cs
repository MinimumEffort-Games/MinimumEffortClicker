using System.Text.Json;

namespace MinimumEffortClicker.Storage;

/// <summary>
/// Which saved macro was shared as which Workshop item (workshop.json), so sharing a macro again updates its item
/// instead of making a second one. Kept beside the macros, so Steam Cloud carries it to the user's other PCs.
/// </summary>
public sealed class WorkshopShares(string directory)
{
    public const int CurrentVersion = 1;
    private string PathName => Path.Combine(directory, "workshop.json");

    public IReadOnlyDictionary<Guid, WorkshopShare> Load()
    {
        try
        {
            if (!File.Exists(PathName) || new FileInfo(PathName).Length > 1_048_576) return new Dictionary<Guid, WorkshopShare>();
            var value = JsonSerializer.Deserialize<ShareFile>(File.ReadAllBytes(PathName));
            // A file from a newer app version is left alone rather than misread.
            if (value is not { Version: >= 1 and <= CurrentVersion, Items: not null }) return new Dictionary<Guid, WorkshopShare>();
            return value.Items.Where(i => i is { Share: { ItemId: not 0, Title: not null, Description: not null } } && i.MacroId != Guid.Empty && Enum.IsDefined(i.Share.Visibility))
                .GroupBy(i => i.MacroId).ToDictionary(g => g.Key, g => g.Last().Share);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new Dictionary<Guid, WorkshopShare>(); }
    }

    public WorkshopShare? Find(Guid macroId) => Load().TryGetValue(macroId, out var share) ? share : null;

    /// <summary>Records the item for a macro; null forgets it (the item was deleted on Steam).</summary>
    public void Set(Guid macroId, WorkshopShare? share)
    {
        if (macroId == Guid.Empty) throw new ArgumentException("A shared macro needs its id.");
        if (share is { ItemId: 0 }) throw new ArgumentException("A Workshop item id is never 0.");
        var items = Load().ToDictionary();
        if (share is not null) items[macroId] = share; else items.Remove(macroId);
        Directory.CreateDirectory(directory);
        string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, new ShareFile(CurrentVersion, items.Select(p => new SharedMacro(p.Key, p.Value)).ToArray())); stream.Flush(true); }
            File.Move(temporary, PathName, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record ShareFile(int Version, IReadOnlyList<SharedMacro> Items);
    private sealed record SharedMacro(Guid MacroId, WorkshopShare Share);
}
