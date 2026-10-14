using System.IO;

namespace MinimumEffortClicker.App;

/// <summary>
/// A local record of what went wrong, in <c>log.txt</c> inside the data folder, so a failure can be looked at afterwards.
/// It is never sent anywhere. It holds about one megabyte; the previous megabyte is kept as <c>log.old.txt</c>.
/// Logging never throws: a full disk or a locked file must not turn into a second failure.
/// </summary>
internal static class AppLog
{
    private const long MaximumBytes = 1_000_000;
    private static readonly object gate = new();
    private static string? path;

    public static string? FilePath => path;

    public static void Initialize(string dataDirectory)
    {
        lock (gate) path = Path.Combine(dataDirectory, "log.txt");
        Note("Start", $"Minimum Effort Clicker {typeof(AppLog).Assembly.GetName().Version} on {Environment.OSVersion.VersionString}.");
    }

    /// <summary>Something the user tried did not work; the message is what they were shown.</summary>
    public static void Failure(string action, Exception exception) => Write($"{action} failed: {exception.Message} ({exception.GetType().Name})");

    /// <summary>An exception nobody expected: keep the whole trace.</summary>
    public static void Crash(string where, Exception exception) => Write($"Unexpected error in {where}:{Environment.NewLine}{exception}");

    public static void Note(string action, string message) => Write($"{action}: {message}");

    private static string? lastRunProblem;
    private static bool wasBusy;
    /// <summary>
    /// Records why every run ended, however it ended, plus a run that did not start, could not continue, needs cleanup,
    /// or was paused or stopped by a safety rule. The status refreshes many times a second, so only a change is written.
    /// </summary>
    public static void RunState(MinimumEffortClicker.Windows.RuntimeSnapshot snapshot)
    {
        bool problem = snapshot.Failed || snapshot.Reason.StartsWith("Stopped:", StringComparison.Ordinal)
            || snapshot.Reason.StartsWith("Paused:", StringComparison.Ordinal);
        string? key = problem ? snapshot.State + " | " + snapshot.Reason : null;
        bool ended;
        lock (gate)
        {
            ended = wasBusy && !snapshot.Busy;
            wasBusy = snapshot.Busy;
            if (key == lastRunProblem && !ended) return;
            lastRunProblem = key;
        }
        if (ended) Note($"Run ended ({snapshot.Activity})", $"{snapshot.State}: {snapshot.Reason}");
        else if (key is not null) Note($"Run ({snapshot.Activity})", $"{snapshot.State}: {snapshot.Reason}");
    }

    private static void Write(string text)
    {
        lock (gate)
        {
            if (path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaximumBytes)
                    File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, "log.old.txt"), overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {text}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
