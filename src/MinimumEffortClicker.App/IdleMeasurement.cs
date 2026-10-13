using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

// Explicit diagnostic mode: no input, hooks beyond normal startup, or recurring logging.
internal static class IdleMeasurement
{
    internal static string? Destination { get; private set; }
    private static int seconds;
    internal static void Begin(string path, int duration)
    {
        if (duration is < 10 or > 600) throw new ArgumentOutOfRangeException(nameof(duration));
        Destination = Path.GetFullPath(path); seconds = duration;
        if (File.Exists(Destination)) throw new IOException("Choose a new idle report path.");
    }
    internal static async Task CompleteAsync(Func<RuntimeSnapshot> snapshot, Func<(long Refreshes, long Placements)> activity)
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var initial = snapshot();
        var initialActivity = activity();
        var initialThreads = process.Threads.Cast<ProcessThread>().ToDictionary(t => t.Id, t => t.TotalProcessorTime.TotalSeconds);
        long initialPrivateBytes = process.PrivateMemorySize64;
        var initialCpu = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        // Repeated self-inspection distorts idle CPU. The external Measure-Idle.ps1
        // sampler is the acceptance authority and can observe memory throughout.
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        process.Refresh();
        var cpu = process.TotalProcessorTime - initialCpu;
        watch.Stop();
        var final = snapshot();
        bool stoppedAtBothEndpoints = !initial.Busy && initial.InputsSent == 0 && !final.Busy && final.InputsSent == 0;
        var finalActivity = activity();
        var report = new { scope = "Visible idle app; no explicit UI automation requests during sampling; external automation clients may still attach; no input generated",
            utc = DateTimeOffset.UtcNow, elapsedSeconds = watch.Elapsed.TotalSeconds,
            cpuSeconds = cpu.TotalSeconds, cpuPercentOfOneCore = 100 * cpu.TotalSeconds / watch.Elapsed.TotalSeconds,
            stoppedAtBothEndpoints, initialPrivateBytes, finalPrivateBytes = process.PrivateMemorySize64, memorySamples = 2, handles = process.HandleCount,
            mainWindowTopmost = System.Windows.Application.Current.MainWindow?.Topmost,
            statusRefreshes = finalActivity.Refreshes - initialActivity.Refreshes,
            overlayPlacements = finalActivity.Placements - initialActivity.Placements,
            threads = process.Threads.Cast<ProcessThread>().Select(t => new { id = t.Id,
                cpuSeconds = t.TotalProcessorTime.TotalSeconds - initialThreads.GetValueOrDefault(t.Id) }).OrderByDescending(t => t.cpuSeconds).ToArray(),
            logicalProcessors = Environment.ProcessorCount, runtime = Environment.Version.ToString(),
            passesIdleBudget = stoppedAtBothEndpoints && 100 * cpu.TotalSeconds / watch.Elapsed.TotalSeconds < 0.5 };
        await using var stream = new FileStream(Destination!, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true);
        await JsonSerializer.SerializeAsync(stream, report, new JsonSerializerOptions { WriteIndented = true });
    }
}
