using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace MinimumEffortClicker.App;

public partial class App : Application
{
    public Appearance Appearance { get; set; } = Appearance.System;
    // Kept alive for the app's lifetime: a collected registration stops handling its signal.
    private static readonly List<System.Runtime.InteropServices.PosixSignalRegistration> exitSignals = [];

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Compat.TrackShutdown();
        // Only recorded: a crash still ends the app, and the desktop helper lets go of anything it was holding.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception exception) AppLog.Crash("the app", exception); };
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var args = desktop.Args ?? [];
        if (args.Length == 2 && args[0] == "--startup-report") StartupMeasurement.Begin(args[1]);
        if (args.Length == 3 && args[0] == "--idle-report") IdleMeasurement.Begin(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        if (args.Length == 3 && args[0] == "--endurance-report") EnduranceMeasurement.Begin(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        AppTheme.Apply(Appearance);
        StartupMeasurement.Mark("Theme ready");
        if (PlatformSettings is { } settings) settings.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(() => AppTheme.Apply(Appearance));
        bool preview = args.Length == 2 && args[0] == "--render-preview";
        var window = new MainWindow(preview);
        desktop.MainWindow = window;
        if (preview) { Dispatcher.UIThread.Post(() => RenderPreviews(window, args[1], desktop), DispatcherPriority.Background); return; }
        Program.ShowRequested += () => Dispatcher.UIThread.Post(window.BringToFront);
        desktop.ShutdownRequested += (_, _) => window.PrepareSessionExit();
        foreach (var signal in new[] { System.Runtime.InteropServices.PosixSignal.SIGTERM, System.Runtime.InteropServices.PosixSignal.SIGHUP, System.Runtime.InteropServices.PosixSignal.SIGINT })
            exitSignals.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(signal, context => { context.Cancel = true; window.ExitOnSignal(); }));
        window.Show();
        StartupMeasurement.Mark("Window shown");
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Renders the main window to PNGs in both themes and at the minimum size, then exits.</summary>
    private void RenderPreviews(MainWindow window, string directory, IClassicDesktopStyleApplicationLifetime desktop)
    {
        Directory.CreateDirectory(directory);
        var content = (Control)window.Content!;
        foreach (var theme in new[] { Appearance.Dark, Appearance.Light })
        foreach (var size in new[] { (Name: "normal", Width: 1030, Height: 650, Scale: 1d), (Name: "minimum", Width: 900, Height: 600, Scale: 1d), (Name: "minimum-200", Width: 900, Height: 600, Scale: 2d) })
        foreach (var page in new[] { (Name: "actions", Index: 0), (Name: "toggles", Index: 4), (Name: "controller-to-key", Index: 7), (Name: "screenshot", Index: 6), (Name: "settings", Index: 3) })
        {
            AppTheme.Apply(theme);
            if (content is Panel root) root.Background = (Avalonia.Media.IBrush)Resources["AppBackground"]!;
            window.ShowPreviewPage(page.Index);
            window.Width = size.Width; window.Height = size.Height;
            window.Measure(new Size(size.Width, size.Height));
            window.Arrange(new Rect(0, 0, size.Width, size.Height));
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(size.Width * size.Scale), (int)(size.Height * size.Scale)), new Vector(96 * size.Scale, 96 * size.Scale));
            bitmap.Render(content);
            bitmap.Save(Path.Combine(directory, $"shell-{theme.ToString().ToLowerInvariant()}-{size.Name}-{page.Name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        desktop.Shutdown();
    }
}
