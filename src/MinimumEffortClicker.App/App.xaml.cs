using System.Windows;

namespace MinimumEffortClicker.App;
public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? showWindow;
    private RegisteredWaitHandle? showWindowWait;
    public Appearance Appearance { get; set; } = Appearance.System;
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--startup-report") StartupMeasurement.Begin(e.Args[1]);
        if (e.Args.Length == 3 && e.Args[0] == "--idle-report") IdleMeasurement.Begin(e.Args[1], int.Parse(e.Args[2], System.Globalization.CultureInfo.InvariantCulture));
        if (e.Args.Length == 3 && e.Args[0] == "--endurance-report") EnduranceMeasurement.Begin(e.Args[1], int.Parse(e.Args[2], System.Globalization.CultureInfo.InvariantCulture));
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        // An unhandled exception must never end the process while a key or button is still held down.
        DispatcherUnhandledException += (_, args) =>
        {
            (MainWindow as MainWindow)?.EmergencyStop();
            args.Handled = true;
            AppLog.Crash("the app", args.Exception);
            try { AppDialog.Show("Something went wrong and playback was stopped: " + args.Exception.Message + "\n\nSave your macro and restart the app if it misbehaves."); }
            catch (Exception) { /* Reporting must not raise a second failure. */ }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            (MainWindow as MainWindow)?.EmergencyStop();
            if (args.ExceptionObject is Exception exception) AppLog.Crash("a background thread", exception);
        };
        AppTheme.Apply(Appearance);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => AppTheme.ApplyWindowChrome((Window)sender)));
        StartupMeasurement.Mark("Theme ready");
        SystemParameters.StaticPropertyChanged += OnSystemChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnPreferencesChanged;
        bool preview = e.Args.Length == 2 && e.Args[0] == "--render-preview";
        if (!preview)
        {
            instance = new Mutex(false, @"Local\MinimumEffortClicker.App");
            bool acquired;
            try { acquired = instance.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
            {
                instance.Dispose(); instance = null;
                try { using var signal = EventWaitHandle.OpenExisting(@"Local\MinimumEffortClicker.ShowWindow"); signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { AppDialog.Show("Minimum Effort Clicker is already opening. Try again shortly."); }
                Shutdown(); return;
            }
        }
        var window = new MainWindow(preview);
        MainWindow = window;
        if (!preview)
        {
            showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MinimumEffortClicker.ShowWindow");
            showWindowWait = ThreadPool.RegisterWaitForSingleObject(showWindow, (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(window.RestoreFromTray);
            }, null, Timeout.Infinite, false);
        }
        if (e.Args.Length == 2 && e.Args[0] == "--render-preview")
        {
            System.IO.Directory.CreateDirectory(e.Args[1]);
            foreach (var theme in new[] { Appearance.Dark, Appearance.Light })
            foreach (var size in new[] { (Name: "normal", Width: 1030, Height: 650, Scale: 1d), (Name: "minimum", Width: 844, Height: 580, Scale: 1d), (Name: "minimum-200", Width: 844, Height: 580, Scale: 2d) })
            {
                AppTheme.Apply(theme);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(size.Width, size.Height));
                content.Arrange(new Rect(0, 0, size.Width, size.Height));
                content.UpdateLayout();
                var visual = new System.Windows.Media.DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawRectangle((System.Windows.Media.Brush)Resources["AppBackground"], null, new Rect(0, 0, size.Width, size.Height));
                }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(size.Width * size.Scale), (int)(size.Height * size.Scale), 96 * size.Scale, 96 * size.Scale, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(visual);
                bitmap.Render(content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(e.Args[1], $"shell-{theme.ToString().ToLowerInvariant()}-{size.Name}.png"));
                encoder.Save(stream);
            }
            Shutdown();
            return;
        }
        window.Show();
        StartupMeasurement.Mark("Window shown");
    }
    private void OnSystemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshTheme();
    private void OnPreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) => RefreshTheme();
    private void RefreshTheme()
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => AppTheme.Apply(Appearance));
    }
    protected override void OnExit(ExitEventArgs e)
    {
        showWindowWait?.Unregister(null);
        showWindow?.Dispose();
        SystemParameters.StaticPropertyChanged -= OnSystemChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnPreferencesChanged;
        if (instance is not null) { instance.ReleaseMutex(); instance.Dispose(); }
        base.OnExit(e);
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        (MainWindow as MainWindow)?.PrepareSessionExit();
        base.OnSessionEnding(e);
    }
}
