using System.Net.Sockets;
using System.Text;
using Avalonia;

namespace MinimumEffortClicker.App;

internal static class Program
{
    private static Socket? instance;
    private static FileStream? instanceLock;
    public static event Action? ShowRequested;

    [STAThread]
    public static int Main(string[] args)
    {
        // On KDE the app starts a second copy of itself as the input guard; it never opens a window.
        if (MinimumEffortClicker.Linux.Kde.KdeInputGuard.RunIfRequested(args) is { } guardExit) return guardExit;
        bool preview = args.Length == 2 && args[0] == "--render-preview";
        if (!preview && !AcquireInstance()) return 0;
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose); }
        finally { ReleaseInstance(); }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static string SocketPath => Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime ? runtime : Path.GetTempPath(),
        $"minimum-effort-clicker-{Environment.UserName}.sock");

    /// <summary>
    /// One app per desktop session, however fast the icon is clicked. The first launch takes an exclusive lock on a file
    /// at once (.NET on Linux locks a FileShare.None file with flock, which the system drops if the app dies); a later
    /// launch that cannot take it waits for the first one's socket and asks it to show its window, then closes. Before
    /// the lock, a launch during the first one's start-up found no socket yet and opened a second app.
    /// </summary>
    private static bool AcquireInstance()
    {
        try { instanceLock = new FileStream(SocketPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { ShowRunningInstance(); return false; }
        catch (UnauthorizedAccessException) { ShowRunningInstance(); return false; }
        var endpoint = new UnixDomainSocketEndPoint(SocketPath);
        try { File.Delete(SocketPath); } catch (IOException) { } // We hold the lock, so any socket left is stale.
        instance = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        instance.Bind(endpoint);
        instance.Listen(4);
        new Thread(() =>
        {
            while (true)
            {
                try { using var peer = instance.Accept(); ShowRequested?.Invoke(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
            }
        }) { IsBackground = true, Name = "MEC single instance" }.Start();
        return true;
    }

    /// <summary>Asks the running app to show its window; while it is still starting, its socket may take a few seconds to
    /// appear.</summary>
    private static void ShowRunningInstance()
    {
        var endpoint = new UnixDomainSocketEndPoint(SocketPath);
        for (var waited = System.Diagnostics.Stopwatch.StartNew(); waited.Elapsed < ShowWait; Thread.Sleep(200))
        {
            try
            {
                using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                client.Connect(endpoint);
                client.Send(Encoding.ASCII.GetBytes("show"));
                return;
            }
            catch (SocketException) { } // Not listening yet.
        }
    }
    private static readonly TimeSpan ShowWait = TimeSpan.FromSeconds(10);

    private static void ReleaseInstance()
    {
        if (instance is not null)
        {
            instance.Dispose();
            try { File.Delete(SocketPath); } catch (IOException) { }
        }
        // The lock file stays: deleting it could let a launch starting right now lock a different file.
        instanceLock?.Dispose();
    }
}
