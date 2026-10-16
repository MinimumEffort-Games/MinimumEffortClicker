using System.Text.Json;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Storage;

/// <summary>
/// The Controller to Key binds (controller-to-key.json, version 1), written atomically. A file that cannot be read
/// leaves the list empty rather than guessing; the app then says so instead of writing over it.
/// </summary>
public sealed class ControllerKeyBinds(string directory)
{
    public const int MaxBinds = 100;
    private const string FileName = "controller-to-key.json";

    public string FilePath => Path.Combine(directory, FileName);

    /// <summary>The saved binds, or null when the file exists but could not be read.</summary>
    public IReadOnlyList<ControllerKeyBind>? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            if (new FileInfo(FilePath).Length > 65536) return null;
            var value = JsonSerializer.Deserialize<BindFile>(File.ReadAllBytes(FilePath));
            return value is { Version: 1, Binds: not null } && Valid(value.Binds) ? value.Binds : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void Save(IReadOnlyList<ControllerKeyBind> binds)
    {
        if (!Valid(binds)) throw new ArgumentException("Invalid Controller to Key binds.");
        Directory.CreateDirectory(directory);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, new BindFile(1, binds)); stream.Flush(true); }
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>At most 100 binds, each valid, with its own id and its own controller button.</summary>
    public static bool Valid(IReadOnlyList<ControllerKeyBind> binds) => binds.Count <= MaxBinds
        && binds.All(b => b is not null && b.Valid)
        && binds.Select(b => b.Id).Distinct().Count() == binds.Count
        && binds.Select(b => b.Button).Distinct().Count() == binds.Count;

    private sealed record BindFile(int Version, IReadOnlyList<ControllerKeyBind> Binds);
}
