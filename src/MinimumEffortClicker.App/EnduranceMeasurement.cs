using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

// Opt-in VM diagnostic. Observes an explicitly started, input-free Actions loop.
// Never loads a macro, starts playback, synthesizes input or registers new hooks.
internal static class EnduranceMeasurement
{
    internal static string? Destination { get; private set; }
    private static int seconds;
    internal static void Begin(string path, int duration)
    {
        if (duration is < 60 or > 28800) throw new ArgumentOutOfRangeException(nameof(duration));
        Destination = Path.GetFullPath(path); seconds = duration;
        if (File.Exists(Destination) || File.Exists(Destination + ".progress.json"))
            throw new IOException("Choose a fresh endurance report path.");
    }

    internal static async Task CompleteAsync(Func<RuntimeSnapshot> snapshot, Action stop)
    {
        var waiting = Stopwatch.StartNew();
        var watch = new Stopwatch();
        DateTimeOffset? startedUtc = null;
        string? failure = null;
        long firstLoops = 0, previousLoops = 0;
        int samples = 0, stalled = 0;
        using var process = Process.GetCurrentProcess();
        RuntimeSnapshot current = snapshot();
        try
        {
            while (!current.PlaybackActive && waiting.Elapsed.TotalMinutes < 10)
            {
                await WriteAsync(Destination! + ".progress.json", new { stage = "Awaiting explicit Start", utc = DateTimeOffset.UtcNow, requestedSeconds = seconds });
                await Task.Delay(TimeSpan.FromSeconds(2));
                current = snapshot();
                if (current.InputsSent != 0 || current.CleanupPending)
                    throw new InvalidOperationException("Endurance diagnostic requires input-free playback and no pending cleanup.");
            }
            if (!current.PlaybackActive) throw new InvalidOperationException("No explicit playback start within ten minutes.");
            startedUtc = DateTimeOffset.UtcNow; watch.Start();
            firstLoops = previousLoops = current.TotalCompletedLoops;
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                var heartbeat = watch.Elapsed;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, seconds - watch.Elapsed.TotalSeconds)));
                if (watch.Elapsed - heartbeat > TimeSpan.FromSeconds(60))
                    throw new InvalidOperationException("UI endurance heartbeat delayed for more than sixty seconds.");
                current = snapshot(); samples++;
                process.Refresh();
                if (process.PrivateMemorySize64 > 256L * 1024 * 1024 || process.HandleCount > 2000)
                    throw new InvalidOperationException("Endurance resource bound exceeded (256 MiB private memory / 2000 handles).");
                if (!current.PlaybackActive || !current.ActionsActive || current.InputsSent != 0 || current.CleanupPending)
                    throw new InvalidOperationException("Input-free Actions playback stopped, paused or changed during endurance sampling.");
                stalled = current.TotalCompletedLoops > previousLoops ? 0 : stalled + 1;
                if (stalled >= 2) throw new InvalidOperationException("No completed loop for sixty seconds; check the UI and scheduler.");
                previousLoops = current.TotalCompletedLoops;
                await WriteAsync(Destination! + ".progress.json", new { stage = "Running", utc = DateTimeOffset.UtcNow,
                    startedUtc, requestedSeconds = seconds, elapsedSeconds = watch.Elapsed.TotalSeconds,
                    completedLoops = current.TotalCompletedLoops - firstLoops, samples, current.InputsSent, current.State });
            }
        }
        catch (Exception ex) { failure = ex.Message; }
        finally
        {
            watch.Stop();
            stop(); // Stop only: completion or diagnostic failure never starts/resumes.
        }
        for (int attempt = 0; attempt < 100 && snapshot().Busy; attempt++) await Task.Delay(50);
        var final = snapshot();
        bool passed = failure is null && watch.Elapsed.TotalSeconds >= seconds && !final.Busy && !final.CleanupPending && final.InputsSent == 0;
        await WriteAsync(Destination!, new { scope = "Full WPF app, native controls, overlay and scheduler; explicit input-free pause loop. Excludes native input/capture endurance.",
            passed, failure, startedUtc, finishedUtc = DateTimeOffset.UtcNow, requestedSeconds = seconds,
            elapsedSeconds = watch.Elapsed.TotalSeconds, completedLoops = current.TotalCompletedLoops - firstLoops,
            samples, final.InputsSent, final.Busy, final.CleanupPending, runtime = Environment.Version.ToString() });
    }

    private static async Task WriteAsync(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
