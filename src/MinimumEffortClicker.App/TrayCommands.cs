using System.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private TrayIcon? trayIcon;
    private bool exitRequested, trayNoticeShown, startupLoaded, traySettingsLoaded;
    private bool trayHidePending;
    private TrayNoticeWindow? trayNotice;
    // Coming back from minimised or the tray returns to the size the user had, maximised included.
    private WindowState shownState = WindowState.Normal;

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState != WindowState.Minimized) shownState = WindowState;
        base.OnStateChanged(e);
    }

    private void InitializeTray()
    {
        try
        {
            trayIcon = new(
                () => Dispatcher.BeginInvoke(RestoreFromTray),
                () => { CancelScreenshot("Stopped from notification area"); runtime?.Stop("Stopped from the notification area."); },
                () => Dispatcher.BeginInvoke(ExitApplication));
        }
        catch (Exception ex)
        {
            ReasonLabel.Text = "Notification icon unavailable. Closing will exit the app. " + ex.Message;
        }
    }

    internal void RestoreFromTray()
    {
        if (allowClose || closing || operations.Has(Operation.Screenshot) || Dispatcher.HasShutdownStarted) return;
        if (trayNotice is not null) { trayNotice.Activate(); return; }
        if (trayHidePending) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = shownState;
        Activate();
        RefreshStatus();
    }

    private bool TryHideToTray()
    {
        if (exitRequested || trayIcon is null || CloseToTrayBox.IsChecked != true) return false;
        if (trayHidePending) return true;
        if (!traySettingsLoaded || operations.Has(Operation.Screenshot) || operations.Has(Operation.Quick))
        {
            ReasonLabel.Text = !traySettingsLoaded ? "Wait for settings to finish loading."
                : "Finish recording, testing, or adjusting the overlay before hiding the window.";
            return true;
        }
        trayHidePending = true;
        // A modal dialog pumps dispatcher messages. Start it only after the owner's
        // Closing event has returned, so a tray/second-instance restore cannot Show
        // a window while WPF is still closing it.
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (exitRequested || allowClose || closing) return;
                if (!trayNoticeShown)
                {
                    var notice = new TrayNoticeWindow { Owner = this };
                    trayNotice = notice;
                    try { trayNotice.ShowDialog(); }
                    finally { trayNotice = null; }
                    trayNoticeShown = true;
                    if (notice.DisableRequested)
                    {
                        CloseToTrayBox.IsChecked = false;
                        PersistPreferences();
                        ExitApplication(); // Honour the original close through normal cleanup/save handling.
                        return;
                    }
                    PersistPreferences();
                }
                if (exitRequested || allowClose || closing) return;
                Hide();
                RefreshStatus();
            }
            finally { trayHidePending = false; }
        });
        return true;
    }

    private void CloseToTray_Changed(object sender, RoutedEventArgs e)
    {
        if (initialized) PersistPreferences();
    }

    /// <summary>Keep on top (User, 2026-10-18): the clicker stays in front of other windows. Start, tests and recordings
    /// still minimise it when they are set to.</summary>
    private void KeepOnTop_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = KeepOnTopBox.IsChecked == true;
        if (initialized) PersistPreferences();
    }

    private void ExitApplication()
    {
        if (allowClose || closing) return;
        exitRequested = true;
        Close(); // Uses the existing bounded stop, input-release, and save-completion path.
    }


    internal void PrepareSessionExit()
    {
        exitRequested = true;
        runtime?.Stop("Stopped: Windows is ending this session.");
        screenshotCancellation?.Cancel();
        quickCancellation?.Cancel();
        quickRecorder?.Stop();
        CancelPowerSchedule();
    }
}
