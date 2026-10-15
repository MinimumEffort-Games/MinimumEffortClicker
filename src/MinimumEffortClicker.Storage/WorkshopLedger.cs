using System.Text.Json;

namespace MinimumEffortClicker.Storage;

/// <summary>
/// workshop.json. It records which saved macro was shared as which Workshop item, so sharing again updates that item.
/// It also records the id of every action that arrived through the Workshop, so those actions stay marked as made by
/// someone else after the user saves them into a macro of their own. It sits beside the macros, and Steam Cloud
/// carries it to the user's other PCs.
/// </summary>
public sealed class WorkshopLedger(string directory)
{
    public const int CurrentVersion = 1;
    private const long SizeLimitBytes = 8 * 1024 * 1024;
    private string PathName => Path.Combine(directory, "workshop.json");

    public WorkshopShare? Find(Guid macroId) => Read().Shares.TryGetValue(macroId, out var share) ? share : null;

    /// <summary>Ids of actions that came from the Workshop, on this PC or another one synced through Steam Cloud.</summary>
    public IReadOnlySet<Guid> OriginSteps() => Read().Origins;

    /// <summary>Records the item for a macro; null forgets it (the item was deleted on Steam).</summary>
    public void Set(Guid macroId, WorkshopShare? share)
    {
        if (macroId == Guid.Empty) throw new ArgumentException("A shared macro needs its id.");
        if (share is { ItemId: 0 }) throw new ArgumentException("A Workshop item id is never 0.");
        var (shares, origins) = Read();
        var updated = shares.ToDictionary();
        if (share is not null) updated[macroId] = share; else updated.Remove(macroId);
        Write(updated, origins);
    }

    /// <summary>Marks actions as Workshop actions. Marks are never removed: a Workshop action stays one.</summary>
    public void AddOriginSteps(IEnumerable<Guid> stepIds)
    {
        var (shares, origins) = Read();
        var updated = new HashSet<Guid>(origins);
        if (!stepIds.Where(id => id != Guid.Empty).Aggregate(false, (changed, id) => updated.Add(id) | changed)) return;
        Write(shares, updated);
    }

    private (IReadOnlyDictionary<Guid, WorkshopShare> Shares, IReadOnlySet<Guid> Origins) Read()
    {
        try
        {
            if (!File.Exists(PathName) || new FileInfo(PathName).Length > SizeLimitBytes) return (new Dictionary<Guid, WorkshopShare>(), new HashSet<Guid>());
            var value = JsonSerializer.Deserialize<LedgerFile>(File.ReadAllBytes(PathName));
            // A file from a newer app version is left alone rather than misread.
            if (value is not { Version: >= 1 and <= CurrentVersion }) return (new Dictionary<Guid, WorkshopShare>(), new HashSet<Guid>());
            var shares = (value.Items ?? []).Where(i => i is { Share: { ItemId: not 0, Title: not null, Description: not null } } && i.MacroId != Guid.Empty && Enum.IsDefined(i.Share.Visibility))
                .GroupBy(i => i.MacroId).ToDictionary(g => g.Key, g => g.Last().Share);
            return (shares, (value.OriginSteps ?? []).Where(id => id != Guid.Empty).ToHashSet());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return (new Dictionary<Guid, WorkshopShare>(), new HashSet<Guid>()); }
    }

    private void Write(IReadOnlyDictionary<Guid, WorkshopShare> shares, IReadOnlySet<Guid> origins)
    {
        Directory.CreateDirectory(directory);
        string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new LedgerFile(CurrentVersion, shares.Select(p => new SharedMacro(p.Key, p.Value)).ToArray(), origins.Order().ToArray()));
                stream.Flush(true);
            }
            File.Move(temporary, PathName, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record LedgerFile(int Version, IReadOnlyList<SharedMacro>? Items, IReadOnlyList<Guid>? OriginSteps);
    private sealed record SharedMacro(Guid MacroId, WorkshopShare Share);
}
