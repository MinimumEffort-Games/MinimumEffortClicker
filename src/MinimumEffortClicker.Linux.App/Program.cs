using System.Net.Sockets;
using System.Text;
using Avalonia;

namespace MinimumEffortClicker.App;

internal static class Program
{
    private static Socket? instance;
    public static event Action? ShowRequested;

    [STAThread]
    public static int Main(string[] args)
    {
        bool preview = args.Length == 2 && args[0] == "--render-preview";
        if (!preview && !AcquireInstance()) return 0;
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose); }
        finally { ReleaseInstance(); }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static string SocketPath => Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime ? runtime : Path.GetTempPath(),
        $"minimum-effort-clicker-{Environment.UserName}.sock");

    /// <summary>One app per desktop session. A second launch asks the first to show its window.</summary>
    private static bool AcquireInstance()
    {
        var endpoint = new UnixDomainSocketEndPoint(SocketPath);
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(endpoint);
            client.Send(Encoding.ASCII.GetBytes("show"));
            return false;
        }
        catch (SocketException) { File.Delete(SocketPath); } // No listener: a stale socket from a crash.
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

    private static void ReleaseInstance()
    {
        if (instance is null) return;
        instance.Dispose();
        try { File.Delete(SocketPath); } catch (IOException) { }
    }
}
