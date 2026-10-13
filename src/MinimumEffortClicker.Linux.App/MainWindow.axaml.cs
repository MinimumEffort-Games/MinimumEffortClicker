using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using System.Globalization;
using Avalonia.Platform.Storage;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Storage;

namespace MinimumEffortClicker.App;

public partial class MainWindow : Window
{
    public MainWindow() : this(true) { }

    private DesktopRuntime? runtime;
    private readonly DispatcherTimer overlayFollowTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly HelperProfiles store;
    private readonly Preferences preferences;
    private UserPreferences? pendingPreferences;
    private bool preferencesBusy;
    private List<ProfileDefinition> profiles = [];
    private readonly Dictionary<Guid, MacroBinding> macroBindings = [];
    private readonly HashSet<Guid> enabledProfiles = [], enabledMacros = [];
    private bool selectingProfile, bindingBusy;
    private readonly LocalBindings bindingsStore;
    private StatusOverlay? overlay;
    private OverlayOptions overlayOptions = new();
    private bool initialized, closing, allowClose, ioBusy;
    private int refreshPending;
    private (string State, string Reason)? lastRuntimeStatus;
    private string stopKey = "F9";
    private int appliedToggleKey = 0x77, appliedStopKey = 0x78;
    private bool appliedHoldTrigger;

    public MainWindow(bool previewOnly = false)
    {
        store = new(File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode"))
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinimumEffortClicker"));
        preferences = new(store.DirectoryPath);
        bindingsStore = new(store.DirectoryPath);
        InitializeComponent();
        SizeChanged += (_, _) => UpdateActionsListSize();
        StartupMeasurement.Mark("Main XAML initialized");
        InitializeQuickBuilder();
        ToggleHeldKeyBox.Content = "W"; ToggleShortcutBox.Content = "Choose a key…";
        ToggleKeyBox.Content = "F8"; StopKeyBox.Content = "F9"; MacroKeyBox.Content = "Choose a key…";
        if (previewOnly) return;
        runtime = new DesktopRuntime();
        runtime.CaptureScreenshot = stop => Dispatcher.InvokeAsync(() => TakeScreenshotAsync(fromAction: true, stop));
        StartupMeasurement.Mark("Native controls and scheduler ready");
        runtime.Changed += RuntimeChanged;
        runtime.Control.RecordingRequested += QuickRecordHotkey;
        runtime.Control.ScreenshotRequested += ScreenshotHotkey;
        runtime.Control.StopRequested += CancelScreenshot;
        runtime.StopIssued += CancelPowerSchedule;
        runtime.QuickStartRequested += () =>
        {
            var intent = runtime.StopVersion;
            Dispatcher.BeginInvoke(() =>
            {
                if (!closing && !allowClose && !screenshotBusy && !macroFileBusy && !ioBusy && intent == runtime.StopVersion && initialized && !quickOperation && !quickEditorOpen && !bindingBusy && !preferencesBusy && DialogWindow.OpenCount == 0 && overlay?.IsEditing != true && quickValid && runtime.CanStartActions)
                    Run_Click(this, new RoutedEventArgs());
            });
        };
        sessionWatcher.SessionChanged += SessionChanged;
        sessionWatcher.PowerChanged += PowerChanged;
        if (Screens is { } screens) screens.Changed += DisplaysChanged;
        timer.Tick += (_, _) => RefreshStatus();
        overlayFollowTimer.Tick += (_, _) => PositionOverlay();
        initialized = true;
        Config_Changed(this, new RoutedEventArgs());
        RefreshStatus();
        PositionChanged += (_, _) => { if (runtime is not null && !runtime.HasSelectedTarget) RefreshStatus(); };
        SizeChanged += (_, _) => { if (runtime is not null && !runtime.HasSelectedTarget) RefreshStatus(); };
        Loaded += async (_, _) =>
        {
            // Hide/Show must preserve the current target, draft, and enabled shortcuts.
            if (startupLoaded) return;
            startupLoaded = true;
            Pages.IsEnabled = false;
            RefreshLinuxSetup();
            bool settingsLoaded = false;
            try
            {
            var loaded = await Task.Run(store.Load);
            StartupMeasurement.Mark("Profiles loaded");
            if (allowClose || closing) return;
            profiles = loaded.Profiles.ToList(); RefreshLibraryLists();
            if (profiles.Count == 0 && loaded.Warning is null)
            {
                var autorun = new MacroDefinition(Guid.NewGuid(), "Autorun",
                    new(InputControl.Key(SequenceKeys.Supported["W"]), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0), IsToggle: true);
                profiles.Add(new(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Toggles", [autorun]));
                await Task.Run(() => store.Save(profiles));
                RefreshLibraryLists();
            }
            foreach (var binding in await Task.Run(bindingsStore.Load)) macroBindings[binding.MacroId] = binding;
            RefreshToggleList();
            var settings = await Task.Run(preferences.Load);
            StartupMeasurement.Mark("Preferences loaded");
            if (allowClose || closing) return;
            initialized = false;
            LoadScreenshotSettings(settings.Screenshots);
            ThemeBox.SelectedIndex = settings.Appearance;
            ResizeClicksBox.SelectedIndex = settings.ScaleClickPositions ? 0 : 1;
            CloseToTrayBox.IsChecked = settings.CloseToTray;
            trayNoticeShown = settings.TrayNoticeShown;
            traySettingsLoaded = true;
            ((App)Application.Current!).Appearance = (Appearance)settings.Appearance;
            AppTheme.Apply((Appearance)settings.Appearance);
            selectedStartKey = settings.ToggleKey; selectedStopKey = settings.StopKey; ToggleKeyBox.Content = ShortcutKeys.Label(selectedStartKey); StopKeyBox.Content = ShortcutKeys.Label(selectedStopKey);
            OverlayBox.IsChecked = settings.Overlay;
            overlayOptions = settings.OverlayOptions ?? new();
            IdleOverlayBox.IsChecked = overlayOptions.KeepIdle;
            AnchorOverlayBox.IsChecked = overlayOptions.AnchorTarget;
            OverlayOpacity.Value = overlayOptions.Opacity; OverlayScale.Value = overlayOptions.Scale;
            Topmost = false; // The removed main-window topmost preference must not remain active invisibly.
            selectedRecordKey = settings.RecordKey; QuickRecordKeyBox.Content = ShortcutKeys.Label(selectedRecordKey);
            appliedClickLimit = 20; appliedKeepAwake = settings.KeepAwake;
            KeepAwakeBox.IsChecked = appliedKeepAwake;
            runtime.SetClickRateLimit(appliedClickLimit); runtime.KeepAwakeWhileRunning = appliedKeepAwake;
            await runtime.Control.ConfigureAsync(settings.ToggleKey, settings.StopKey);
            if (allowClose || closing) return;
            appliedToggleKey = settings.ToggleKey; appliedStopKey = settings.StopKey; appliedHoldTrigger = false;
            NewToggle_Click(this, new RoutedEventArgs());
            runtime.Control.HoldTrigger = false; stopKey = ShortcutKeys.Label(settings.StopKey);
            initialized = true; Config_Changed(this, new RoutedEventArgs()); RefreshStatus();
            if (loaded.Warning is not null) ReasonLabel.Text = loaded.Warning;
            RefreshTargetChoices();
            await SetQuickRecordKey();
            await MakeSavedMacrosAvailable();
            await ConfigureScreenshotHotkey();
            settingsLoaded = true;
            InitializeTray();
            }
            catch (Exception ex) { if (!allowClose) ReasonLabel.Text = "Startup settings could not be loaded: " + ex.Message; }
            finally
            {
                if (!allowClose)
                {
                    initialized = true; Pages.IsEnabled = true;
                    if (StartupMeasurement.Enabled)
                    {
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        await StartupMeasurement.CompleteAsync(runtime.Control.Available, !runtime.Snapshot.Busy && runtime.Snapshot.InputsSent == 0, settingsLoaded);
                    }
                    if (IdleMeasurement.Destination is not null)
                    {
                        try { await IdleMeasurement.CompleteAsync(() => runtime.Snapshot, () => (statusRefreshCount, overlayPlacementCount)); }
                        finally { ExitApplication(); }
                    }
                    if (EnduranceMeasurement.Destination is not null)
                    {
                        try { await EnduranceMeasurement.CompleteAsync(() => runtime.Snapshot, () => runtime.Stop("Endurance diagnostic finished.")); }
                        finally { ExitApplication(); }
                    }
                }
            }
        };
        Closed += (_, _) =>
        {
            trayIcon?.Dispose(); trayIcon = null;
            timer.Stop(); overlayFollowTimer.Stop(); sessionWatcher.Dispose();
            if (Screens is { } screens) screens.Changed -= DisplaysChanged;
            CancelPowerSchedule(); CloseShutdownNotice();
            runtime.StopIssued -= CancelPowerSchedule;
            runtime.Control.RecordingRequested -= QuickRecordHotkey;
            screenshotCancellation?.Cancel();
            runtime.Control.ScreenshotRequested -= ScreenshotHotkey;
            runtime.Control.StopRequested -= CancelScreenshot;
            runtime.Changed -= RuntimeChanged; runtime.Dispose(); overlay?.SetEditing(false); overlay?.Close();
        };
    }

    private readonly SessionWatcher sessionWatcher = new();
    private void SessionChanged() { CancelScreenshot("Session changed"); runtime?.Stop("Stopped: the desktop session was locked or switched."); }
    private void PowerChanged() { CancelScreenshot("Power changed"); runtime?.Stop("Stopped: the computer is going to sleep."); }
    private void RuntimeChanged()
    {
        if (Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref refreshPending, 1) != 0) return;
        Dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref refreshPending, 0); RefreshStatus(); });
    }
    private long statusRefreshCount, overlayPlacementCount;
    private void UpdateActionsListSize()
    {
        double maximum = Math.Clamp(Bounds.Height - 450, 150, 320);
        QuickActionList.MinHeight = Math.Min(200, maximum);
        QuickActionList.MaxHeight = maximum;
    }
    private void RefreshStatus()
    {
        if (runtime is null || allowClose) return;
        statusRefreshCount++;
        var snapshot = runtime.Snapshot;
        if (!snapshot.Busy && desktopPlaybackFrom is { } previous)
        {
            desktopPlaybackFrom = null;
            if (snapshot.State is "Could not continue" or "Not started" or "Cleanup pending")
                RestoreDesktopOperationWindow(previous);
        }
        TargetLabel.IsEnabled = ToggleTargetLabel.IsEnabled = !snapshot.Busy && !snapshot.CleanupPending
            && !quickOperation && !screenshotBusy && !macroFileBusy && !ioBusy && !bindingBusy && !closing;
        if (!runtime.IsDesktopMode && !runtime.HasSelectedTarget && runtime.SelectedWindow is { } unavailable
            && (TargetLabel.SelectedItem as TargetWindow)?.Title != unavailable.Title + " (unavailable)")
            RefreshTargetChoices();
        string targetBorder = runtime.HasSelectedTarget ? "SuccessBorder" : "StopBorder";
        TargetCard.SetResourceReference(Border.BorderBrushProperty, targetBorder);
        ToggleTargetCard.SetResourceReference(Border.BorderBrushProperty, targetBorder);
        RecordDiagnosticState(snapshot);
        RefreshPower(snapshot);
        Avalonia.Automation.AutomationProperties.SetHelpText(RunButton, snapshot.State);
        FailureDetailLabel.Visibility = snapshot.CleanupPending || snapshot.State is "Could not continue" or "Not started" or "Cleanup pending" ? Visibility.Visible : Visibility.Collapsed;
        FailureDetailLabel.Text = snapshot.Reason;
        PausePlaybackButton.IsEnabled = !quickOperation && (snapshot.PlaybackActive || runtime.CanResume);
        PausePlaybackButton.Content = runtime.CanResume ? "Resume" : "Pause";
        var status = (snapshot.State, snapshot.Reason);
        if (lastRuntimeStatus != status)
        {
            ReasonLabel.Text = snapshot.Reason;
            lastRuntimeStatus = status;
        }
        RefreshQuickAvailability(snapshot);
        CleanupButton.Visibility = snapshot.CleanupPending ? Visibility.Visible : Visibility.Collapsed;
        HotkeyStatus.Text = runtime.Control.Available ? "" : runtime.Control.Error;
        timer.Interval = TimeSpan.FromMilliseconds(snapshot.Busy ? 100 : 1000);
        if (snapshot.Busy && overlay?.IsEditing != true || shutdownSchedule.Armed) timer.Start(); else timer.Stop();
        bool capturing = snapshot.Busy && snapshot.State.StartsWith("Recording", StringComparison.Ordinal);
        if (!screenshotBusy && (capturing || overlay?.IsEditing == true || OverlayBox.IsChecked == true && (IsVisible || runtime.IsDesktopMode || runtime.HasSelectedTarget) && (IsVisible || snapshot.Busy || overlayOptions.KeepIdle || enabledToggles.Count > 0)))
        {
            EnsureOverlay();
            overlay!.Configure(!snapshot.Busy && !overlay.IsEditing ? overlayOptions with { Expanded = false } : overlayOptions);
            overlay.Update(snapshot, stopKey, MacroBindings.KeyLabel(appliedToggleKey), ToggleOverlayLines(snapshot), ShortcutKeys.Label(appliedRecordKey), !quickHistory.Current.Steps.Any());
            bool newlyShown = !overlay.IsVisible;
            if (newlyShown) overlay.Show();
            PositionOverlay(newlyShown);
            if (!overlay.IsEditing && runtime.HasSelectedTarget) overlayFollowTimer.Start();
            else overlayFollowTimer.Stop();
        }
        else { overlayFollowTimer.Stop(); overlay?.Hide(); }
    }

    private void PositionOverlay(bool newlyShown = false)
    {
        if (runtime is null || overlay is not { IsVisible: true } || overlay.IsEditing || allowClose) return;
        overlayPlacementCount++;
        // Locked overlays follow their input scope, never the clicker window.
        try
        {
            var frame = runtime.GetTargetFrame();
            overlay.PlaceAt(frame.X + frame.Width, frame.Y);
        }
        catch (InvalidOperationException) { overlay.Constrain(); }
    }

    private static double Number(TextBox box)
    {
        if (!double.TryParse(box.Text ?? "", NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value))
            throw new ArgumentException("Enter finite numeric timing values.");
        return value;
    }
    private static string Describe(HelperPlan plan)
    {
        string action = plan.Control.Kind switch
        {
            InputKind.ScanCode => (plan.Mode == HelperMode.Hold ? "Hold " : "Press ") + HelperPlan.SupportedKeys.First(x => x.Value == plan.Control.Code).Key,
            InputKind.Scroll => plan.Control.Code > 0 ? "Scroll up" : "Scroll down",
            InputKind.HorizontalScroll => plan.Control.Code > 0 ? "Scroll right" : "Scroll left",
            _ => ((PointerButton)plan.Control.Code) + " click"
        };
        string repeat = plan.ActiveDuration is { } duration ? $"for {duration.TotalSeconds:0.###} seconds"
            : plan.Count == 0 ? "until stopped" : plan.Count == 1 ? "once" : $"{plan.Count:N0} times";
        string interval = plan.MaximumInterval is { } maximum
            ? $"{plan.Interval.TotalMilliseconds:0.###}–{maximum.TotalMilliseconds:0.###} ms (random)"
            : $"{plan.Interval.TotalMilliseconds:0.###} ms";
        return plan.Mode == HelperMode.Hold ? $"{action} · {repeat}" : $"{action} · every {interval} · {repeat}";
    }
    private WindowState? desktopPlaybackFrom;
    private bool actionsStartPending;
    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is not null && (runtime.Snapshot.ActionsActive || actionsStartPending))
        {
            CancelScreenshot("Actions stopped."); quickCancellation?.Cancel(); runtime.StopActions(); return;
        }
        if (runtime is null || screenshotBusy || quickOperation || macroFileBusy || ioBusy) return;
        if (runtime.CanResume) { await ResumeActionsAsync(); return; }
        if (!TryQuickSequence(out var definition, out var plan, out var error)) { ReasonLabel.Text = error; return; }
        TestResultLabel.Text = "";
        WindowState? minimizedFrom = null;
        using var startCancellation = new CancellationTokenSource();
        quickOperation = true; actionsStartPending = true; quickCancellation = startCancellation;
        long startIntent = runtime.StopVersion;
        bool accepted = false;
        try
        {
            ApplyKeepAwake();
            if (runtime.IsDesktopMode)
            {
                var frame = runtime.GetTargetFrame(); plan!.ValidateDesktopMode(frame.Width, frame.Height, frame.Dpi);
                minimizedFrom = WindowState; WindowState = WindowState.Minimized;
            }
            if (!plan!.LeaveCursorFree && !runtime.BringTargetForward()) { ReasonLabel.Text = runtime.Snapshot.Reason; return; }
            // Allow the initiating mouse/key release and foreground handoff to settle BEFORE
            // sampling physical input. The old countdown ran after that sample.
            RefreshStatus();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(200, startCancellation.Token);
            if (runtime.IsDesktopMode)
            {
                // Windows' minimise animation can still cover a saved desktop point after
                // WPF reports Minimized. Let that handoff finish before the first edge.
                var frame = runtime.GetTargetFrame();
                var first = plan.Inputs.FirstOrDefault(i => i.Down && i.Control.Kind == InputKind.MouseButton);
                bool Covered()
                {
                    if (first?.Position is { } point)
                    {
                        var at = point.Resolve(frame.X, frame.Y, frame.Width, frame.Height, frame.Dpi);
                        return runtime.IsOwnPoint(at.X, at.Y, false);
                    }
                    return false;
                }
                for (int retry = 0; retry < 50 && Covered(); retry++) await Task.Delay(20, startCancellation.Token);
                if (Covered()) throw new InvalidOperationException("The first click location is still covered by ME Clicker. Move the clicker away or use Ping click location to check it.");
            }
            if (startIntent != runtime.StopVersion || allowClose || closing) return;
            accepted = runtime.TryStartQuick(plan!, definition!.Steps.Count, TimeSpan.Zero, ResizeClicksBox.SelectedIndex == 0);
            if (accepted) desktopPlaybackFrom = minimizedFrom;
            if (!accepted) { ReasonLabel.Text = runtime.Snapshot.Reason; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ReasonLabel.Text = ex.Message; }
        finally
        {
            quickCancellation = null; quickOperation = false; actionsStartPending = false;
            if (!accepted) RestoreDesktopOperationWindow(minimizedFrom);
            RefreshStatus();
        }
    }
    private void Stop_Click(object sender, RoutedEventArgs e) { screenshotCancellation?.Cancel(); quickCancellation?.Cancel(); quickRecorder?.Stop(); runtime?.Stop(); }
    private void StopActions_Click(object sender, RoutedEventArgs e) { if (quickOperation) Stop_Click(sender, e); else runtime?.StopActions(); }
    private void Cleanup_Click(object sender, RoutedEventArgs e) => runtime?.RetryCleanup();
    private void Overlay_Changed(object sender, RoutedEventArgs e) { if (initialized) { RefreshStatus(); PersistPreferences(); } }
    internal void ShowPreviewPage(int page) => Navigate(page);
    private void Navigation_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (Pages is null) return;
        if (Navigation.SelectedItem is ListBoxItem item && int.TryParse(item.Tag?.ToString(), out int page)) ShowPage(page);
    }
    private int currentPage;
    private void ShowPage(int page)
    {
        currentPage = page;
        for (int i = 0; i < Pages.Children.Count; i++) Pages.Children[i].Visibility = i == page ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Navigate(int page)
    {
        var tab = Navigation.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == page.ToString());
        if (tab is not null) Navigation.SelectedItem = tab;
        ShowPage(page);
    }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        var appearance = (Appearance)ThemeBox.SelectedIndex;
        ((App)Application.Current!).Appearance = appearance;
        AppTheme.Apply(appearance);
        PersistPreferences();
    }
    private void LightTheme_Click(object sender, RoutedEventArgs e) => ThemeBox.SelectedIndex = 2;
    private void DarkTheme_Click(object sender, RoutedEventArgs e) => ThemeBox.SelectedIndex = 1;
    private async void ApplyHotkeys_Click(object sender, RoutedEventArgs e) => await ApplyHotkeysAsync();
    private async Task ApplyHotkeysAsync()
    {
        if (runtime is null) return;
        if (bindingBusy) { ReasonLabel.Text = "Wait for the current binding change."; return; }
        if (runtime.Snapshot.Busy) { runtime.Stop("Stopped before changing global controls."); ReasonLabel.Text = "Wait for cleanup, then apply controls again."; return; }
        if (selectedStartKey == selectedStopKey || selectedStartKey == selectedRecordKey || selectedStopKey == selectedRecordKey
            || screenshotSettings.HotkeyEnabled && (selectedStartKey == screenshotSettings.Hotkey || selectedStopKey == screenshotSettings.Hotkey))
        {
            selectedStartKey = appliedToggleKey; selectedStopKey = appliedStopKey;
            ToggleKeyBox.Content = ShortcutKeys.Label(appliedToggleKey); StopKeyBox.Content = ShortcutKeys.Label(appliedStopKey);
            ReasonLabel.Text = "Each global control needs a different key. The previous key was kept."; return;
        }
        bindingBusy = true; Pages.IsEnabled = false;
        try
        {
            enabledProfiles.Clear(); enabledMacros.Clear(); enabledToggles.Clear(); RefreshToggleList();
            await runtime.ConfigureHelpersAsync([]);
            ProfileSelection_Changed(this, null!);
            bool success = await runtime.Control.ConfigureAsync(selectedStartKey, selectedStopKey);
            runtime.Control.HoldTrigger = false;
            stopKey = ShortcutKeys.Label(selectedStopKey);
            RefreshStatus();
            if (success)
            {
                appliedToggleKey = selectedStartKey;
                appliedStopKey = selectedStopKey;
                appliedHoldTrigger = runtime.Control.HoldTrigger;
                PersistPreferences();
            }
            await SetQuickRecordKey();
            await ConfigureScreenshotHotkey();
            if (!success)
            {
                string error = runtime.Control.Error ?? "That key is unavailable. The previous keys were restored.";
                selectedStartKey = appliedToggleKey; selectedStopKey = appliedStopKey;
                await runtime.Control.ConfigureAsync(appliedToggleKey, appliedStopKey);
                await SetQuickRecordKey(); await ConfigureScreenshotHotkey();
                ReasonLabel.Text = error;
            }
            ToggleKeyBox.Content = ShortcutKeys.Label(appliedToggleKey);
            StopKeyBox.Content = ShortcutKeys.Label(appliedStopKey);
            if (success) ReasonLabel.Text = "Keybinds saved.";
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
        finally { bindingBusy = false; Pages.IsEnabled = true; }
    }

    private void ResizeClicks_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        PersistPreferences();
        ReasonLabel.Text = "Resize setting saved. It applies the next time you start or test an action.";
    }
    private async void PersistPreferences()
    {
        if (!initialized) return;
        pendingPreferences = new(1, ThemeBox.SelectedIndex, appliedToggleKey,
            appliedStopKey, appliedHoldTrigger, OverlayBox.IsChecked == true, overlayOptions, false, appliedClickLimit, appliedKeepAwake, ResizeClicksBox.SelectedIndex == 0,
            CloseToTrayBox.IsChecked == true, trayNoticeShown, screenshotSettings, appliedRecordKey);
        if (preferencesBusy) return;
        preferencesBusy = true;
        try
        {
            while (pendingPreferences is { } next)
            {
                pendingPreferences = null;
                await Task.Run(() => preferences.Save(next));
            }
        }
        catch (Exception ex) { ReasonLabel.Text = "Preferences could not be saved: " + ex.Message; }
        finally { preferencesBusy = false; }
    }

    private async Task SaveProfiles(List<ProfileDefinition> updated)
    {
        if (ioBusy) throw new InvalidOperationException("A profile operation is already in progress.");
        ioBusy = true;
        try { await Task.Run(() => store.Save(updated)); profiles = updated; RefreshLibraryLists(); }
        finally { ioBusy = false; }
    }
    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ioBusy || macroFileBusy || quickOperation || runtime?.Snapshot.Busy == true) { MacroSaveStatus.Text = "Stop before saving the macro."; return; }
        string name = (ActionMacroNameBox.Text ?? "").Trim();
        if (name.Length == 0) { MacroSaveStatus.Text = "Input macro name."; ActionMacroNameBox.Focus(); return; }
        if (!TryQuickSequence(out var sequence, out _, out var error)) { ReasonLabel.Text = error; return; }
        macroFileBusy = true; RefreshStatus();
        string? exportedPath = null;
        try
        {
            var owner = profiles.FirstOrDefault(p => p.Id == loadedQuickProfile);
            var existing = owner?.Macros.FirstOrDefault(m => m.Id == loadedQuickMacro && m.Name == name)
                ?? loadedFileProfile?.Macros.FirstOrDefault(m => m.Id == loadedQuickMacro && m.Name == name);
            if (existing is null && profiles.SelectMany(p => p.Macros).Any(m => !m.IsToggle && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("That macro name already exists. Load it to edit, or choose a new name.");
            var macro = new MacroDefinition(existing?.Id ?? Guid.NewGuid(), name, null, existing?.Notes ?? "", sequence);
            ProfileDefinition saved;
            string path = existing is not null && loadedMacroFile is not null ? loadedMacroFile : MacroFiles.SavePath(MacroDirectory, macro);
            var file = existing is not null && loadedFileProfile is not null
                ? loadedFileProfile with { Points = quickPoints, Macros = loadedFileProfile.Macros.Select(m => m.Id == macro.Id ? macro : m).ToArray() }
                : new ProfileDefinition(ProfileDefinition.CurrentVersion, existing is not null ? owner?.Id ?? Guid.NewGuid() : Guid.NewGuid(), name, [macro], quickPoints);
            // Validate and atomically save the portable file before updating the compatibility library.
            await Task.Run(() => HelperProfiles.Export(path, file));
            exportedPath = path;
            if (existing is not null && owner is not null)
            {
                enabledMacros.Remove(existing.Id); await ApplyAvailability();
                saved = owner with { Points = quickPoints, Macros = owner.Macros.Any(m => m.Id == macro.Id)
                    ? owner.Macros.Select(m => m.Id == existing.Id ? macro : m).ToArray() : [.. owner.Macros, macro] };
                await SaveProfiles(profiles.Select(p => p.Id == owner.Id ? saved : p).ToList());
            }
            else
            {
                // A new folder preserves point identity/calibration without merging unrelated point libraries.
                saved = new(ProfileDefinition.CurrentVersion, file.Id, name, [macro], quickPoints);
                await SaveProfiles([.. profiles, saved]);
            }
            loadedQuickProfile = saved.Id; loadedQuickMacro = macro.Id;
            ProfileList.SelectedItem = saved; MacroList.SelectedItem = macro;
            loadedMacroFile = path; loadedFileProfile = file;
            MacroSaveStatus.Text = $"Saved ‘{name}’."; MacroSaveStatus.ToolTip = path;
            ReasonLabel.Text = "Macro saved. Use Load Macro to open it for editing or playback.";
        }
        catch (Exception ex) { MacroSaveStatus.Text = exportedPath is null ? "Could not save: " + ex.Message : $"File saved at {exportedPath}, but the local library update failed: {ex.Message}"; }
        finally { macroFileBusy = false; RefreshStatus(); }
    }
    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ProfileDefinition profile || MacroList.SelectedItem is not MacroDefinition macro) return;
        if (runtime?.Snapshot.Busy == true || quickOperation) { ReasonLabel.Text = "Stop the active run before loading a macro."; return; }
        try { LoadActionMacro(profile, macro); loadedMacroFile = null; loadedFileProfile = null; }
        catch (Exception ex) { loadingQuick = false; ReasonLabel.Text = "Could not load into this list: " + ex.Message; }
    }
    private void LoadActionMacro(ProfileDefinition profile, MacroDefinition macro)
    {
            var definition = macro.Sequence ?? SequenceCompiler.FromHelper(macro.Plan!);
            var history = new SequenceHistory(definition, profile.Points);
            quickPoints = profile.Points;
            quickHistory = history;
            var repetition = definition.Repetition ?? new();
            loadingQuick = true;
            RecordMovementBox.IsChecked = SequenceLibrary.AllSteps(definition).Any(s => s.Motion?.UsePreviousPause == true || s.Motion?.MoveDuration > TimeSpan.Zero);
            LoopBox.IsChecked = repetition.Count == 0 && repetition.ActiveDuration is null;
            loadingQuick = false;
            ProfileNameBox.Text = profile.Name; ActionMacroNameBox.Text = macro.Name; loadedQuickMacro = macro.Id; loadedQuickProfile = profile.Id; MacroSaveStatus.Text = "";
            Navigate(0); RefreshQuickList(); NewAction_Click(this, new RoutedEventArgs());
            ReasonLabel.Text = "Loaded. Press Start or the configured start / stop key when ready.";
    }
    private static readonly FilePickerFileType ProfileFiles = new("Minimum Effort profile") { Patterns = ["*.mecprofile"] };
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Import", FileTypeFilter = [ProfileFiles], AllowMultiple = false });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } fileName) return;
        try
        {
            var profile = await Task.Run(() => HelperProfiles.Import(fileName));
            if (AppDialog.Show(this, $"{profile.Name}\n\n{profile.Macros.Count} macros\n{string.Join("\n", profile.Macros.Take(8).Select(m => m.Name + ": " + DescribeMacro(m)))}\n\nImport disabled? No target or hotkeys will be assigned.", "Review import", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            await SaveProfiles([.. profiles, profile]); ProfileList.SelectedItem = profile;
            ReasonLabel.Text = "Imported disabled. Load into Actions to review and select your target.";
        }
        catch (Exception ex) { ReasonLabel.Text = "Import rejected: " + ex.Message; }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ProfileDefinition profile) return;
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Export", FileTypeChoices = [ProfileFiles], DefaultExtension = "mecprofile", SuggestedFileName = "helper.mecprofile" });
        if (file?.TryGetLocalPath() is not { } fileName) return;
        try { await Task.Run(() => HelperProfiles.Export(fileName, profile)); ReasonLabel.Text = "Exported actions, conditions, coordinates and any captured templates. No window binding, hotkeys or device state included."; }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
    }
    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ProfileDefinition profile || AppDialog.Show(this, $"Delete '{profile.Name}' and its {profile.Macros.Count} macros?", "Delete profile", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            enabledProfiles.Remove(profile.Id);
            foreach (var macro in profile.Macros) { enabledMacros.Remove(macro.Id); macroBindings.Remove(macro.Id); }
            await ApplyAvailability();
            await SaveProfiles(profiles.Where(p => p.Id != profile.Id).ToList()); ReasonLabel.Text = "Folder deleted.";
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
    }

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (allowClose || runtime is null) return;
        if (TryHideToTray()) { e.Cancel = true; return; }
        CancelPowerSchedule();
        screenshotCancellation?.Cancel();
        quickCancellation?.Cancel(); quickRecorder?.Stop();
        if (!screenshotBusy && !quickOperation && !runtime.Snapshot.Busy && !runtime.Snapshot.CleanupPending && !preferencesBusy && !ioBusy && !bindingBusy && !macroFileBusy) { allowClose = true; return; }
        e.Cancel = true;
        if (closing) return;
        closing = true; runtime.Stop("Application closing.");
        for (int i = 0; i < 60 && runtime.Snapshot.Busy; i++) await Task.Delay(50);
        for (int i = 0; i < 60 && (preferencesBusy || ioBusy || bindingBusy || quickOperation || macroFileBusy || screenshotBusy); i++) await Task.Delay(50);
        if (preferencesBusy || ioBusy || bindingBusy || quickOperation || macroFileBusy || screenshotBusy) { closing = false; exitRequested = false; RestoreFromTray(); ReasonLabel.Text = "A save, capture or binding update is still finishing. Try exiting again shortly."; return; }
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending)
        { closing = false; exitRequested = false; RestoreFromTray(); ReasonLabel.Text = "Release physical input and retry cleanup before exiting."; return; }
        allowClose = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        Close();
    }
}
