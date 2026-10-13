using System.Windows;

namespace MinimumEffortClicker.TestTarget;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Length == 2 && e.Args[0] == "--probe-pipe" && e.Args[1].StartsWith("MEC-Probe-", StringComparison.Ordinal))
        {
            window.Width = 800;
            window.Height = 650;
            window.Left = 70;
            window.Top = 45;
            var endpoint = new ProbeEndpoint(window, e.Args[1]);
            window.Closed += (_, _) => endpoint.Dispose();
        }
        if (e.Args.Length == 1 && e.Args[0] == "--desktop-replay-receiver") window.EnableDesktopReplaySurface();
        window.Show();
    }
}
