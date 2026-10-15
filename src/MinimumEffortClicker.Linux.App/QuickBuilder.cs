using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using System.Collections.ObjectModel;
using System.Globalization;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private SequenceHistory quickHistory = new(new([]));
    private IReadOnlyList<NamedPoint>? quickPoints;
    private ConditionDefinition? quickCondition;
    private SequenceStep? editingQuick;
    private string? originalQuickDefaultName;
    private Guid? loadedQuickProfile, loadedQuickMacro;
    private bool loadingQuick, quickValid, quickOperation, quickEditorOpen;
    private bool useCursorLocation = true;
    private CancellationTokenSource? quickCancellation;
    private DesktopRecorder? quickRecorder;
    private readonly ObservableCollection<QuickChildRow> quickChildren = [];
    private readonly ObservableCollection<string> quickInputNames = [];
    private readonly Dictionary<string, InputControl> quickInputs = SequenceKeys.Supported.ToDictionary(k => k.Key, k => InputControl.Key(k.Value));
    private sealed record QuickRow(int Index, SequenceStep Step, string Title, string Detail, IBrush Background, IBrush Foreground)
    {
        /// <summary>The row's Repeating (0) / Once (1) dropdown.</summary>
        public int RepeatIndex => Step.Once ? 1 : 0;
    }
    private static SolidColorBrush ColourBrush(int rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    private static IBrush ColourText(int rgb)
    {
        static double L(int c) { double s = c / 255d; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(rgb >> 16 & 255) + 0.7152 * L(rgb >> 8 & 255) + 0.0722 * L(rgb & 255) > 0.179 ? Brushes.Black : Brushes.White;
    }
    private void ShowConditionColour()
    {
        QuickEyedropper.Visibility = ConditionKindBox.SelectedIndex is 0 or 1 ? Visibility.Visible : Visibility.Collapsed;
        if (ModeBox.SelectedIndex == 3)
            ActionHint.Text = ConditionKindBox.SelectedIndex == 1 ? "Pick one background pixel to detect when it is covered. In a larger area, all matching pixels must disappear." : "Continue when a colour appears or disappears.";
        if (quickCondition is not null)
        {
            try { ConditionLabel.Text = ConditionSummary(ReadQuickCondition()); }
            catch (ArgumentException ex) { ConditionLabel.Text = ex.Message; }
        }
        bool colour = quickCondition?.Kind is ConditionKind.PixelColor or ConditionKind.RegionColor or ConditionKind.ColorDisappears;
        ConditionSwatch.Visibility = colour ? Visibility.Visible : Visibility.Collapsed;
        if (colour) { ConditionSwatch.Background = ColourBrush(quickCondition!.Color); ConditionColourLabel.Foreground = ColourText(quickCondition.Color); ConditionColourLabel.Text = $"#{quickCondition.Color:X6}"; }
    }
    private sealed class QuickChildRow
    {
        public required TimedAction Original { get; init; }
        public string Input { get; set; } = "W";
        public string Offset { get; set; } = "0";
        public string Duration { get; set; } = "20";
        public string X { get; set; } = "";
        public string Y { get; set; } = "";
        // Avalonia's DataGrid has no combo-box column; each row offers the input names itself.
        public required IReadOnlyList<string> Choices { get; init; }
    }
    private void InitializeQuickBuilder()
    {
        KeyBox.ItemsSource = SequenceKeys.Supported.Where(k => !SequenceKeys.IsModifier(InputControl.Key(k.Value))).Select(k => k.Key).ToArray();
        KeyBox.SelectedItem = "W";
        foreach (var button in MouseChoices) quickInputs[button + " mouse"] = InputControl.Mouse(button);
        QuickRecordKeyBox.Content = ShortcutKeys.Label(selectedRecordKey);
        foreach (var name in quickInputs.Keys) quickInputNames.Add(name);
        QuickChildrenGrid.ItemsSource = quickChildren;
        QuickUndoButton.IsEnabled = QuickRedoButton.IsEnabled = false;
        QuickFields.AddHandler(TextBox.TextChangedEvent, (_, _) => { if (initialized && !loadingQuick) TestResultLabel.Text = ""; });
    }
    private static string Ms(TimeSpan time) => time.TotalMilliseconds.ToString("0.###", CultureInfo.CurrentCulture);
    private static TimeSpan Milliseconds(TextBox box) => TimeSpan.FromMilliseconds(Number(box));
    private static TimeSpan OptionalPause(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? TimeSpan.Zero : Milliseconds(box);
    private SequenceDefinition ApplyListOptions(SequenceDefinition source) => QuickActions.WithClickMovement(
        QuickActions.WithCursorFree(source, false), RecordMovementBox.IsChecked == true);
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
        bool idle = !snapshot.Busy && !snapshot.CleanupPending && !quickOperation && !macroFileBusy && !ioBusy;
        Navigation.IsEnabled = !quickOperation;
        // Keep the ScrollViewer enabled while its editing controls are locked.
        QuickFields.IsEnabled = QuickEditorTools.IsEnabled = QuickListTools.IsEnabled = QuickActionList.IsEnabled = idle;
        LoopBox.IsEnabled = MinimizeOnStartBox.IsEnabled = idle;
        bool stopping = snapshot.ActionsActive || actionsStartPending;
        RunButton.Content = $"{(stopping ? "Stop" : "Start")} {MacroBindings.KeyLabel(appliedToggleKey)}";
        RunButton.SetButtonStyle(stopping ? "RedButton" : "GreenButton");
        Avalonia.Automation.AutomationProperties.SetName(RunButton, stopping ? "Stop Actions" : "Start Actions");
        RunButton.IsEnabled = stopping || !quickOperation && !macroFileBusy && !ioBusy && !screenshotBusy && !quickEditorOpen && !bindingBusy && runtime?.Control.Available == true
            && (runtime.CanResume || quickValid && runtime.CanStartActions);
        QuickRecordButton.IsEnabled = quickRecorder is not null || quickCancellation is not null || idle;
        QuickRecordButton.Content = $"{ShortcutKeys.Label(appliedRecordKey)} Record/Stop";
        LoadMacroButton.IsEnabled = idle && !ioBusy;
        MinimizeOnRecordBox.IsEnabled = RecordDelaysBox.IsEnabled = RecordMovementBox.IsEnabled = idle;
        QuickRecordKeyBox.IsEnabled = idle && !recordKeyChanging;
        ClearActionListButton.IsEnabled = idle && quickHistory.Current.Steps.Count > 0;
        ShowTestSelectedButton();
        if (quickRecorder?.Paused == true) QuickRecordStatus.Text = "Recording paused after focus changed. Finish to keep the captured actions.";
    }
    private void Loop_Changed(object sender, RoutedEventArgs e)
    {
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
        KeyFields.Visibility = ModeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        RandomFields.Visibility = ModeBox.SelectedIndex is 0 or 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        PressFields.Visibility = ModeBox.SelectedIndex is 0 or 1 ? Visibility.Visible : Visibility.Collapsed;
        AfterFields.Visibility = ModeBox.SelectedIndex is 0 or 1 or 5 ? Visibility.Visible : Visibility.Collapsed;
        MotionFields.Visibility = ModeBox.SelectedIndex == 0 && !useCursorLocation ? Visibility.Visible : Visibility.Collapsed;
        DoubleFields.Visibility = ModeBox.SelectedIndex == 0 && ClickKindBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PauseFields.Visibility = ModeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        ScreenshotActionFields.Visibility = ModeBox.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        ScanIntervalFields.Visibility = ConditionFields.Visibility = ModeBox.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        ComplexFields.Visibility = complex ? Visibility.Visible : Visibility.Collapsed;
        AddActionButton.IsEnabled = ModeBox.SelectedIndex >= 0 || complex;
        // Blue Add Action adds a new action; green Save Changes changes the selected one, so it is always clear which.
        AddActionButton.Content = editingQuick is null ? "Add Action" : "Save Changes";
        AddActionButton.Classes.Set("blue", editingQuick is null); AddActionButton.Classes.Set("green", editingQuick is not null);
        PressLabel.Text = ClickKindBox.SelectedIndex == 2 && ModeBox.SelectedIndex == 0 || HoldKeyBox.IsChecked == true && ModeBox.SelectedIndex == 1 ? "Hold duration (ms)" : "Press duration (ms)";
        ShowTimingOptions();
        Location_Changed(this, new RoutedEventArgs());
        ActionHint.Text = complex ? "Edit the recorded inputs below. Their overlap is preserved." : ModeBox.SelectedIndex switch
        { 0 => "Click at your cursor or a saved location.", 1 => "Press a key, with optional modifiers or a longer hold.", 2 => "Wait before the next action.", 3 => "Continue when a colour appears or disappears.", 4 => "Save a fullscreen PNG of all monitors.", 5 => "Turn the mouse wheel at your cursor or a saved location.", _ => "Choose an action to see its settings." };
    }
    private void ClickKind_Changed(object sender, SelectionChangedEventArgs e)
    { if (!initialized || loadingQuick) return; PressBox.Text = ClickKindBox.SelectedIndex == 2 ? "1000" : "20"; ShowQuickFields(); }
    private void HoldKey_Changed(object sender, RoutedEventArgs e)
    { if (!initialized || loadingQuick) return; PressBox.Text = HoldKeyBox.IsChecked == true ? "1000" : "20"; ShowQuickFields(); }
    private void Location_Changed(object sender, RoutedEventArgs e)
    {
        if (CursorLocationButton is null || SetLocationButton is null) return;
        CursorLocationButton.SetButtonStyle(useCursorLocation ? "GreenButton" : "StandardButton");
        SetLocationButton.SetButtonStyle(useCursorLocation ? "StandardButton" : "GreenButton");
        QuickXBox.IsEnabled = QuickYBox.IsEnabled = !useCursorLocation;
        MotionFields.Visibility = ModeBox.SelectedIndex == 0 && !useCursorLocation ? Visibility.Visible : Visibility.Collapsed;
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
        if (!int.TryParse(QuickXBox.Text ?? "", out int x) || !int.TryParse(QuickYBox.Text ?? "", out int y))
            throw new ArgumentException("Enter whole-number X and Y coordinates.");
        var result = point with { X = x, Y = y };
        if (!result.IsValid) throw new ArgumentException("X and Y must be inside the saved window size.");
        return result;
    }
    private void ConditionKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loadingQuick || ConditionLabel is null) return;
        TestResultLabel.Text = "";
        if (ConditionKindBox.SelectedIndex is 0 or 1 && quickCondition?.Kind is ConditionKind.PixelColor or ConditionKind.RegionColor or ConditionKind.ColorDisappears)
            quickCondition = quickCondition with { Kind = ConditionKindBox.SelectedIndex == 1 ? ConditionKind.ColorDisappears : ConditionKind.RegionColor };
        else quickCondition = null;
        ConditionLabel.Text = quickCondition is null ? "Choose the area and what to look for." : ConditionSummary(quickCondition);
        if (QuickEyedropper is not null) ShowConditionColour();
    }
    private ConditionDefinition ReadQuickCondition()
    {
        var condition = quickCondition ?? throw new ArgumentException("Choose an area and colour first.");
        var result = condition with { Policy = condition.Policy with { SampleInterval = QuickScanInterval() } };
        result.Validate(); return result;
    }
    private TimeSpan QuickScanInterval()
    {
        var interval = Number(QuickScanIntervalBox);
        if (interval < 100 || interval > 5000) throw new ArgumentException("Scan interval must be 100–5,000 ms. Try 500 ms.");
        return TimeSpan.FromMilliseconds(interval);
    }
    private void QuickScan_Changed(object sender, TextChangedEventArgs e)
    {
        if (!initialized || loadingQuick || quickCondition is null) return;
        TestResultLabel.Text = "";
        try { ConditionLabel.Text = ConditionSummary(ReadQuickCondition()); }
        catch (ArgumentException ex) { ConditionLabel.Text = ex.Message; }
    }
    private void QuickCondition_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy) return;
        var kind = ConditionKindBox.SelectedIndex == 1 ? ConditionKind.ColorDisappears : ConditionKind.RegionColor;
        ConditionDefinition original;
        try { original = quickCondition is null ? new(kind, new(QuickScanInterval())) : ReadQuickCondition(); }
        catch (ArgumentException ex) { ReasonLabel.Text = ex.Message; return; }
        var dialog = new ConditionEditor(this, runtime, original, coloursOnly: true);
        quickEditorOpen = true;
        try { if (dialog.ShowDialog() == true) { loadingQuick = true; quickCondition = dialog.Result; QuickScanIntervalBox.Text = Ms(quickCondition!.Policy.SampleInterval); ConditionKindBox.SelectedIndex = quickCondition.Kind switch { ConditionKind.ColorDisappears => 1, ConditionKind.RegionColor or ConditionKind.PixelColor => 0, _ => -1 }; ConditionLabel.Text = ConditionSummary(quickCondition!); ShowConditionColour(); } }
        finally { loadingQuick = false; quickEditorOpen = false; }
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
                if (CtrlBox.IsChecked == true) keys.Add(0x1D);
                if (ShiftBox.IsChecked == true) keys.Add(0x2A);
                if (AltBox.IsChecked == true) keys.Add(0x38);
                keys.Add(SequenceKeys.Supported[(string)KeyBox.SelectedItem!]);
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
            if (!int.TryParse(PositionOffsetBox.Text ?? "", out int pixels)) throw new ArgumentException("Enter a whole-number pixel offset.");
            step = step with { Motion = new(OffsetPixels: pixels) };
        }
        string enteredName = (ActionNameBox.Text ?? "").Trim();
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
        ButtonBox.SelectedIndex = ClickKindBox.SelectedIndex = 0; useCursorLocation = true; quickPoint = null; ShowQuickCoordinates();
        KeyBox.SelectedItem = "W"; CtrlBox.IsChecked = ShiftBox.IsChecked = AltBox.IsChecked = HoldKeyBox.IsChecked = false;
        PressBox.Text = "20"; DoubleGapBox.Text = "80"; AfterBox.Text = "20"; PauseDurationBox.Text = "1"; PauseUnitBox.SelectedIndex = 1;
        ScreenshotPauseBox.Text = "1000"; ScrollDirectionBox.SelectedIndex = 1; ScrollNotchesBox.Text = "3"; ScrollGapBox.Text = "30";
        quickCondition = null; QuickScanIntervalBox.Text = "500"; ConditionKindBox.SelectedIndex = 0; ConditionLabel.Text = "Select the area to scan and what to look for.";
        RandomTimingBox.IsChecked = false; MaxPressBox.Text = "40"; MaxAfterBox.Text = "80";
        VaryPositionBox.IsChecked = false; PositionOffsetBox.Text = "3";
        quickChildren.Clear(); TestResultLabel.Text = ""; AddActionButton.Content = "Add Action";
        loadingQuick = false; Location_Changed(sender, e); ShowQuickFields(); ShowConditionColour();
    }
    private QuickRow[] QuickRows(IEnumerable<SequenceStep> steps) => steps.Select((s, i) =>
        new QuickRow(i, s, $"{i + 1}. {s.Name}" + (s.Enabled ? "" : " (skipped)"), s.Condition is { } c ? ConditionSummary(c) : QuickActions.ReadScroll(s) is { } scroll ? ScrollSummary(scroll) : TimingSummary(s) + (s.Children is { Count: > 1 } children ? $" · {children.Count} inputs" : ""),
            s.Condition is { Kind: ConditionKind.PixelColor or ConditionKind.RegionColor or ConditionKind.ColorDisappears } colour ? ColourBrush(colour.Color) : Brushes.Transparent,
            s.Condition is { Kind: ConditionKind.PixelColor or ConditionKind.RegionColor or ConditionKind.ColorDisappears } ink ? ColourText(ink.Color) : (IBrush)this.FindResource("TextPrimary")!)).ToArray();
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
            { ModeBox.SelectedIndex = 3; quickCondition = s.Condition; ConditionKindBox.SelectedIndex = s.Condition!.Kind switch { ConditionKind.ColorDisappears => 1, ConditionKind.RegionColor or ConditionKind.PixelColor => 0, _ => -1 }; QuickScanIntervalBox.Text = Ms(s.Condition.Policy.SampleInterval); ConditionLabel.Text = ConditionSummary(s.Condition); }
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
                else if (uncomplicated && actions.All(c => c.Control.Kind == InputKind.ScanCode && c.Offset == TimeSpan.Zero && c.Duration == actions[0].Duration) && actions.Count(c => !SequenceKeys.IsModifier(c.Control)) == 1 && actions.Where(c => SequenceKeys.IsModifier(c.Control)).All(c => c.Control.Code is 0x1D or 0x2A or 0x38))
                {
                    ModeBox.SelectedIndex = 1; KeyBox.SelectedItem = SequenceKeys.Supported.First(k => k.Value == actions.First(c => !SequenceKeys.IsModifier(c.Control)).Control.Code).Key;
                    CtrlBox.IsChecked = actions.Any(c => c.Control.Code == 0x1D); ShiftBox.IsChecked = actions.Any(c => c.Control.Code == 0x2A); AltBox.IsChecked = actions.Any(c => c.Control.Code == 0x38);
                    HoldKeyBox.IsChecked = s.Name.StartsWith("Hold ", StringComparison.Ordinal) || actions[0].Duration.TotalMilliseconds >= 500; PressBox.Text = Ms(actions[0].Duration); AfterBox.Text = Ms(s.Duration - actions[0].Duration);
                }
                else
                {
                    quickChildren.Clear(); ComplexDurationBox.Text = Ms(s.Duration);
                    foreach (var child in actions)
                    {
                        string label = quickInputs.FirstOrDefault(k => k.Value == child.Control).Key ?? RecordingNormalizer.Label(child.Control);
                        quickInputs[label] = child.Control;
                        quickChildren.Add(new() { Choices = quickInputNames, Original = child, Input = label, Offset = Ms(child.Offset), Duration = Ms(child.Duration), X = child.Position?.X.ToString() ?? "", Y = child.Position?.Y.ToString() ?? "" });
                    }
                    foreach (var name in quickInputs.Keys) if (!quickInputNames.Contains(name)) quickInputNames.Add(name);
                }
            }
        }
        finally
        {
            VaryPositionBox.IsChecked = s.Motion?.OffsetPixels > 0;
            PositionOffsetBox.Text = (s.Motion?.OffsetPixels > 0 ? s.Motion.OffsetPixels : 3).ToString();
            MaxPressBox.Text = Ms(s.Timing?.MaximumPressDuration ?? (double.TryParse(PressBox.Text ?? "", out var press) ? TimeSpan.FromMilliseconds(press) : TimeSpan.FromMilliseconds(20)));
            MaxAfterBox.Text = Ms((s.Timing?.MaximumInterval is { } maxInterval ? maxInterval - (s.Duration - OptionalPause(AfterBox)) : s.Timing?.MaximumPause) ?? (s.Kind == StepKind.Wait ? s.Duration : double.TryParse(AfterBox.Text ?? "", out var after) ? TimeSpan.FromMilliseconds(after) : TimeSpan.FromMilliseconds(20)));
            loadingQuick = false; ShowQuickFields(); ShowConditionColour();
            try { originalQuickDefaultName = s.Kind == StepKind.Input && s.Control is { } input && s.Name == RecordingNormalizer.Label(input) ? s.Name : ReadQuickAction(useGeneratedName: true).Name; }
            catch (ArgumentException) { originalQuickDefaultName = null; }
        }
    }
    private void QuickActive_Click(object sender, RoutedEventArgs e)
    {
        if (loadingQuick || sender is not CheckBox { DataContext: QuickRow row } box || runtime?.Snapshot.Busy == true || quickOperation) return;
        var steps = quickHistory.Current.Steps.ToArray();
        var step = steps[row.Index] with { Enabled = box.IsChecked == true };
        steps[row.Index] = step;
        quickHistory.Commit(quickHistory.Current with { Steps = steps });
        if (editingQuick?.Id == step.Id) editingQuick = editingQuick with { Enabled = step.Enabled };
        RefreshQuickList(QuickActionList.SelectedIndex, preserveEditor: true);
        ReasonLabel.Text = step.Enabled ? "Action active." : "Action skipped. Check Active to include it again.";
    }
    /// <summary>The row's Repeating / Once dropdown. Undo restores the previous choice.</summary>
    private void QuickRepeat_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Also fires when a row is first shown; only a real change is committed.
        if (loadingQuick || sender is not ComboBox { DataContext: QuickRow row, SelectedIndex: >= 0 } box || box.SelectedIndex == row.RepeatIndex) return;
        if (runtime?.Snapshot.Busy == true || quickOperation) { box.SelectedIndex = row.RepeatIndex; return; }
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
        try { quickHistory.Commit(SequenceEdits.Replace(quickHistory.Current, row.Index + 1, 0, SequenceEdits.CopyWithNewIds([row.Step]), quickPoints)); RefreshQuickList(row.Index + 1); }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
    }
    private void QuickRemove_Click(object sender, RoutedEventArgs e)
    { if (QuickActionList.SelectedItem is QuickRow row) { quickHistory.Commit(SequenceEdits.Replace(quickHistory.Current, row.Index, 1, [], quickPoints)); RefreshQuickList(); NewAction_Click(sender, e); } }
    private void QuickClear_Click(object sender, RoutedEventArgs e)
    {
        if (quickOperation || macroFileBusy || ioBusy || runtime?.Snapshot.Busy == true || runtime?.Snapshot.CleanupPending == true) return;
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
    /// A press anywhere in the clicker outside the Create an action panel and the Action List's rows and buttons ends
    /// editing. The panel edits the selected action, and the list's buttons act on it, so those keep it selected.
    /// </summary>
    private void EditingWindow_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (editingQuick is null && QuickActionList.SelectedIndex < 0 || e.Source is not Avalonia.Visual source) return;
        bool Inside(Avalonia.Visual root) => source == root || Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(root, source);
        bool Within<T>() => source is T || Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(source).OfType<T>().Any();
        // A dropdown's list opens in a popup (its own top level, or the overlay layer); a pick there belongs to the dropdown.
        if (TopLevel.GetTopLevel(source) != this || Within<Avalonia.Controls.Primitives.OverlayLayer>() || Within<Avalonia.Controls.Primitives.PopupRoot>()) return;
        if (Inside(CreateActionCard) || Inside(QuickEditorTools) || Inside(TestSelectedButton)) return;
        if (Inside(ActionListCard) && (Within<ListBoxItem>() || Within<Button>() || Within<ScrollBar>())) return;
        StopEditing();
    }
}
