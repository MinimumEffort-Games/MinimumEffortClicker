using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private readonly ShutdownSchedule shutdownSchedule = new();
    private readonly object powerGate = new();
    private readonly Stopwatch powerClock = Stopwatch.StartNew();
    private int appliedClickLimit = 20;
    private bool appliedKeepAwake;
    private Window? shutdownNotice;
    private TextBlock? shutdownNoticeText;
    private bool closingShutdownNotice;

    private void ApplyKeepAwake()
    {
        if (runtime is null) return;
        appliedKeepAwake = KeepAwakeBox.IsChecked == true;
        runtime.KeepAwakeWhileRunning = appliedKeepAwake;
    }
    private void KeepAwake_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        ApplyKeepAwake(); PersistPreferences();
    }
    private void ShutdownMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ShutdownValueLabel is null || ShutdownTimeFields is null || ArmShutdownButton is null) return;
        int mode = ShutdownModeBox.SelectedIndex;
        ShutdownNumberFields.Visibility = mode is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        ShutdownTimeFields.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
        ArmShutdownButton.Visibility = mode > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShutdownValueLabel.Text = mode == 2 ? "Minutes running" : "Loops";
        ShutdownValueBox.Text = mode == 2 ? "60" : "100";
        var time = DateTime.Now.AddHours(1);
        ShutdownHourBox.Text = time.ToString("HH", CultureInfo.InvariantCulture);
        ShutdownMinuteBox.Text = time.ToString("mm", CultureInfo.InvariantCulture);
    }
    private void ArmShutdown_Click(object sender, RoutedEventArgs e)
    {
        if (shutdownSchedule.Armed) { CancelPowerSchedule(); RefreshStatus(); return; }
        if (runtime is null || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending || ShutdownModeBox.SelectedIndex < 1) return;
        try
        {
            var now = DateTimeOffset.Now;
            ShutdownRequest request;
            string description;
            if (ShutdownModeBox.SelectedIndex == 1)
            {
                request = new(ShutdownTrigger.CompletedLoops, Loops: long.Parse(ShutdownValueBox.Text, CultureInfo.CurrentCulture));
                description = $"After {request.Loops:N0} completed Action List loops across future runs.";
            }
            else if (ShutdownModeBox.SelectedIndex == 2)
            {
                request = new(ShutdownTrigger.ActiveRunTime, ActiveTime: TimeSpan.FromMinutes(Number(ShutdownValueBox)));
                description = $"After {request.ActiveTime.TotalMinutes:0.###} minutes of active playback across future runs. Idle time, recording and startup countdowns do not count.";
            }
            else
            {
                if (!TimeOnly.TryParseExact($"{ShutdownHourBox.Text}:{ShutdownMinuteBox.Text}", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    throw new ArgumentException("Enter a local time as HH:mm, for example 23:30.");
                var local = DateTime.SpecifyKind(DateTime.Today.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
                if (local <= now.LocalDateTime) local = local.AddDays(1);
                if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
                    throw new ArgumentException("That time falls in a daylight-saving clock change. Choose an unambiguous local time.");
                request = new(ShutdownTrigger.SystemTime, At: new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)));
                description = $"At {request.At.LocalDateTime:f} ({request.At:zzz}), even if playback is idle.";
            }
            request.Validate(now);
            if (AppDialog.Show(this, $"Shut down this PC?\n\n{description}\n\nPlayback stops and releases its inputs first. A 30-second countdown lets you cancel. Stop All cancels this plan. Save your work; Windows may ask open apps to close.\n\nThis plan is forgotten when the app closes.",
                "Review automatic shutdown", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            var snapshot = runtime.Snapshot;
            if (snapshot.Busy || snapshot.CleanupPending) throw new InvalidOperationException("Stop playback and finish cleanup before arming shutdown.");
            lock (powerGate)
            {
                shutdownSchedule.Arm(request, DateTimeOffset.Now, snapshot.TotalCompletedLoops, snapshot.TotalActiveTime, snapshot.TotalCompletedClicks);
                runtime.CompletionLimit = request.Trigger == ShutdownTrigger.CompletedLoops ? checked(snapshot.TotalCompletedLoops + request.Loops) : long.MaxValue;
                LoopBox.IsChecked = true;
                runtime.ActiveTimeLimit = request.Trigger == ShutdownTrigger.ActiveRunTime ? snapshot.TotalActiveTime + request.ActiveTime : TimeSpan.MaxValue;
            }
            RefreshStatus();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or InvalidOperationException)
        { ReasonLabel.Text = ex.Message; }
    }

    // May run on the native stop thread. It never waits for the WPF dispatcher or sends OS input.
    private void CancelPowerSchedule()
    {
        lock (powerGate)
        {
            shutdownSchedule.Cancel();
            if (runtime is { } active) { active.CompletionLimit = long.MaxValue; active.ClickCompletionLimit = long.MaxValue; active.ActiveTimeLimit = TimeSpan.MaxValue; active.ShutdownPending = false; }
        }
        RuntimeChanged();
    }
    private void CancelShutdown_Click(object sender, RoutedEventArgs e) { CancelPowerSchedule(); RefreshStatus(); }

    private void RefreshPower(RuntimeSnapshot snapshot)
    {
        if (runtime is null) return;
        var status = shutdownSchedule.Update(DateTimeOffset.Now, powerClock.Elapsed, snapshot.TotalCompletedLoops, snapshot.TotalActiveTime, snapshot.TotalCompletedClicks);
        KeepAwakeBox.IsEnabled = !snapshot.Busy && !quickOperation;
        bool editable = !snapshot.Busy && !snapshot.CleanupPending && !quickOperation && !macroFileBusy && !ioBusy && !status.Armed;
        ShutdownModeBox.IsEnabled = ShutdownNumberFields.IsEnabled = ShutdownTimeFields.IsEnabled = editable;
        ArmShutdownButton.IsEnabled = status.Armed || editable;
        ArmShutdownButton.Content = status.Armed ? "Cancel Shutdown" : "Enable shutdown…";
        ArmShutdownButton.SetResourceReference(FrameworkElement.StyleProperty, status.Armed ? "RedButton" : "GreenButton");
        PowerBanner.Text = status.Message; PowerStatusLabel.Text = status.Armed ? status.Message : "";
        PowerBanner.Visibility = status.Armed ? Visibility.Visible : Visibility.Collapsed;
        if (!status.CountingDown) { runtime.ShutdownPending = false; CloseShutdownNotice(); return; }
        runtime.ShutdownPending = true;
        if (snapshot.CleanupPending)
        {
            CancelPowerSchedule();
            shutdownSchedule.Cancel("Shutdown cancelled because input cleanup needs attention.");
            return;
        }
        if (snapshot.Busy) { runtime.StopForShutdown(); return; }
        if (shutdownNotice is null)
        {
            shutdownNoticeText = new TextBlock { Text = status.Message, TextWrapping = TextWrapping.Wrap, FontSize = 18, Margin = new Thickness(0, 0, 0, 20) };
            var cancel = new Button { Content = "Cancel shutdown", Padding = new Thickness(18, 10, 18, 10), IsCancel = true };
            cancel.Click += CancelShutdown_Click;
            var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(shutdownNoticeText); panel.Children.Add(cancel);
            shutdownNotice = new Window { Title = "Minimum Effort · PC shutdown", Width = 440, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, Topmost = true, ShowActivated = false, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            shutdownNotice.Closed += (_, _) => { if (!closingShutdownNotice) CancelPowerSchedule(); };
            shutdownNotice.Show();
            shutdownSchedule.BeginCountdown(powerClock.Elapsed);
        }
        shutdownNoticeText!.Text = status.Message;
        try
        {
            if (shutdownSchedule.TryCommit(powerClock.Elapsed, !snapshot.Busy && !snapshot.CleanupPending && !preferencesBusy && !ioBusy && !bindingBusy, PcPower.RequestShutdown))
            {
                runtime.ShutdownPending = false; runtime.CompletionLimit = long.MaxValue; runtime.ClickCompletionLimit = long.MaxValue; runtime.ActiveTimeLimit = TimeSpan.MaxValue;
                CloseShutdownNotice();
            }
        }
        catch (Exception ex)
        {
            CancelPowerSchedule(); shutdownSchedule.Cancel("Windows could not start shutdown: " + ex.Message); CloseShutdownNotice();
        }
    }

    private void CloseShutdownNotice()
    {
        if (shutdownNotice is null) return;
        closingShutdownNotice = true;
        shutdownNotice.Close(); shutdownNotice = null; shutdownNoticeText = null;
        closingShutdownNotice = false;
    }
}
