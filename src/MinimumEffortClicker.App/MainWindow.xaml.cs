using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Storage;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow : Window
{
    private DesktopRuntime? runtime;
    private readonly DispatcherTimer overlayFollowTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly HelperProfiles store;
    private readonly Preferences preferences;
    private UserPreferences? pendingPreferences;
    private List<ProfileDefinition> profiles = [];
    private readonly Dictionary<Guid, MacroBinding> macroBindings = [];
    private readonly LocalBindings bindingsStore;
    private StatusOverlay? overlay;
    private OverlayOptions overlayOptions = new();
    private bool initialized, closing, allowClose;
    private readonly OperationGate operations = new();
    private int refreshPending;
    private (string State, string Reason)? lastRuntimeStatus;
    private string stopKey = "F9";
    private int appliedToggleKey = ShortcutKeys.DefaultStartKey, appliedStopKey = ShortcutKeys.DefaultStopKey;
    private bool appliedHoldTrigger;

    public MainWindow(bool previewOnly = false)
    {
        store = new(File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode"))
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinimumEffortClicker"));
        preferences = new(store.DirectoryPath);
        bindingsStore = new(store.DirectoryPath);
        if (!previewOnly) AppLog.Initialize(store.DirectoryPath);
        InitializeComponent();
        PreviewMouseDown += EditingWindow_PreviewMouseDown;
        StartupMeasurement.Mark("Main XAML initialized");
        InitializeQuickBuilder();
        ToggleHeldKeyBox.Content = "W"; ToggleShortcutBox.Content = "Choose a key…";
        ToggleKeyBox.Content = "F8"; StopKeyBox.Content = "F9";
        if (previewOnly) return;
        runtime = new DesktopRuntime();
        InitializeWorkshop();
        runtime.CaptureScreenshot = stop => Dispatcher.InvokeAsync(() => TakeScreenshotAsync(fromAction: true, stop)).Task.Unwrap();
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
                if (!closing && !allowClose && initialized && operations.Idle && !operations.Any(Operation.Dialog | Operation.Preferences) && intent == runtime.StopVersion && !System.Windows.Interop.ComponentDispatcher.IsThreadModal && quickValid && runtime.CanStartActions)
                    Run_Click(this, new RoutedEventArgs());
            });
        };
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        timer.Tick += (_, _) => RefreshStatus();
        overlayFollowTimer.Tick += (_, _) => PositionOverlay();
        // Stays false until Loaded has read the saved preferences, so an early click cannot overwrite them with defaults.
        RefreshStatus();
        LocationChanged += (_, _) => { if (runtime is not null && !runtime.HasSelectedTarget) RefreshStatus(); };
        SizeChanged += (_, _) => { if (runtime is not null && !runtime.HasSelectedTarget) RefreshStatus(); };
        Loaded += async (_, _) =>
        {
            // Hide/Show must preserve the current target, draft, and enabled shortcuts.
            if (startupLoaded) return;
            startupLoaded = true;
            Pages.IsEnabled = false;
            // The tray icon exists from the start, so closing while settings load asks to wait instead of quitting.
            InitializeTray();
            bool settingsLoaded = false;
            try
            {
            var loaded = await Task.Run(store.Load);
            StartupMeasurement.Mark("Profiles loaded");
            if (allowClose || closing) return;
            profiles = loaded.Profiles.ToList(); RefreshToggleList();
            if (profiles.Count == 0 && loaded.Warning is null)
            {
                var autorun = new MacroDefinition(Guid.NewGuid(), "Autorun",
                    new(InputControl.Key(SequenceKeys.Supported["W"]), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0), IsToggle: true);
                profiles.Add(new(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Toggles", [autorun]));
                await Task.Run(() => store.Save(profiles));
                RefreshToggleList();
            }
            var toggleIds = profiles.SelectMany(p => p.Macros).Select(m => m.Id).ToHashSet();
            var storedBindings = await Task.Run(bindingsStore.Load);
            foreach (var binding in storedBindings.Where(b => toggleIds.Contains(b.MacroId))) macroBindings[binding.MacroId] = binding;
            // A binding whose toggle no longer exists would silently block its key forever.
            if (storedBindings.Count != macroBindings.Count) await Task.Run(() => bindingsStore.Save(macroBindings.Values.ToArray()));
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
            ((App)Application.Current).Appearance = (Appearance)settings.Appearance;
            AppTheme.Apply((Appearance)settings.Appearance);
            selectedStartKey = settings.ToggleKey; selectedStopKey = settings.StopKey; ToggleKeyBox.Content = ShortcutKeys.Label(selectedStartKey); StopKeyBox.Content = ShortcutKeys.Label(selectedStopKey);
            OverlayBox.IsChecked = settings.Overlay;
            overlayOptions = settings.OverlayOptions ?? new();
            IdleOverlayBox.IsChecked = overlayOptions.KeepIdle;
            OverlayOpacity.Value = overlayOptions.Opacity; OverlayScale.Value = overlayOptions.Scale;
            Topmost = false; // The removed main-window topmost preference must not remain active invisibly.
            selectedRecordKey = settings.RecordKey; QuickRecordKeyBox.Content = ShortcutKeys.Label(selectedRecordKey);
            EmergencyStopBox.SelectedIndex = (int)settings.EmergencyStop; runtime.EmergencyStop = settings.EmergencyStop; EmergencyStop_Changed(this, null!);
            appliedClickLimit = 20; appliedKeepAwake = settings.KeepAwake;
            KeepAwakeBox.IsChecked = appliedKeepAwake;
            var choices = settings.Actions ?? new();
            LoopBox.IsChecked = choices.Loop; MinimizeOnStartBox.IsChecked = choices.MinimizeOnStart; RecordDelaysBox.IsChecked = choices.RecordPauses;
            MinimizeOnRecordBox.IsChecked = choices.MinimizeOnRecord; RecordMovementBox.IsChecked = choices.SimulateMovement;
            // Set before anything can save preferences again, so an early save keeps the remembered toggles.
            rememberedToggles = settings.ActiveToggles ?? [];
            controllerControls = settings.Controller ?? ControllerControls.Default; ShowControllerButtons();
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
            await RestoreActiveToggles();
            StartControllers();
            SyncWorkshopMacros();
            await ConfigureScreenshotHotkey();
            settingsLoaded = true;
            }
            catch (Exception ex) { if (!allowClose) ReasonLabel.Text = "Startup settings could not be loaded: " + ex.Message; }
            finally
            {
                if (!allowClose)
                {
                    // If loading failed the defaults stand, and closing must still work.
                    initialized = true; Pages.IsEnabled = true; traySettingsLoaded = true;
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
            gamepads?.Dispose(); gamepads = null;
            timer.Stop(); overlayFollowTimer.Stop(); SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            CancelPowerSchedule(); CloseShutdownNotice();
            CloseWorkshop();
            if (runtime is null) return; // --render-preview never creates a runtime.
            runtime.StopIssued -= CancelPowerSchedule;
            runtime.Control.RecordingRequested -= QuickRecordHotkey;
            screenshotCancellation?.Cancel();
            runtime.Control.ScreenshotRequested -= ScreenshotHotkey;
            runtime.Control.StopRequested -= CancelScreenshot;
            runtime.Changed -= RuntimeChanged; runtime.Dispose(); overlay?.Close();
        };
    }

    /// <summary>Called from the unhandled-exception handlers; releases owned input without touching the UI.</summary>
    internal void EmergencyStop() => runtime?.EmergencyRelease();
    private void SessionChanged(object sender, SessionSwitchEventArgs e) { CancelScreenshot("Session changed"); runtime?.Stop("Stopped: Windows session changed."); }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { CancelScreenshot("Power changed"); runtime?.Stop("Stopped: Windows power state changed."); }
    private void RuntimeChanged()
    {
        if (Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref refreshPending, 1) != 0) return;
        Dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref refreshPending, 0); RefreshStatus(); });
    }
    private long statusRefreshCount, overlayPlacementCount;
    private void RefreshStatus()
    {
        if (runtime is null || allowClose) return;
        statusRefreshCount++;
        var snapshot = runtime.Snapshot;
        AppLog.RunState(snapshot);
        if (playbackAway is { } away)
        {
            if (snapshot.ActionsActive) playbackSeenActive = true;
            // User (2026-10-14/15): the clicker comes back when the Action List ends, however it ended, with any target.
            // A list that ends while a toggle keeps running leaves the clicker out of the way: the user is playing.
            if (!snapshot.Busy) { playbackAway = null; ComeBack(away); }
            else if (playbackSeenActive && !snapshot.ActionsActive) playbackAway = null;
        }
        TargetLabel.IsEnabled = ToggleTargetLabel.IsEnabled = !snapshot.Busy && !snapshot.CleanupPending
            && operations.Idle && !closing;
        if (!runtime.IsDesktopMode && !runtime.HasSelectedTarget && runtime.SelectedWindow is { } unavailable
            && (TargetLabel.SelectedItem as TargetWindow)?.Title != unavailable.Title + " (unavailable)")
            RefreshTargetChoices();
        string targetBorder = runtime.HasSelectedTarget ? "SuccessBorder" : "StopBorder";
        TargetCard.SetResourceReference(Border.BorderBrushProperty, targetBorder);
        ToggleTargetCard.SetResourceReference(Border.BorderBrushProperty, targetBorder);
        RefreshPower(snapshot);
        System.Windows.Automation.AutomationProperties.SetHelpText(RunButton, snapshot.State);
        FailureDetailLabel.Visibility = snapshot.CleanupPending || snapshot.Failed ? Visibility.Visible : Visibility.Collapsed;
        FailureDetailLabel.Text = snapshot.Reason;
        PausePlaybackButton.IsEnabled = !operations.Has(Operation.Quick) && (snapshot.PlaybackActive || runtime.CanResume);
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
        if (snapshot.Busy || shutdownSchedule.Armed) timer.Start(); else timer.Stop();
        if (!operations.Has(Operation.Screenshot) && (snapshot.Recording || OverlayBox.IsChecked == true && (IsVisible || runtime.IsDesktopMode || runtime.HasSelectedTarget) && (IsVisible || snapshot.Busy || overlayOptions.KeepIdle || enabledToggles.Count > 0)))
        {
            EnsureOverlay();
            overlay!.Configure(!snapshot.Busy ? overlayOptions with { Expanded = false } : overlayOptions);
            overlay.Update(snapshot, StopHint, StartHint, ToggleOverlayLines(snapshot), RecordHint, !quickHistory.Current.Steps.Any());
            bool newlyShown = !overlay.IsVisible;
            if (newlyShown) overlay.Show();
            PositionOverlay(newlyShown);
            if (runtime.HasSelectedTarget) overlayFollowTimer.Start();
            else overlayFollowTimer.Stop();
            runtime.OverlayHandle = new WindowInteropHelper(overlay).Handle;
        }
        else { overlayFollowTimer.Stop(); overlay?.Hide(); runtime.OverlayHandle = 0; }
    }

    private void PositionOverlay(bool newlyShown = false)
    {
        if (runtime is null || overlay is not { IsVisible: true } || allowClose) return;
        overlayPlacementCount++;
        // The overlay follows the Click target, never the clicker window.
        try
        {
            var frame = runtime.GetTargetFrame();
            overlay.PlaceAt(frame.X + frame.Width, frame.Y);
        }
        catch (InvalidOperationException) { overlay.Constrain(); }
    }

    private static double Number(TextBox box)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value))
            throw new ArgumentException("Enter finite numeric timing values.");
        return value;
    }
    private StepAside? playbackAway;
    private bool playbackSeenActive;
    private bool actionsStartPending;
    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is not null && (runtime.Snapshot.ActionsActive || actionsStartPending))
        {
            CancelScreenshot("Actions stopped."); quickCancellation?.Cancel(); runtime.StopActions(); return;
        }
        if (runtime is null || !operations.Idle) return;
        if (runtime.CanResume) { await ResumeActionsAsync(); return; }
        if (!TryQuickSequence(out var definition, out var plan, out var error)) { ReasonLabel.Text = error; return; }
        if (WorkshopKeysBlocked(definition!)) return;
        TestResultLabel.Text = "";
        StepAside? away = null;
        using var startCancellation = new CancellationTokenSource();
        operations.Begin(Operation.Quick); actionsStartPending = true; quickCancellation = startCancellation;
        long startIntent = runtime.StopVersion;
        bool accepted = false;
        try
        {
            ApplyKeepAwake();
            if (runtime.IsDesktopMode)
            {
                var frame = runtime.GetTargetFrame(); plan!.ValidateDesktopMode(frame.Width, frame.Height, frame.Dpi);
                away = StepAsideForTarget(MinimizeOnStartBox.IsChecked == true);
            }
            away ??= StepAsideForTarget(minimize: false); // A window target comes forward below; the clicker comes back after.
            if (!plan!.LeaveCursorFree && !runtime.BringTargetForward()) { ReasonLabel.Text = runtime.Snapshot.Reason; return; }
            // Allow the initiating mouse/key release and foreground handoff to settle BEFORE
            // sampling physical input. The old countdown ran after that sample.
            RefreshStatus();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
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
                if (Covered()) throw new InvalidOperationException("The first click location is still covered by ME Clicker. Move the clicker away or use Ping location to check it.");
            }
            if (startIntent != runtime.StopVersion || allowClose || closing) return;
            accepted = runtime.TryStartQuick(plan!, definition!.Steps.Count, TimeSpan.Zero, ResizeClicksBox.SelectedIndex == 0);
            if (accepted) { playbackAway = away; playbackSeenActive = false; }
            if (!accepted) { ReasonLabel.Text = runtime.Snapshot.Reason; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ReasonLabel.Text = ex.Message; }
        finally
        {
            quickCancellation = null; operations.End(Operation.Quick); actionsStartPending = false;
            if (!accepted) ComeBack(away);
            RefreshStatus();
        }
    }
    private void Stop_Click(object sender, RoutedEventArgs e) { screenshotCancellation?.Cancel(); quickCancellation?.Cancel(); quickRecorder?.Stop(); runtime?.Stop(); }
    private void Cleanup_Click(object sender, RoutedEventArgs e) => runtime?.RetryCleanup();
    private void Overlay_Changed(object sender, RoutedEventArgs e) { if (initialized) { RefreshStatus(); PersistPreferences(); } }
    private EmergencyStopMode ChosenEmergencyStop => (EmergencyStopMode)Math.Max(0, EmergencyStopBox.SelectedIndex);
    /// <summary>Settings → Emergency stop: shows what the choice does and hands it to the runtime.</summary>
    private void EmergencyStop_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (EmergencyStopHint is null) return;
        EmergencyStopHint.Text = ChosenEmergencyStop == EmergencyStopMode.OnlyStopAll
            ? "Stop All (F9) stops everything at once: the Action List, every toggle and recording. Nothing else you press or move stops the clicker, so you can click into your game while it runs."
            : "Any key or mouse button the clicker is using, or moving the mouse while the clicker moves it, stops everything at once. Safer, but clicking into your game to bring it to the front also stops a run. Stop All still stops everything.";
        if (runtime is not null) runtime.EmergencyStop = ChosenEmergencyStop;
        if (initialized) { RefreshStatus(); PersistPreferences(); }
    }
    private void Navigation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Pages is null) return;
        if (Navigation.SelectedItem is ListBoxItem { Tag: string page }) ShowPage(page);
    }
    /// <summary>Name of the visible page grid; each navigation tab's Tag names its page.</summary>
    private string currentPage = "QuickPage";
    private void ShowPage(string page)
    {
        currentPage = page;
        foreach (UIElement child in Pages.Children)
            child.Visibility = child is FrameworkElement { Name: var name } && name == page ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Navigate(string page)
    {
        var tab = Navigation.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag as string == page);
        if (tab is not null) Navigation.SelectedItem = tab;
        ShowPage(page);
    }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        var appearance = (Appearance)ThemeBox.SelectedIndex;
        ((App)Application.Current).Appearance = appearance;
        AppTheme.Apply(appearance);
        // Action rows carry their own brushes (colour conditions use ink colours), so rebuild them for the new theme.
        RefreshQuickList(QuickActionList.SelectedIndex, preserveEditor: true);
        PersistPreferences();
    }
    private void LightTheme_Click(object sender, RoutedEventArgs e) => ThemeBox.SelectedIndex = 2;
    private void DarkTheme_Click(object sender, RoutedEventArgs e) => ThemeBox.SelectedIndex = 1;
    private async Task ApplyHotkeysAsync()
    {
        if (runtime is null) return;
        if (operations.Has(Operation.Binding)) { ReasonLabel.Text = "Wait for the current binding change."; return; }
        if (runtime.Snapshot.Busy) { runtime.Stop("Stopped before changing global controls."); ReasonLabel.Text = "Wait for cleanup, then apply controls again."; return; }
        if (selectedStartKey == selectedStopKey || selectedStartKey == selectedRecordKey || selectedStopKey == selectedRecordKey
            || screenshotSettings.HotkeyEnabled && (selectedStartKey == screenshotSettings.Hotkey || selectedStopKey == screenshotSettings.Hotkey))
        {
            selectedStartKey = appliedToggleKey; selectedStopKey = appliedStopKey;
            ToggleKeyBox.Content = ShortcutKeys.Label(appliedToggleKey); StopKeyBox.Content = ShortcutKeys.Label(appliedStopKey);
            ReasonLabel.Text = "Each global control needs a different key. The previous key was kept."; return;
        }
        operations.Begin(Operation.Binding); Pages.IsEnabled = false;
        try
        {
            enabledToggles.Clear(); rememberedToggles = []; RefreshToggleList();
            await runtime.ConfigureHelpersAsync([]);
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
        finally { operations.End(Operation.Binding); Pages.IsEnabled = true; }
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
            CloseToTrayBox.IsChecked == true, trayNoticeShown, screenshotSettings, appliedRecordKey, ChosenEmergencyStop,
            Actions: new(LoopBox.IsChecked == true, MinimizeOnStartBox.IsChecked == true, RecordDelaysBox.IsChecked == true,
                MinimizeOnRecordBox.IsChecked == true, RecordMovementBox.IsChecked == true),
            ActiveToggles: rememberedToggles, Controller: controllerControls);
        if (operations.Has(Operation.Preferences)) return;
        operations.Begin(Operation.Preferences);
        try
        {
            while (pendingPreferences is { } next)
            {
                pendingPreferences = null;
                await Task.Run(() => preferences.Save(next));
            }
        }
        catch (Exception ex) { ReasonLabel.Text = "Preferences could not be saved: " + ex.Message; }
        finally { operations.End(Operation.Preferences); }
    }

    private async Task SaveProfiles(List<ProfileDefinition> updated)
    {
        if (operations.Has(Operation.Library)) throw new InvalidOperationException("A profile operation is already in progress.");
        operations.Begin(Operation.Library);
        try { await Task.Run(() => store.Save(updated)); profiles = updated; RefreshToggleList(); }
        finally { operations.End(Operation.Library); }
    }
    private async void SaveProfile_Click(object sender, RoutedEventArgs e) => await SaveCurrentMacroAsync();
    /// <summary>Saves the Action List under the name in the box. True when the file is written; Share on Workshop relies on it.</summary>
    private async Task<bool> SaveCurrentMacroAsync()
    {
        if (!operations.Idle || runtime?.Snapshot.Busy == true) { MacroSaveStatus.Text = "Stop before saving the macro."; return false; }
        string name = ActionMacroNameBox.Text.Trim();
        if (name.Length == 0) { MacroSaveStatus.Text = "Input macro name."; ActionMacroNameBox.Focus(); return false; }
        if (!TryQuickSequence(out var sequence, out _, out var error)) { ReasonLabel.Text = error; return false; }
        operations.Begin(Operation.MacroFile); RefreshStatus();
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
            loadedMacroFile = path; loadedFileProfile = file;
            MacroSaveStatus.Text = $"Saved ‘{name}’."; MacroSaveStatus.ToolTip = path;
            ReasonLabel.Text = "Macro saved. Use Load Macro to open it for editing or playback.";
            return true;
        }
        catch (Exception ex) { MacroSaveStatus.Text = exportedPath is null ? "Could not save: " + ex.Message : $"File saved at {exportedPath}, but the local library update failed: {ex.Message}"; return false; }
        finally { operations.End(Operation.MacroFile); RefreshStatus(); }
    }
    private void LoadActionMacro(ProfileDefinition profile, MacroDefinition macro)
    {
            var definition = macro.Sequence ?? SequenceCompiler.FromHelper(macro.Plan!);
            var history = new SequenceHistory(definition, profile.Points);
            quickPoints = profile.Points;
            quickHistory = history;
            var repetition = definition.Repetition ?? new();
            loadingQuick = true;
            RecordMovementBox.IsChecked = definition.Steps.Any(s => s.Motion?.UsePreviousPause == true || s.Motion?.MoveDuration > TimeSpan.Zero);
            LoopBox.IsChecked = repetition.Count == 0 && repetition.ActiveDuration is null;
            loadingQuick = false;
            ActionMacroNameBox.Text = macro.Name; loadedQuickMacro = macro.Id; loadedQuickProfile = profile.Id; MacroSaveStatus.Text = "";
            Navigate("QuickPage"); RefreshQuickList(); NewAction_Click(this, new RoutedEventArgs());
            ReasonLabel.Text = "Loaded. Press Start or the configured start / stop key when ready.";
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose || runtime is null) return;
        if (TryHideToTray()) { e.Cancel = true; return; }
        CancelPowerSchedule();
        screenshotCancellation?.Cancel();
        quickCancellation?.Cancel(); quickRecorder?.Stop();
        if (!operations.Has(Operation.Screenshot) && !operations.Has(Operation.Quick) && !runtime.Snapshot.Busy && !runtime.Snapshot.CleanupPending && !operations.Has(Operation.Preferences) && !operations.Has(Operation.Library) && !operations.Has(Operation.Binding) && !operations.Has(Operation.MacroFile)) { allowClose = true; return; }
        e.Cancel = true;
        if (closing) return;
        closing = true; runtime.Stop("Application closing.");
        for (int i = 0; i < 60 && runtime.Snapshot.Busy; i++) await Task.Delay(50);
        for (int i = 0; i < 60 && (!operations.Idle || operations.Has(Operation.Preferences)); i++) await Task.Delay(50);
        if (!operations.Idle || operations.Has(Operation.Preferences)) { closing = false; exitRequested = false; RestoreFromTray(); ReasonLabel.Text = "A save, capture or binding update is still finishing. Try exiting again shortly."; return; }
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending)
        { closing = false; exitRequested = false; RestoreFromTray(); ReasonLabel.Text = "Release physical input and retry cleanup before exiting."; return; }
        allowClose = true;
        await Dispatcher.Yield(DispatcherPriority.Background);
        Close();
    }
}
