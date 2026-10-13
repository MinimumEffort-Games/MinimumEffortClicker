using MinimumEffortClicker.Linux;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MinimumEffortClicker.App;

// Opt-in local measurement. No persistent startup logging in ordinary use.
internal static class StartupMeasurement
{
    private static readonly List<object> stages = [];
    private static Stopwatch? watch;
    private static double processAgeAtStart;
    private static string? destination;
    internal static bool Enabled => destination is not null;
    internal static void Begin(string path)
    {
        using var process = Process.GetCurrentProcess();
        processAgeAtStart = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        watch = Stopwatch.StartNew(); destination = Path.GetFullPath(path);
        Mark("WPF startup entered");
    }
    internal static void Mark(string stage)
    {
        if (watch is not null && stages.Count < 24)
            stages.Add(new { stage, processElapsedMs = processAgeAtStart + watch.Elapsed.TotalMilliseconds });
    }
    internal static async Task CompleteAsync(bool controlsAvailable, bool stopped, bool settingsLoaded)
    {
        if (destination is null) return;
        Mark("Settings loaded and UI dispatcher idle");
        var report = new { scope = "Opt-in process-start to initialized UI measurement; no input generated",
            utc = DateTimeOffset.UtcNow, controlsAvailable, stopped, settingsLoaded, stages = stages.ToArray() };
        var path = destination; destination = null;
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true);
            await JsonSerializer.SerializeAsync(stream, report, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (IOException) { /* Measurement failure does not change playback or normal startup. */ }
        catch (UnauthorizedAccessException) { }
    }
}
