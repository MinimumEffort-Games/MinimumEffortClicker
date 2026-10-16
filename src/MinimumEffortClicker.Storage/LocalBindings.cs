using System.Text.Json;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Storage;

/// <summary>Machine-local assignments only. Restoring assignments never enables a toggle; which toggles were active is kept in preferences.</summary>
public sealed class LocalBindings(string directory)
{
    public IReadOnlyList<MacroBinding> Load()
    {
        try
        {
            string path = Path.Combine(directory, "bindings.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return [];
            var value = JsonSerializer.Deserialize<BindingFile>(File.ReadAllBytes(path));
            return value is { Version: 1, Bindings: not null } && Valid(value.Bindings) ? value.Bindings : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
    public void Save(IReadOnlyList<MacroBinding> bindings)
    {
        if (!Valid(bindings)) throw new ArgumentException("Invalid local bindings.");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "bindings.json"), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, new BindingFile(1, bindings)); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool Valid(IReadOnlyList<MacroBinding> bindings) => bindings.Count <= 500
        && bindings.All(b => b is not null && b.MacroId != Guid.Empty && MacroBindings.IsSupportedKey(b.Key))
        && bindings.Select(b => b.MacroId).Distinct().Count() == bindings.Count;
    private sealed record BindingFile(int Version, IReadOnlyList<MacroBinding> Bindings);
}
