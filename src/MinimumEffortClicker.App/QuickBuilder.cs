using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private SequenceHistory quickHistory = new(new([]));
    private IReadOnlyList<NamedPoint>? quickPoints;
    // Where a selected area sits, with the window size and DPI it was picked at; null until an area is selected.
    private ClientPoint? quickAreaOrigin;
    // A wait from an older macro that the panel has no fields for (an image wait): kept as it is, only its timing edits.
    private ConditionDefinition? quickOtherCondition;
    private SequenceStep? editingQuick;
    private string? originalQuickDefaultName;
    private Guid? loadedQuickProfile, loadedQuickMacro;
    private bool loadingQuick, quickValid;
    private bool useCursorLocation = true;
    private CancellationTokenSource? quickCancellation;
    private DesktopRecorder? quickRecorder;
    private readonly ObservableCollection<QuickChildRow> quickChildren = [];
    private readonly Dictionary<string, InputControl> quickInputs = SequenceKeys.Supported.ToDictionary(k => k.Key, k => InputControl.Key(k.Value));
    private sealed record QuickRow(int Index, SequenceStep Step, string Title, string Detail, Brush Background, Brush Foreground, bool Reselect = false)
    {
        /// <summary>The row's Repeating (0) / Once (1) dropdown.</summary>
        public int RepeatIndex => Step.Once ? 1 : 0;
        /// <summary>A wait whose area belongs to the other kind of Click target: tinted red with a "Reselect Area" tag.</summary>
        public Visibility ReselectVisibility => Reselect ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>The mouse for clicks and scrolls, the keyboard for key presses, nothing for pauses, waits and
        /// screenshots. A recording that mixes both shows the mouse.</summary>
        public ImageSource? Device
        {
            get
            {
                IEnumerable<InputControl> inputs = Step.Kind switch
                {
                    StepKind.Input when Step.Control is { } control => [control],
                    StepKind.TimedGroup => (Step.Children ?? []).Select(c => c.Control),
                    _ => []
                };
                var kinds = inputs.Where(i => i.Kind != InputKind.PointerMove).Select(i => i.Kind).ToList();
                if (kinds.Count == 0) return null;
                return kinds.All(k => k == InputKind.ScanCode) ? UiArt.Keyboard : UiArt.Mouse;
            }
        }
    }
    /// <summary>
    /// An area picked with Desktop as the Click target is a spot on the screen, one picked on a window
    /// moves with that window. After switching between the two the area belongs to the old target, so the wait is
    /// marked and needs Select area again.
    /// </summary>
    private bool PickedForOtherTarget(CaptureRegion? region) =>
        region is not null && runtime is not null && (region.Origin.Space == CoordinateSpace.Desktop) != runtime.IsDesktopMode;
    private static SolidColorBrush ColourBrush(int rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    private static Brush ColourText(int rgb)
    {
        static double L(int c) { double s = c / 255d; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(rgb >> 16 & 255) + 0.7152 * L(rgb >> 8 & 255) + 0.0722 * L(rgb & 255) > 0.179 ? Brushes.Black : Brushes.White;
    }
    /// <summary>The colour preview, the one-line summary and Ping scan area, from what the wait fields say now.</summary>
    private void ShowConditionColour()
    {
        if (ConditionLabel is null || PingScanAreaButton is null) return;
        ConditionColourPreview.Background = TryConditionColour(out int rgb) ? ColourBrush(rgb) : Brushes.Transparent;
        // The summary sits on the colour, so it is dark on light colours and light on dark ones.
        if (TryConditionColour(out int ink)) ConditionLabel.Foreground = ColourText(ink); else ConditionLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        if (ModeBox.SelectedIndex != 3) return;
        ActionHint.Text = ConditionKindBox.SelectedIndex == 1 ? "Choose an area and its colour: the wait continues once no pixel in that area has the colour any more." : "Continue when a colour appears or disappears.";
        try
        {
            var condition = ReadQuickCondition();
            ConditionLabel.Text = ConditionSummary(condition); PingScanAreaButton.IsEnabled = condition.Region is not null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ConditionLabel.Text = ex.Message; PingScanAreaButton.IsEnabled = false; }
        // Select area is outlined red until there is an area, then green.
        bool areaChosen = quickOtherCondition?.Region is { } kept && !PickedForOtherTarget(kept);
        if (!areaChosen) try { ReadQuickArea(); areaChosen = true; } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        SelectAreaButton.SetResourceReference(StyleProperty, areaChosen ? "ChosenButton" : "NeedsChoiceButton");
        RefreshAddAction();
    }
    /// <summary>
    /// Add Action / Save Changes stays grey until the editor describes an action that can be added;
    /// hovering it then says what is missing.
    /// </summary>
    private void RefreshAddAction()
    {
        if (AddActionButton is null || loadingQuick) return;
        bool complex = editingQuick is not null && ModeBox.SelectedIndex < 0;
        string? missing = null;
        if (ModeBox.SelectedIndex < 0 && !complex) missing = "Choose an action first.";
        else try { ReadQuickAction(); } catch (Exception ex) { missing = ex.Message; }
        AddActionButton.IsEnabled = missing is null;
        AddActionButton.ToolTip = missing;
    }
    private sealed class QuickChildRow
    {
        public required TimedAction Original { get; init; }
        public string Input { get; set; } = "W";
        public string Offset { get; set; } = "0";
        public string Duration { get; set; } = "20";
        public string X { get; set; } = "";
        public string Y { get; set; } = "";
    }
    private void InitializeQuickBuilder()
    {
        SetQuickKey("W");
        MouseChoice = PointerButton.Left;
        ButtonPicker.SelectedChanged += (_, _) => { if (initialized && !loadingQuick) { TestResultLabel.Text = ""; RefreshAddAction(); } };
        foreach (var button in MouseChoices) quickInputs[button + " mouse"] = InputControl.Mouse(button);
        QuickRecordKeyBox.Content = ShortcutKeys.Label(selectedRecordKey);
        ChildInputColumn.ItemsSource = quickInputs.Keys.ToArray(); QuickChildrenGrid.ItemsSource = quickChildren;
        QuickUndoButton.IsEnabled = QuickRedoButton.IsEnabled = false;
        QuickFields.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (initialized && !loadingQuick) { TestResultLabel.Text = ""; RefreshAddAction(); } }));
        // Any choice in the editor can make the action addable or not.
        QuickFields.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => { if (initialized) RefreshAddAction(); }));
        QuickFields.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => { if (initialized) RefreshAddAction(); }));
        QuickFields.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => { if (initialized) RefreshAddAction(); }));
    }
    private static string Ms(TimeSpan time) => time.TotalMilliseconds.ToString("0.###", CultureInfo.CurrentCulture);
    private static TimeSpan Milliseconds(TextBox box) => TimeSpan.FromMilliseconds(Number(box));
    private static TimeSpan OptionalPause(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? TimeSpan.Zero : Milliseconds(box);
    private SequenceDefinition ApplyListOptions(SequenceDefinition source) => QuickActions.WithClickMovement(
        QuickActions.WithCursorFree(source, false), RecordMovementBox.IsChecked == true);
    private static string QuickSummary(SequenceDefinition sequence) => $"{sequence.Steps.Count} action{(sequence.Steps.Count == 1 ? "" : "s")} · {sequence.Repetition ?? new()}";
    private bool TryQuickSequence(out SequenceDefinition? definition, out SequencePlan? plan, out string error)
    {
        definition = null; plan = null; error = "";
        try
        {
            if (!quickHistory.Current.Steps.Any(s => s.Enabled)) throw new ArgumentException("Add at least one enabled action.");
            var repetition = new SequenceRepetition(LoopBox.IsChecked == true ? 0 : 1);
            definition = ApplyListOptions(quickHistory.Current with { Repetition = repetition });
            if (EnduranceMeasurement.Destination is not null && definition.Steps.Any(s => s.Enabled && (s.Kind != StepKind.Wait || s.Duration > TimeSpan.FromSeconds(10))))
                throw new ArgumentException("Endurance diagnostic accepts only pauses of ten seconds or less.");
            plan = SequenceCompiler.Compile(definition, quickPoints); return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        { error = ex is FormatException ? "Enter a whole number of loops." : ex.Message; return false; }
    }
    private void Config_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized || loadingQuick || runtime is null) return;
        quickValid = TryQuickSequence(out var definition, out _, out var error);
        RunButton.ToolTip = quickValid ? null : error;
        runtime.QuickSequenceArmed = quickValid;
        RefreshStatus();
    }
    private void RefreshQuickAvailability(RuntimeSnapshot snapshot)
    {
        bool idle = !snapshot.Busy && !snapshot.CleanupPending && operations.Idle;
        Navigation.IsEnabled = !operations.Has(Operation.Quick);
        // Keep the ScrollViewer enabled while its editing controls are locked.
        QuickFields.IsEnabled = QuickEditorTools.IsEnabled = QuickListTools.IsEnabled = QuickActionList.IsEnabled = idle;
        LoopBox.IsEnabled = MinimizeOnStartBox.IsEnabled = idle;
        bool stopping = snapshot.ActionsActive || actionsStartPending;
        RunButton.Content = $"{(stopping ? "Stop" : "Start")} {StartHint}";
        RunButton.SetResourceReference(FrameworkElement.StyleProperty, stopping ? "RedButton" : "GreenButton");
        System.Windows.Automation.AutomationProperties.SetName(RunButton, stopping ? "Stop Actions" : "Start Actions");
        RunButton.IsEnabled = stopping || operations.Idle && !operations.Has(Operation.Dialog) && runtime?.Control.Available == true
            && (runtime.CanResume || quickValid && runtime.CanStartActions);
        QuickRecordButton.IsEnabled = quickRecorder is not null || quickCancellation is not null || idle;
        QuickRecordButton.Content = $"{RecordHint} Record/Stop";
        LoadMacroButton.IsEnabled = idle;
        MinimizeOnRecordBox.IsEnabled = RecordDelaysBox.IsEnabled = RecordMovementBox.IsEnabled = idle;
        QuickRecordKeyBox.IsEnabled = idle && !operations.Has(Operation.RecordKey);
        ClearActionListButton.IsEnabled = idle && quickHistory.Current.Steps.Count > 0;
        ShowTestSelectedButton();
        if (quickRecorder?.Paused == true) QuickRecordStatus.Text = "Recording paused after focus changed. Finish to keep the captured actions.";
    }
    /// <summary>Minimize on Start, Record pauses, Minimize on record, mouse movement: remembered for the next start.</summary>
    private void ActionsChoice_Changed(object sender, RoutedEventArgs e) { Config_Changed(sender, e); PersistPreferences(); }
    private void Loop_Changed(object sender, RoutedEventArgs e)
    {
        // Remembered for the next start, also when a loaded macro sets it (the last choice wins).
        PersistPreferences();
        if (loadingQuick || !initialized) return;
        // Explicit repetition edits replace an older active-time limit.
        if (quickHistory.Current.Repetition?.ActiveDuration is not null)
            quickHistory.Commit(quickHistory.Current with { Repetition = null });
        Config_Changed(sender, e);
    }
    private void ActionType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ActionFields is null || loadingQuick) return;
        TestResultLabel.Text = "";
        ShowQuickFields();
    }
    private void ShowQuickFields()
    {
        bool complex = editingQuick is not null && ModeBox.SelectedIndex < 0;
        ActionFields.Visibility = ModeBox.SelectedIndex >= 0 || complex ? Visibility.Visible : Visibility.Collapsed;
        MouseFields.Visibility = ModeBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScrollFields.Visibility = ModeBox.SelectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        LocationFields.Visibility = ModeBox.SelectedIndex is 0 or 5 ? Visibility.Visible : Visibility.Collapsed;
        ClickLocationHeader.Visibility = ModeBox.SelectedIndex == 5 ? Visibility.Collapsed : Visibility.Visible;
        ScrollLocationHeader.Visibility = ModeBox.SelectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        KeyFields.Visibility = ModeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        RandomFields.Visibility = ModeBox.SelectedIndex is 0 or 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        PressFields.Visibility = ModeBox.SelectedIndex is 0 or 1 ? Visibility.Visible : Visibility.Collapsed;
        AfterFields.Visibility = ModeBox.SelectedIndex is 0 or 1 or 5 ? Visibility.Visible : Visibility.Collapsed;
        MotionFields.Visibility = ModeBox.SelectedIndex == 0 && !useCursorLocation ? Visibility.Visible : Visibility.Collapsed;
        DoubleFields.Visibility = ModeBox.SelectedIndex == 0 && ClickKindBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PauseFields.Visibility = ModeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        ScreenshotActionFields.Visibility = ModeBox.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        ConditionAdvancedFields.Visibility = ConditionFields.Visibility = ModeBox.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        ComplexFields.Visibility = complex ? Visibility.Visible : Visibility.Collapsed;
        // Blue Add Action adds a new action; green Save Changes changes the selected one, so it is always clear which.
        AddActionButton.Content = editingQuick is null ? "Add Action" : "Save Changes";
        AddActionButton.SetResourceReference(StyleProperty, editingQuick is null ? "BlueButton" : "GreenButton");
        PressLabel.Text = ClickKindBox.SelectedIndex == 2 && ModeBox.SelectedIndex == 0 || HoldKeyBox.IsChecked == true && ModeBox.SelectedIndex == 1 ? "Hold duration (ms)" : "Press duration (ms)";
        ShowTimingOptions();
        Location_Changed(this, new RoutedEventArgs());
        ActionHint.Text = complex ? "Edit the recorded inputs below. Their overlap is preserved." : ModeBox.SelectedIndex switch
        { 0 => "Click at your cursor or a saved location.", 1 => "Press a key, with optional modifiers or a longer hold.", 2 => "Wait before the next action.", 3 => "Continue when a colour appears or disappears.", 4 => "Save a fullscreen PNG of all monitors.", 5 => "Turn the mouse wheel at your cursor or a saved location.", _ => "Choose an action to see its settings." };
        if (ModeBox.SelectedIndex == 3) ShowConditionColour(); else RefreshAddAction();
    }
    private void ClickKind_Changed(object sender, SelectionChangedEventArgs e)
    { if (!initialized || loadingQuick) return; PressBox.Text = ClickKindBox.SelectedIndex == 2 ? "1000" : "20"; ShowQuickFields(); }
    private void HoldKey_Changed(object sender, RoutedEventArgs e)
    { if (!initialized || loadingQuick) return; PressBox.Text = HoldKeyBox.IsChecked == true ? "1000" : "20"; ShowQuickFields(); }
    /// <summary>The Key Press key (a SequenceKeys name), shown on the Key button as its keycap.</summary>
    private string quickKey = "W";
    private void SetQuickKey(string name)
    {
        quickKey = name;
        KeyBox.Content = UiArt.InputContent(InputControl.Key(SequenceKeys.Supported[name]), name);
    }
    private void ChooseKey_Click(object sender, RoutedEventArgs e)
    {
        var picker = new KeyPickerWindow(this, "Choose a key", "Click the key to press. Ctrl, Shift and Alt are the buttons next to the key.",
            InputControl.Key(SequenceKeys.Supported[quickKey]), input => SequenceKeys.IsModifier(input) ? "use the Ctrl, Shift and Alt buttons next to the key." : null, withMouse: false);
        if (picker.ShowDialog() != true || picker.Selected is not { } input || KeyPickerWindow.KeyName(input) is not { } key) return;
        SetQuickKey(key); TestResultLabel.Text = ""; RefreshAddAction();
    }
    private void Location_Changed(object sender, RoutedEventArgs e)
    {
        if (CursorLocationButton is null || SetLocationButton is null) return;
        CursorLocationButton.SetResourceReference(StyleProperty, useCursorLocation ? "ChosenButton" : "StandardButton");
        SetLocationButton.SetResourceReference(StyleProperty, useCursorLocation ? "StandardButton" : "ChosenButton");
        QuickXBox.IsEnabled = QuickYBox.IsEnabled = !useCursorLocation;
        MotionFields.Visibility = ModeBox.SelectedIndex == 0 && !useCursorLocation ? Visibility.Visible : Visibility.Collapsed;
        RefreshAddAction();
    }
    private void CursorLocation_Click(object sender, RoutedEventArgs e)
    { useCursorLocation = true; Location_Changed(sender, e); }
    private void ShowQuickCoordinates()
    {
        QuickXBox.Text = quickPoint?.X.ToString(CultureInfo.CurrentCulture) ?? "";
        QuickYBox.Text = quickPoint?.Y.ToString(CultureInfo.CurrentCulture) ?? "";
        Location_Changed(this, new RoutedEventArgs());
    }
    private ClientPoint ReadQuickPoint()
    {
        if (quickPoint is not { } point) throw new ArgumentException("Use Set Location to choose a point first.");
        if (!int.TryParse(QuickXBox.Text, out int x) || !int.TryParse(QuickYBox.Text, out int y))
            throw new ArgumentException("Enter whole-number X and Y coordinates.");
        var result = point with { X = x, Y = y };
        if (!result.IsValid) throw new ArgumentException("X and Y must be inside the saved window size.");
        return result;
    }
    private void ConditionKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loadingQuick || ConditionLabel is null) return;
        TestResultLabel.Text = "";
        // Choosing a colour wait replaces an older image wait that was loaded into the panel.
        if (ConditionKindBox.SelectedIndex >= 0) quickOtherCondition = null;
        ShowConditionColour();
    }
    private void ConditionField_Changed(object sender, TextChangedEventArgs e) { if (initialized && !loadingQuick) ShowConditionColour(); }
    private void ConditionCalibrate_Changed(object sender, RoutedEventArgs e) { if (initialized && !loadingQuick) ShowConditionColour(); }

    private bool TryConditionColour(out int rgb) =>
        int.TryParse(ConditionRgbBox.Text.Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb) && rgb is >= 0 and <= 0xFFFFFF
        && ConditionRgbBox.Text.Trim().TrimStart('#').Length == 6;
    private static int WholeNumber(TextBox box, int minimum, int maximum, string message) =>
        int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) && value >= minimum && value <= maximum ? value : throw new ArgumentException(message);

    /// <summary>
    /// The wait as the panel's fields describe it. A selected area keeps the window size and DPI it was picked at, and
    /// playback adapts it like a click; coordinates typed with Calibrate ticked are measured on the Click target as it is now.
    /// </summary>
    private ConditionDefinition ReadQuickCondition()
    {
        var policy = new ConditionPolicy(QuickScanInterval(), WholeNumber(ConditionStableBox, 1, 10, "Consecutive matching samples must be 1–10."));
        if (quickOtherCondition is { } other)
        {
            if (PickedForOtherTarget(other.Region)) throw new ArgumentException("This area was picked for another Click target. Select area again.");
            var kept = other with { Policy = policy }; kept.Validate(); return kept;
        }
        var region = ReadQuickArea();
        if (!TryConditionColour(out int rgb)) throw new ArgumentException("Enter the colour as six hexadecimal digits, for example DEAB48.");
        int tolerance = WholeNumber(ConditionToleranceBox, 0, 255, "Tolerance must be 0–255.");
        var condition = new ConditionDefinition(ConditionKindBox.SelectedIndex == 1 ? ConditionKind.ColorDisappears : ConditionKind.RegionColor,
            policy, region, rgb, (byte)tolerance, 1);
        condition.Validate(); return condition;
    }
    /// <summary>The watched area from X, Y, width and height (see ReadQuickCondition for how it is measured).</summary>
    private CaptureRegion ReadQuickArea()
    {
        if (new[] { ConditionXBox, ConditionYBox, ConditionWidthBox, ConditionHeightBox }.All(b => string.IsNullOrWhiteSpace(b.Text)))
            throw new ArgumentException("Select an area first.");
        int x = WholeNumber(ConditionXBox, int.MinValue, int.MaxValue, "Enter whole-number X and Y coordinates.");
        int y = WholeNumber(ConditionYBox, int.MinValue, int.MaxValue, "Enter whole-number X and Y coordinates.");
        int width = WholeNumber(ConditionWidthBox, 1, 256, "Width and height must be 1–256 pixels.");
        int height = WholeNumber(ConditionHeightBox, 1, 256, "Width and height must be 1–256 pixels.");
        ClientPoint origin;
        if (ConditionCalibrateBox.IsChecked == true || quickAreaOrigin is null)
        {
            var frame = (runtime ?? throw new InvalidOperationException("The clicker is still starting.")).GetTargetFrame();
            // Desktop mode keeps areas in absolute screen pixels so they work on any monitor.
            origin = new(x, y, frame.Width, frame.Height, frame.Dpi, runtime.IsDesktopMode ? CoordinateSpace.Desktop : CoordinateSpace.WindowClient);
        }
        else origin = quickAreaOrigin with { X = x, Y = y };
        var region = new CaptureRegion(origin, width, height); region.Validate();
        if (PickedForOtherTarget(region)) throw new ArgumentException("This area was picked for another Click target. Select area again.");
        return region;
    }
    private TimeSpan QuickScanInterval()
    {
        var interval = Number(QuickScanIntervalBox);
        if (interval < 100 || interval > 5000) throw new ArgumentException("Scan interval must be 100–5,000 ms. Try 500 ms.");
        return TimeSpan.FromMilliseconds(interval);
    }
    /// <summary>Puts a saved wait into the panel's fields (called while loadingQuick is set).</summary>
    private void ShowConditionFields(ConditionDefinition? condition)
    {
        bool colour = condition?.Kind is ConditionKind.PixelColor or ConditionKind.RegionColor or ConditionKind.ColorDisappears;
        quickOtherCondition = condition is not null && !colour ? condition : null;
        ConditionKindBox.SelectedIndex = condition is null ? 0 : condition.Kind == ConditionKind.ColorDisappears ? 1 : colour ? 0 : -1;
        var region = condition?.Region;
        quickAreaOrigin = region?.Origin;
        ConditionXBox.Text = region?.Origin.X.ToString(CultureInfo.CurrentCulture) ?? ""; ConditionYBox.Text = region?.Origin.Y.ToString(CultureInfo.CurrentCulture) ?? "";
        ConditionWidthBox.Text = region?.Width.ToString(CultureInfo.CurrentCulture) ?? ""; ConditionHeightBox.Text = region?.Height.ToString(CultureInfo.CurrentCulture) ?? "";
        ConditionRgbBox.Text = colour ? condition!.Color.ToString("X6") : "";
        ConditionToleranceBox.Text = (condition?.Tolerance ?? 8).ToString(CultureInfo.CurrentCulture);
        QuickScanIntervalBox.Text = condition is null ? "500" : Ms(condition.Policy.SampleInterval);
        ConditionStableBox.Text = (condition?.Policy.StableSamples ?? 1).ToString(CultureInfo.CurrentCulture);
        ConditionCalibrateBox.IsChecked = region is null;
    }

    private async void SelectConditionArea_Click(object sender, RoutedEventArgs e) => await PickConditionArea(colourOnly: false);
    private async void ConditionEyedropper_Click(object sender, RoutedEventArgs e) => await PickConditionArea(colourOnly: true);
    /// <summary>
    /// Select area (drag a box; the colour comes from its top-left pixel) or the Eyedropper (click one pixel; the area is
    /// kept, or becomes that pixel when there is none yet), on a still picture of the target or, on Desktop, of the monitors.
    /// </summary>
    private async Task PickConditionArea(bool colourOnly)
    {
        if (runtime is null || runtime.Snapshot.Busy || operations.Has(Operation.Quick)) return;
        operations.Begin(Operation.Quick); RefreshStatus();
        using var selection = new CancellationTokenSource(); quickCancellation = selection;
        void Cancel(string reason) { _ = selection.CancelAsync(); }
        runtime.Control.StopRequested += Cancel;
        try
        {
            int side = colourOnly ? 1 : 256;
            RegionPicker picker;
            if (runtime.IsDesktopMode)
            {
                // The clicker steps out of the pictures and comes back before the picker opens.
                var (monitors, pictures) = await CaptureMonitorPicturesAsync(selection.Token);
                selection.Token.ThrowIfCancellationRequested();
                Activate();
                picker = new RegionPicker(this, monitors, pictures, runtime.GetTargetFrame(), side);
            }
            else
            {
                // The picture is read from the window itself, so nothing moves.
                var (snapshot, frame) = await Task.Run(() => runtime.CaptureTargetPicture(selection.Token), selection.Token);
                selection.Token.ThrowIfCancellationRequested();
                picker = new RegionPicker(this, snapshot, frame, side);
            }
            using var abortPicker = selection.Token.Register(() => Dispatcher.BeginInvoke(() => picker.Close()));
            if (picker.ShowDialog() == true && picker.Region is { } chosen)
            {
                loadingQuick = true;
                try
                {
                    quickOtherCondition = null;
                    if (ConditionKindBox.SelectedIndex < 0) ConditionKindBox.SelectedIndex = 0;
                    if (!colourOnly || new[] { ConditionXBox, ConditionYBox, ConditionWidthBox, ConditionHeightBox }.All(b => string.IsNullOrWhiteSpace(b.Text)))
                    {
                        quickAreaOrigin = chosen.Origin; ConditionCalibrateBox.IsChecked = false;
                        ConditionXBox.Text = chosen.Origin.X.ToString(CultureInfo.CurrentCulture); ConditionYBox.Text = chosen.Origin.Y.ToString(CultureInfo.CurrentCulture);
                        ConditionWidthBox.Text = chosen.Width.ToString(CultureInfo.CurrentCulture); ConditionHeightBox.Text = chosen.Height.ToString(CultureInfo.CurrentCulture);
                    }
                    ConditionRgbBox.Text = picker.Color.ToString("X6");
                }
                finally { loadingQuick = false; }
                TestResultLabel.Text = "";
                ReasonLabel.Text = colourOnly ? "Colour picked. The area is kept." : "Area and colour selected. Resize the target only before selecting a new area.";
            }
        }
        catch (OperationCanceledException) { ReasonLabel.Text = "Selection cancelled."; }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; AppLog.Failure(colourOnly ? "Eyedropper" : "Select area", ex); }
        finally
        {
            runtime.Control.StopRequested -= Cancel; quickCancellation = null; operations.End(Operation.Quick);
            RefreshStatus(); ShowConditionColour();
        }
    }
    private static string ConditionSummary(ConditionDefinition c) => (c.Kind switch
        { ConditionKind.RegionColor or ConditionKind.PixelColor => $"Colour #{c.Color:X6} appears", ConditionKind.ColorDisappears => $"Colour #{c.Color:X6} disappears", ConditionKind.ImageAppears => "Image appears", ConditionKind.ImageDisappears => "Image disappears", ConditionKind.RegionChanged => "Area changes", _ => "Window is active" })
        + " · " + (c.Region is { } r ? $"{r.Width} × {r.Height} at {r.Origin.X}, {r.Origin.Y}" : "selected window") + $" · scan every {Ms(c.Policy.SampleInterval)} ms";
    private SequenceStep ReadQuickAction(bool useGeneratedName = false)
    {
        SequenceStep step;
        var after = ModeBox.SelectedIndex is 0 or 1 or 5 ? OptionalPause(AfterBox) : TimeSpan.Zero;
        switch (ModeBox.SelectedIndex)
        {
            case 0:
                step = QuickActions.Click(MouseChoice, (QuickClickKind)ClickKindBox.SelectedIndex, Milliseconds(PressBox), Milliseconds(DoubleGapBox), after, useCursorLocation ? null : ReadQuickPoint()); break;
            case 1:
                var keys = new List<int>();
                if (CtrlBox.IsChecked == true) keys.Add(SequenceKeys.LeftCtrl);
                if (ShiftBox.IsChecked == true) keys.Add(SequenceKeys.LeftShift);
                if (AltBox.IsChecked == true) keys.Add(SequenceKeys.LeftAlt);
                keys.Add(SequenceKeys.Supported[quickKey]);
                step = QuickActions.Keys(keys, Milliseconds(PressBox), after, HoldKeyBox.IsChecked == true); break;
            case 2: step = QuickActions.Pause(TimeSpan.FromMilliseconds(Number(PauseDurationBox) * (PauseUnitBox.SelectedIndex == 1 ? 1000 : 1))); break;
            case 3: step = SequenceStep.WaitUntil("Wait Until", ReadQuickCondition()); break;
            case 4: step = SequenceStep.Screenshot(Milliseconds(ScreenshotPauseBox)); break;
            case 5:
                if (!int.TryParse(ScrollNotchesBox.Text, out int notches)) throw new ArgumentException("Enter a whole number of notches, 1–100.");
                step = QuickActions.Scroll((ScrollDirection)ScrollDirectionBox.SelectedIndex, notches, Milliseconds(ScrollGapBox), after, useCursorLocation ? null : ReadQuickPoint()); break;
            default:
                if (editingQuick is null) throw new ArgumentException("Choose an action first.");
                if (!QuickChildrenGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !QuickChildrenGrid.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException("Finish editing the input row first.");
                var children = quickChildren.Select(c => c.Original with { Control = quickInputs[c.Input], Offset = TimeSpan.FromMilliseconds(double.Parse(c.Offset, CultureInfo.CurrentCulture)), Duration = TimeSpan.FromMilliseconds(double.Parse(c.Duration, CultureInfo.CurrentCulture)), Position = c.Original.Position is { } p ? p with { X = int.Parse(c.X), Y = int.Parse(c.Y) } : null }).ToArray();
                step = editingQuick.Kind == StepKind.Input ? editingQuick with { Duration = Milliseconds(ComplexDurationBox), Control = children[0].Control, Position = children[0].Position }
                    : editingQuick.Kind == StepKind.TimedGroup ? editingQuick with { Duration = Milliseconds(ComplexDurationBox), Children = children }
                    : editingQuick with { Duration = Milliseconds(ComplexDurationBox) }; break;
        }
        if (ModeBox.SelectedIndex is 0 or 1)
        {
            bool random = RandomTimingBox.IsChecked == true;
            step = step with { Timing = random ? new(Milliseconds(MaxPressBox), OptionalPause(MaxAfterBox)) : null };
        }
        if (ModeBox.SelectedIndex == 2 && RandomTimingBox.IsChecked == true)
            step = step with { Timing = new(MaximumPause: Milliseconds(MaxAfterBox)) };
        if (ModeBox.SelectedIndex == 0 && !useCursorLocation && VaryPositionBox.IsChecked == true)
        {
            if (!int.TryParse(PositionOffsetBox.Text, out int pixels)) throw new ArgumentException("Enter a whole-number pixel offset.");
            step = step with { Motion = new(OffsetPixels: pixels) };
        }
        string enteredName = ActionNameBox.Text.Trim();
        bool automatic = useGeneratedName || enteredName.Length == 0 || editingQuick is not null && enteredName == editingQuick.Name && enteredName == originalQuickDefaultName;
        step = step with { Id = editingQuick?.Id ?? step.Id, Name = automatic ? step.Name : enteredName, Enabled = editingQuick?.Enabled ?? true, Notes = editingQuick?.Notes ?? "", Once = editingQuick?.Once ?? false };
        step = ApplyListOptions(new([step])).Steps[0];
        SequenceCompiler.Compile(quickHistory.Current with { Steps = [step], Repetition = new() }, quickPoints);
        return step;
    }
    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var step = ReadQuickAction(); var list = quickHistory.Current.Steps.ToList();
            int index = editingQuick is null ? -1 : list.FindIndex(s => s.Id == editingQuick.Id);
            if (index < 0) list.Add(step); else list[index] = step;
            quickHistory.Commit(ApplyListOptions(quickHistory.Current with { Steps = list }));
            // Saving ends the edit: nothing stays selected, and the editor keeps its values for adding a similar action.
            RefreshQuickList(); ShowQuickFields();
            ReasonLabel.Text = index < 0 ? "Action added. Select a row to edit it." : "Changes saved. Select a row to edit it again.";
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
    }
    private void NewAction_Click(object sender, RoutedEventArgs e)
    {
        loadingQuick = true; QuickActionList.SelectedIndex = -1; editingQuick = null; ModeBox.SelectedIndex = -1;
        ActionNameBox.Text = "";
        MouseChoice = PointerButton.Left; ClickKindBox.SelectedIndex = 0; useCursorLocation = true; quickPoint = null; ShowQuickCoordinates();
        SetQuickKey("W"); CtrlBox.IsChecked = ShiftBox.IsChecked = AltBox.IsChecked = HoldKeyBox.IsChecked = false;
        PressBox.Text = "20"; DoubleGapBox.Text = "80"; AfterBox.Text = "20"; PauseDurationBox.Text = "1"; PauseUnitBox.SelectedIndex = 1;
        ScreenshotPauseBox.Text = "1000"; ScrollDirectionBox.SelectedIndex = 1; ScrollNotchesBox.Text = "3"; ScrollGapBox.Text = "30";
        ShowConditionFields(null); ConditionLabel.Text = "Select the area to scan and what to look for.";
        RandomTimingBox.IsChecked = false; MaxPressBox.Text = "40"; MaxAfterBox.Text = "80";
        VaryPositionBox.IsChecked = false; PositionOffsetBox.Text = "3";
        quickChildren.Clear(); TestResultLabel.Text = ""; AddActionButton.Content = "Add Action";
        loadingQuick = false; Location_Changed(sender, e); ShowQuickFields(); ShowConditionColour();
    }
    private QuickRow[] QuickRows(IEnumerable<SequenceStep> steps) => steps.Select((s, i) =>
        new QuickRow(i, s, $"{i + 1}. {s.Name}" + (s.Enabled ? "" : " (skipped)"), s.Condition is { } c ? ConditionSummary(c) : QuickActions.ReadScroll(s) is { } scroll ? ScrollSummary(scroll) : TimingSummary(s) + (s.Children is { Count: > 1 } children ? $" · {children.Count} inputs" : ""),
            s.Condition is { Kind: ConditionKind.PixelColor or ConditionKind.RegionColor or ConditionKind.ColorDisappears } colour ? ColourBrush(colour.Color) : Brushes.Transparent,
            s.Condition is { Kind: ConditionKind.PixelColor or ConditionKind.RegionColor or ConditionKind.ColorDisappears } ink ? ColourText(ink.Color) : (Brush)FindResource("TextPrimary"),
            PickedForOtherTarget(s.Condition?.Region))).ToArray();
    /// <summary>Shows actions captured so far below the existing list while recording runs.</summary>
    private void ShowRecordingPreview(IReadOnlyList<SequenceStep> recorded)
    {
        loadingQuick = true;
        var rows = QuickRows([.. quickHistory.Current.Steps, .. recorded]);
        QuickActionList.ItemsSource = rows;
        if (rows.Length > 0) QuickActionList.ScrollIntoView(rows[^1]);
        loadingQuick = false;
    }
    private void RefreshQuickList(int selected = -1, bool preserveEditor = false)
    {
        loadingQuick = true;
        QuickActionList.ItemsSource = QuickRows(quickHistory.Current.Steps);
        QuickUndoButton.IsEnabled = quickHistory.CanUndo; QuickRedoButton.IsEnabled = quickHistory.CanRedo;
        if (preserveEditor)
        {
            QuickActionList.SelectedIndex = selected;
            loadingQuick = false; Config_Changed(this, new RoutedEventArgs()); return;
        }
        loadingQuick = false; editingQuick = null; ActionNameBox.Text = ""; AddActionButton.Content = "Add Action";
        if (selected >= 0) QuickActionList.SelectedIndex = selected;
        else ShowQuickFields();
        Config_Changed(this, new RoutedEventArgs());
    }
    private void QuickSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        ShowTestSelectedButton();
        if (loadingQuick || QuickActionList.SelectedItem is not QuickRow row) return;
        TestResultLabel.Text = "";
        var s = row.Step; editingQuick = s; loadingQuick = true;
        try
        {
            RandomTimingBox.IsChecked = s.Timing?.MaximumPressDuration is not null || s.Timing?.MaximumPause is not null || s.Timing?.MaximumInterval is not null;
            ModeBox.SelectedIndex = -1; ActionNameBox.Text = s.Name;
            if (s.Kind == StepKind.Wait) { ModeBox.SelectedIndex = 2; PauseUnitBox.SelectedIndex = 0; PauseDurationBox.Text = Ms(s.Duration); }
            else if (s.Kind == StepKind.Screenshot) { ModeBox.SelectedIndex = 4; ScreenshotPauseBox.Text = Ms(s.Duration); }
            else if (s.Kind == StepKind.Condition)
            { ModeBox.SelectedIndex = 3; ShowConditionFields(s.Condition); ConditionLabel.Text = ConditionSummary(s.Condition!); }
            else
            {
                var actions = s.Kind == StepKind.Input ? new[] { new TimedAction(Guid.NewGuid(), TimeSpan.Zero, s.Control!.Value, s.Duration, s.Position, s.EndPosition, s.TargetId) } : s.Children?.ToArray() ?? [];
                bool uncomplicated = actions.Length > 0 && actions.All(c => c.TargetId is null && (c.EndPosition is null || c.EndPosition == c.Position));
                if (QuickActions.ReadScroll(s) is { } scroll)
                {
                    ModeBox.SelectedIndex = 5; ScrollDirectionBox.SelectedIndex = (int)scroll.Direction;
                    ScrollNotchesBox.Text = scroll.Notches.ToString(CultureInfo.CurrentCulture); ScrollGapBox.Text = Ms(scroll.Gap);
                    quickPoint = scroll.Position; useCursorLocation = quickPoint is null; ShowQuickCoordinates();
                    AfterBox.Text = Ms(scroll.After);
                }
                else if (uncomplicated && actions.All(c => c.Control.Kind == InputKind.MouseButton) && actions.Length <= 2 && actions[0].Offset == TimeSpan.Zero && (actions.Length == 1 || actions[1].Control == actions[0].Control && actions[1].Duration == actions[0].Duration && actions[1].Offset >= actions[0].Duration && actions[1].Position == actions[0].Position))
                {
                    ModeBox.SelectedIndex = 0; MouseChoice = (PointerButton)actions[0].Control.Code;
                    ClickKindBox.SelectedIndex = actions.Length == 2 ? 1 : s.Name.StartsWith("Hold ", StringComparison.Ordinal) || actions[0].Duration.TotalMilliseconds >= 500 ? 2 : 0;
                    PressBox.Text = Ms(actions[0].Duration); DoubleGapBox.Text = actions.Length == 2 ? Ms(actions[1].Offset - actions[0].Duration) : "80";
                    quickPoint = actions[0].Position; useCursorLocation = quickPoint is null; ShowQuickCoordinates();
                    AfterBox.Text = Ms(s.Duration - actions.Max(c => c.Offset + c.Duration));
                }
                else if (uncomplicated && actions.All(c => c.Control.Kind == InputKind.ScanCode && c.Offset == TimeSpan.Zero && c.Duration == actions[0].Duration) && actions.Count(c => !SequenceKeys.IsModifier(c.Control)) == 1 && actions.Where(c => SequenceKeys.IsModifier(c.Control)).All(c => c.Control.Code is SequenceKeys.LeftCtrl or SequenceKeys.LeftShift or SequenceKeys.LeftAlt))
                {
                    ModeBox.SelectedIndex = 1; SetQuickKey(SequenceKeys.Supported.First(k => k.Value == actions.First(c => !SequenceKeys.IsModifier(c.Control)).Control.Code).Key);
                    CtrlBox.IsChecked = actions.Any(c => c.Control.Code == SequenceKeys.LeftCtrl); ShiftBox.IsChecked = actions.Any(c => c.Control.Code == SequenceKeys.LeftShift); AltBox.IsChecked = actions.Any(c => c.Control.Code == SequenceKeys.LeftAlt);
                    HoldKeyBox.IsChecked = s.Name.StartsWith("Hold ", StringComparison.Ordinal) || actions[0].Duration.TotalMilliseconds >= 500; PressBox.Text = Ms(actions[0].Duration); AfterBox.Text = Ms(s.Duration - actions[0].Duration);
                }
                else
                {
                    quickChildren.Clear(); ComplexDurationBox.Text = Ms(s.Duration);
                    foreach (var child in actions)
                    {
                        string label = quickInputs.FirstOrDefault(k => k.Value == child.Control).Key ?? RecordingNormalizer.Label(child.Control);
                        quickInputs[label] = child.Control;
                        quickChildren.Add(new() { Original = child, Input = label, Offset = Ms(child.Offset), Duration = Ms(child.Duration), X = child.Position?.X.ToString() ?? "", Y = child.Position?.Y.ToString() ?? "" });
                    }
                    ChildInputColumn.ItemsSource = quickInputs.Keys.ToArray();
                }
            }
        }
        finally
        {
            VaryPositionBox.IsChecked = s.Motion?.OffsetPixels > 0;
            PositionOffsetBox.Text = (s.Motion?.OffsetPixels > 0 ? s.Motion.OffsetPixels : 3).ToString();
            MaxPressBox.Text = Ms(s.Timing?.MaximumPressDuration ?? (double.TryParse(PressBox.Text, out var press) ? TimeSpan.FromMilliseconds(press) : TimeSpan.FromMilliseconds(20)));
            MaxAfterBox.Text = Ms((s.Timing?.MaximumInterval is { } maxInterval ? maxInterval - (s.Duration - OptionalPause(AfterBox)) : s.Timing?.MaximumPause) ?? (s.Kind == StepKind.Wait ? s.Duration : double.TryParse(AfterBox.Text, out var after) ? TimeSpan.FromMilliseconds(after) : TimeSpan.FromMilliseconds(20)));
            loadingQuick = false; ShowQuickFields(); ShowConditionColour();
            try { originalQuickDefaultName = s.Kind == StepKind.Input && s.Control is { } input && s.Name == RecordingNormalizer.Label(input) ? s.Name : ReadQuickAction(useGeneratedName: true).Name; }
            catch (ArgumentException) { originalQuickDefaultName = null; }
        }
    }
    private void QuickActive_Click(object sender, RoutedEventArgs e)
    {
        if (loadingQuick || sender is not ToggleButton { DataContext: QuickRow row } box || runtime?.Snapshot.Busy == true || operations.Has(Operation.Quick)) return;
        var steps = quickHistory.Current.Steps.ToArray();
        var step = steps[row.Index] with { Enabled = box.IsChecked == true };
        steps[row.Index] = step;
        quickHistory.Commit(quickHistory.Current with { Steps = steps });
        if (editingQuick?.Id == step.Id) editingQuick = editingQuick with { Enabled = step.Enabled };
        RefreshQuickList(QuickActionList.SelectedIndex, preserveEditor: true);
        ReasonLabel.Text = step.Enabled ? "Action active." : "Action skipped. Check Active to include it again.";
    }
    /// <summary>The row's Repeating / Once dropdown. Undo restores the previous choice.</summary>
    private void QuickRepeat_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Also fires when a row is first shown; only a real change is committed.
        if (loadingQuick || sender is not Selector { DataContext: QuickRow row, SelectedIndex: >= 0 } box || box.SelectedIndex == row.RepeatIndex) return;
        if (runtime?.Snapshot.Busy == true || operations.Has(Operation.Quick)) { box.SelectedIndex = row.RepeatIndex; return; }
        bool once = box.SelectedIndex == 1;
        // The list is rebuilt after the dropdown has finished closing, not while its own row is still handling the click.
        Dispatcher.BeginInvoke(() =>
        {
            var steps = quickHistory.Current.Steps.ToArray();
            if (row.Index >= steps.Length || steps[row.Index].Id != row.Step.Id) return;
            var step = steps[row.Index] with { Once = once };
            steps[row.Index] = step;
            quickHistory.Commit(quickHistory.Current with { Steps = steps });
            if (editingQuick?.Id == step.Id) editingQuick = editingQuick with { Once = once };
            RefreshQuickList(QuickActionList.SelectedIndex, preserveEditor: true);
            ReasonLabel.Text = once ? $"Action {row.Index + 1} runs once: with Loop on, only in the first loop." : $"Action {row.Index + 1} repeats in every loop.";
        });
    }
    private void QuickUp_Click(object sender, RoutedEventArgs e) => MoveQuick(-1);
    private void QuickDown_Click(object sender, RoutedEventArgs e) => MoveQuick(1);
    private void MoveQuick(int direction)
    {
        if (QuickActionList.SelectedItem is not QuickRow row) return;
        int next = row.Index + direction; var list = quickHistory.Current.Steps.ToArray(); if (next < 0 || next >= list.Length) return;
        (list[next], list[row.Index]) = (list[row.Index], list[next]); quickHistory.Commit(quickHistory.Current with { Steps = list }); RefreshQuickList(next);
    }
    private void QuickDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (QuickActionList.SelectedItem is not QuickRow row) return;
        try
        {
            var copies = SequenceEdits.CopyWithNewIds([row.Step]);
            quickHistory.Commit(SequenceEdits.Replace(quickHistory.Current, row.Index + 1, 0, copies, quickPoints)); RefreshQuickList(row.Index + 1);
            MarkWorkshopCopies([row.Step], copies);
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
    }
    private void QuickRemove_Click(object sender, RoutedEventArgs e)
    { if (QuickActionList.SelectedItem is QuickRow row) { quickHistory.Commit(SequenceEdits.Replace(quickHistory.Current, row.Index, 1, [], quickPoints)); RefreshQuickList(); NewAction_Click(sender, e); } }
    private void QuickClear_Click(object sender, RoutedEventArgs e)
    {
        if (!operations.Idle || runtime?.Snapshot.Busy == true || runtime?.Snapshot.CleanupPending == true) return;
        quickHistory.Commit(quickHistory.Current with { Steps = [], KeyboardLayout = null });
        RefreshQuickList(); NewAction_Click(sender, e);
        ReasonLabel.Text = "Action List cleared. Undo restores it; saved macros are kept.";
    }
    private void QuickUndo_Click(object sender, RoutedEventArgs e) { var kept = (QuickActionList.SelectedItem as QuickRow)?.Step.Id; quickHistory.Undo(); KeepSelection(kept, sender, e); }
    private void QuickRedo_Click(object sender, RoutedEventArgs e) { var kept = (QuickActionList.SelectedItem as QuickRow)?.Step.Id; quickHistory.Redo(); KeepSelection(kept, sender, e); }
    /// <summary>After undo or redo the same action stays selected while it still exists; otherwise the editor is cleared.</summary>
    private void KeepSelection(Guid? id, object sender, RoutedEventArgs e)
    {
        int index = id is { } kept ? quickHistory.Current.Steps.ToList().FindIndex(s => s.Id == kept) : -1;
        RefreshQuickList(index);
        if (index < 0) NewAction_Click(sender, e);
    }

    /// <summary>
    /// Ends editing the selected action: nothing stays selected, the button turns back into a blue Add Action, and the
    /// editor keeps its values, so Add Action now adds a new action instead of changing the old one.
    /// </summary>
    private void StopEditing()
    {
        if (editingQuick is null && QuickActionList.SelectedIndex < 0) return;
        loadingQuick = true; QuickActionList.SelectedIndex = -1; loadingQuick = false;
        editingQuick = null; ActionNameBox.Text = "";
        ShowQuickFields(); ShowTestSelectedButton();
    }

    /// <summary>
    /// A click anywhere in the clicker outside the Create an action panel and the Action List's rows and buttons ends
    /// editing. The panel edits the selected action, and the list's buttons act on it, so those keep it selected.
    /// </summary>
    private void EditingWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (editingQuick is null && QuickActionList.SelectedIndex < 0 || e.OriginalSource is not DependencyObject source) return;
        // A dropdown's list opens in its own popup, outside this window's tree; a pick there belongs to the dropdown.
        if (TreeRoot(source) != this) return;
        if (IsInside(source, CreateActionCard) || IsInside(source, QuickEditorTools) || IsInside(source, TestSelectedButton)) return;
        if (IsInside(source, ActionListCard) && (Ancestor<ListBoxItem>(source) is not null || Ancestor<ButtonBase>(source) is not null
            || Ancestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null)) return;
        StopEditing();
    }
    private static bool IsInside(DependencyObject node, DependencyObject root)
    {
        for (DependencyObject? at = node; at is not null; at = TreeParent(at)) if (at == root) return true;
        return false;
    }
    private static DependencyObject TreeRoot(DependencyObject node)
    {
        var at = node;
        while (TreeParent(at) is { } parent) at = parent;
        return at;
    }
    private static T? Ancestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (DependencyObject? at = node; at is not null; at = TreeParent(at)) if (at is T found) return found;
        return null;
    }
    // Text runs are not visuals, so walk their logical parent.
    private static DependencyObject? TreeParent(DependencyObject node) => node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
