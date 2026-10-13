using System.Globalization;

namespace MinimumEffortClicker.Storage;

public static class ScreenshotFiles
{
    /// <summary>Commit a complete image under a unique name; never overwrite an existing file.</summary>
    public static string Save(string folder, Action<Stream> encode, CancellationToken cancellation,
        string? targetTitle = null, DateTime? capturedAt = null, bool dailyFolders = false)
    {
        ArgumentNullException.ThrowIfNull(encode);
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            throw new ArgumentException("Choose a full screenshot folder path.");
        cancellation.ThrowIfCancellationRequested();
        var at = capturedAt ?? DateTime.Now;
        folder = DestinationDirectory(folder, dailyFolders, at);
        Directory.CreateDirectory(folder);
        string name = SafeTitle(targetTitle) + " " + at.ToString("yyyy-MM-dd HH-mm-ss-fff", CultureInfo.InvariantCulture);
        string temporary = Path.Combine(folder, ".ME-" + Guid.NewGuid().ToString("N") + ".png.tmp");
        bool created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                encode(output);
                cancellation.ThrowIfCancellationRequested();
                output.Flush(true);
            }
            // Encode once. Atomically claim a name without replacing an owner's image, even
            // when multiple captures share a timestamp or another process creates the file.
            for (int suffix = 1; suffix <= 10000; suffix++)
            {
                cancellation.ThrowIfCancellationRequested();
                string path = Path.Combine(folder, name + (suffix == 1 ? "" : $" ({suffix})") + ".png");
                try { File.Move(temporary, path, false); return path; }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path)) { }
            }
            throw new IOException("Too many screenshots already use this name and time.");
        }
        finally
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static string DestinationDirectory(string folder, bool dailyFolders, DateTime capturedAt) =>
        dailyFolders ? Path.Combine(folder, capturedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : folder;

    private static string SafeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Desktop";
        var title = new string(value.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray());
        title = string.Join(" ", title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (title.Length > 100) title = title[..(char.IsHighSurrogate(title[99]) ? 99 : 100)].TrimEnd(' ', '.');
        if (title.Length == 0) return "Desktop";
        var stem = title.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" or "CONIN$" or "CONOUT$"
            || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789¹²³".Contains(stem[3])) title = "_" + title;
        return title;
    }
}
