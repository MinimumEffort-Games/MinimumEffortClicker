using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class SequenceEditor : Window
{
    private readonly SequenceHistory history;
    private readonly DesktopRuntime runtime;
    private readonly IReadOnlyList<NamedPoint> points;
    private sealed record PointChoice(Guid? Id, string Name) { public override string ToString() => Name; }
    private readonly PointChoice[] pointChoices;
    private readonly Dictionary<string, InputControl> inputs = SequenceKeys.Supported.ToDictionary(x => x.Key, x => InputControl.Key(x.Value));
    private readonly ObservableCollection<ChildRow> children = [];
    private bool refreshing, discarded;
    private int refreshPending;
    private (string State, string Reason)? lastRuntimeStatus;
    private bool recordBusy;
    private bool resumingCapture;
    private CancellationTokenSource? recordCountdown;
    private DesktopRecorder? recorder;
    private IReadOnlyList<SequenceStep> copied = [];
    public SequenceDefinition Result => history.Current;
    public Func<SequenceDefinition, Task>? SaveDraftAsync { get; init; }
    private bool saving;
    public string? SetupGuide { get; init; }

    public SequenceEditor(SequenceDefinition sequence, DesktopRuntime runtime, IReadOnlyList<NamedPoint>? points = null, bool allowRepetition = true)
    {
        this.runtime = runtime; this.points = points?.ToArray() ?? []; history = new(sequence, this.points);
        pointChoices = [new(null, "Recorded position / current pointer"), .. this.points.Select(p => new PointChoice(p.Id, p.Name))];
        foreach (var button in Enum.GetValues<PointerButton>()) inputs[button + " mouse"] = InputControl.Mouse(button);
        inputs["Scroll up"] = new(InputKind.Scroll, 120); inputs["Scroll down"] = new(InputKind.Scroll, -120);
        inputs["Move pointer"] = new(InputKind.PointerMove, 0);
        inputs["Scroll right"] = new(InputKind.HorizontalScroll, 120); inputs["Scroll left"] = new(InputKind.HorizontalScroll, -120);
        foreach (var key in SequenceKeys.Logical) inputs[key.Key] = new(InputKind.LogicalKey, key.Value);
        foreach (var input in SequenceLibrary.AllSteps(sequence).SelectMany(s => s.Control is { } control ? new[] { control } : (s.Children ?? []).Select(c => c.Control)))
            if (!inputs.ContainsValue(input)) inputs[$"Scroll delta {input.Code}"] = input;
        InitializeComponent();
        LoopSettingsButton.IsEnabled = allowRepetition;
        InputBox.ItemsSource = inputs.Keys; ChildInputColumn.ItemsSource = inputs.Keys;
        NamedPointBox.ItemsSource = pointChoices; ChildPointColumn.ItemsSource = pointChoices;
        ChildrenGrid.ItemsSource = children;
        runtime.Changed += RuntimeChanged;
        runtime.Control.RecordingRequested += RecordHotkey;
        RecordKeyBox.ItemsSource = Enumerable.Range(1, 11).Select(x => "F" + x).ToArray(); RecordKeyBox.SelectedIndex = 9;
        Loaded += async (_, _) =>
        {
            await SetRecordKey();
            if (SetupGuide == "record") MessageLabel.Text = "Press F10 to start recording. Press it again to finish, then review and edit each step.";
            if (SetupGuide == "vision") AddCondition_Click(this, new RoutedEventArgs());
        };
        Closed += async (_, _) =>
        {
            runtime.Changed -= RuntimeChanged; runtime.Control.RecordingRequested -= RecordHotkey;
            try { await runtime.Control.ConfigureRecordingAsync(null); } catch (ObjectDisposedException) { }
        };
        Refresh(0);
    }
    private sealed class ChildRow
    {
        public Guid Id { get; init; }
        public string Input { get; set; } = "W";
        public string Offset { get; set; } = "0";
        public string Duration { get; set; } = "100";
        public ClientPoint? Position { get; init; }
        public ClientPoint? EndPosition { get; init; }
        public string PositionLabel => Describe(Position, EndPosition);
        public string X { get; set; } = "";
        public string Y { get; set; } = "";
        public string EndX { get; set; } = "";
        public string EndY { get; set; } = "";
        public PointChoice? Target { get; set; }
    }
    private void RuntimeChanged()
    {
        if (Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref refreshPending, 1) != 0) return;
        Dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref refreshPending, 0); RefreshRuntime(); });
    }
    private void RefreshRuntime()
    {
        var snapshot = runtime.Snapshot;
        PausePlaybackButton.IsEnabled = snapshot.PlaybackActive;
        ResumePlaybackButton.IsEnabled = runtime.CanResume;
        AuthoringHeader.IsEnabled = EditorBody.IsEnabled = !recordBusy;
        PauseCaptureButton.IsEnabled = recorder is { Paused: false } && !resumingCapture;
        ResumeCaptureButton.IsEnabled = recorder is { Paused: true } && !resumingCapture;
        RunConfiguredButton.IsEnabled = RunButton.IsEnabled = RunSelectedButton.IsEnabled = !recordBusy && !snapshot.Busy && !snapshot.CleanupPending;
        SaveButton.IsEnabled = !recordBusy && !snapshot.Busy && !snapshot.CleanupPending;
        RecordButton.IsEnabled = ReplaceRecordButton.IsEnabled = !recordBusy && !snapshot.Busy && !snapshot.CleanupPending;
        var status = (snapshot.State, snapshot.Reason);
        if (lastRuntimeStatus != status)
        {
            if (snapshot.Busy || lastRuntimeStatus is not null) MessageLabel.Text = snapshot.State + ": " + snapshot.Reason;
            lastRuntimeStatus = status;
        }
    }
    private static string Time(TimeSpan duration) => duration.TotalMilliseconds.ToString("0.###", CultureInfo.CurrentCulture);
    private static TimeSpan Parse(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number)
            || number < 0 || number > SequenceCompiler.MaximumDuration.TotalMilliseconds) throw new ArgumentException("Enter a duration from zero to one day.");
        return TimeSpan.FromMilliseconds(number);
    }
    private string Label(InputControl input)
    {
        if (input.Kind == InputKind.Unicode) return "Unicode text";
        var label = inputs.FirstOrDefault(x => x.Value == input).Key;
        if (label is not null) return label;
        label = $"Scroll delta {input.Code}"; inputs[label] = input;
        InputBox.ItemsSource = inputs.Keys.ToArray(); ChildInputColumn.ItemsSource = inputs.Keys.ToArray();
        return label;
    }
    private static string Describe(ClientPoint? point, ClientPoint? end) => point is null ? "Follows current pointer" :
        $"{point.Space} ({point.X}, {point.Y})" + (end is null ? "" : $" → ({end.X}, {end.Y})") + $" · {point.Width}×{point.Height} @ {point.Dpi} DPI";
    private static ClientPoint? EditPoint(ClientPoint? original, string x, string y)
    {
        if (original is null)
        {
            if (!string.IsNullOrWhiteSpace(x) || !string.IsNullOrWhiteSpace(y)) throw new ArgumentException("Create or edit a named point under Targets before assigning new coordinates.");
            return null;
        }
        if (!int.TryParse(x, out int px) || !int.TryParse(y, out int py)) throw new ArgumentException("Enter whole client-pixel coordinates.");
        var changed = original with { X = px, Y = py };
        if (!changed.IsValid) throw new ArgumentException("The point must remain inside its calibrated client area.");
        return changed;
    }
    private void Refresh(int selected)
    {
        refreshing = true;
        StepsList.ItemsSource = history.Current.Steps.Select((s, i) => $"{i + 1:00}  {(s.Enabled ? "" : "[disabled] ")}{s.Name} · "
            + (s.Flow is { } f ? (f.Kind == FlowKind.IfElse ? "If / Else" : $"Repeat until · at most {f.MaximumIterations} bodies") : s.Kind == StepKind.Subsequence ? $"call {history.Current.Subsequences!.First(b => b.Id == s.SubsequenceId).Name}" : s.Condition is { } c ? $"wait ≤ {c.Policy.Timeout.TotalSeconds:0.###}s / attempt" : $"{Time(s.Duration)} ms")).ToArray();
        StepsList.SelectedIndex = Math.Clamp(selected, -1, history.Current.Steps.Count - 1);
        UndoButton.IsEnabled = history.CanUndo; RedoButton.IsEnabled = history.CanRedo;
        var plan = SequenceCompiler.Compile(history.Current, points);
        SummaryLabel.Text = $"{history.Current.Steps.Count} actions · {plan.Duration.TotalSeconds:0.###}s fixed timing · ≤ {plan.WorstCaseDuration.TotalSeconds:0.###}s including bounded paths, before pacing"
            + $" · repeat {plan.Repetition}" + (plan.KeyboardLayout is null ? "" : $" · recorded layout {plan.KeyboardLayout}");
        refreshing = false; Selection_Changed(this, null!); RefreshRuntime();
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || StepName is null) return;
        int index = StepsList.SelectedIndex;
        if (index < 0) return;
        var step = history.Current.Steps[index];
        StepEnabled.IsChecked = step.Enabled; StepName.Text = step.Name; DurationBox.Text = Time(step.Duration);
        StepNotes.Text = step.Notes;
        TextInspector.Visibility = step.Kind == StepKind.Text ? Visibility.Visible : Visibility.Collapsed;
        TextContent.Text = step.Text ?? "";
        DurationBox.IsEnabled = step.Kind is not (StepKind.Condition or StepKind.Subsequence or StepKind.ConditionalFlow);
        EditConditionButton.Visibility = step.Kind == StepKind.Condition ? Visibility.Visible : Visibility.Collapsed;
        EditFlowButton.Visibility = step.Kind == StepKind.ConditionalFlow ? Visibility.Visible : Visibility.Collapsed;
        InputBox.IsEnabled = step.Kind == StepKind.Input;
        InputBox.SelectedItem = step.Control is { } input ? Label(input) : "W";
        NamedPointBox.SelectedItem = pointChoices.First(p => p.Id == step.TargetId);
        NamedPointBox.IsEnabled = step.Kind == StepKind.Input;
        PositionLabel.Text = step.Control?.Kind is InputKind.MouseButton or InputKind.Scroll or InputKind.HorizontalScroll or InputKind.PointerMove ? Describe(step.Position, step.EndPosition) : "";
        if (step.TargetId is { } targetId)
        {
            var point = points.First(p => p.Id == targetId);
            PositionLabel.Text = $"Named point: {point.Name} · {Describe(point.Position, step.EndPosition)}. Edit its shared position under Targets.";
        }
        PositionFields.Visibility = step.Position is null ? Visibility.Collapsed : Visibility.Visible;
        PositionX.Text = step.Position?.X.ToString() ?? ""; PositionY.Text = step.Position?.Y.ToString() ?? "";
        EndX.Text = step.EndPosition?.X.ToString() ?? ""; EndY.Text = step.EndPosition?.Y.ToString() ?? "";
        EndX.IsEnabled = EndY.IsEnabled = step.EndPosition is not null;
        GroupInspector.Visibility = step.Kind == StepKind.TimedGroup ? Visibility.Visible : Visibility.Collapsed;
        children.Clear();
        foreach (var child in step.Children ?? []) children.Add(new() { Id = child.Id, Input = Label(child.Control), Offset = Time(child.Offset), Duration = Time(child.Duration), Position = child.Position, EndPosition = child.EndPosition,
            X = child.Position?.X.ToString() ?? "", Y = child.Position?.Y.ToString() ?? "", EndX = child.EndPosition?.X.ToString() ?? "", EndY = child.EndPosition?.Y.ToString() ?? "",
            Target = pointChoices.First(p => p.Id == child.TargetId) });
    }
    private void Edit(Func<SequenceDefinition, SequenceDefinition> change, string message)
    {
        try { int selected = StepsList.SelectedIndex; history.Commit(change(history.Current)); Refresh(selected); MessageLabel.Text = message; }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or InvalidOperationException) { MessageLabel.Text = ex.Message; }
    }
    private void Add(SequenceStep step)
    {
        int at = StepsList.SelectedIndex + 1;
        Edit(s => SequenceEdits.Replace(s, at, 0, [step], points), "Action added to the draft.");
        Refresh(Math.Min(at, history.Current.Steps.Count - 1));
    }
    private void AddInput_Click(object sender, RoutedEventArgs e) => Add(SequenceStep.Input("Tap W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(50)));
    private void AddWait_Click(object sender, RoutedEventArgs e) => Add(SequenceStep.Wait(TimeSpan.FromMilliseconds(500)));
    private void AddText_Click(object sender, RoutedEventArgs e) => Add(SequenceStep.TypeText("Example text"));
    private void AddGroup_Click(object sender, RoutedEventArgs e) => Add(SequenceStep.Group("Hold W while tapping Space", TimeSpan.FromSeconds(1),
        [new(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(0x11), TimeSpan.FromSeconds(1)),
         new(Guid.NewGuid(), TimeSpan.FromMilliseconds(200), InputControl.Key(0x39), TimeSpan.FromMilliseconds(50))]));
    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyActionEdit();
    private bool ApplyActionEdit()
    {
        int index = StepsList.SelectedIndex; if (index < 0) return true;
        if (!ChildrenGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !ChildrenGrid.CommitEdit(DataGridEditingUnit.Row, true))
        { MessageLabel.Text = "Correct the timed-child edit before continuing."; return false; }
        try
        {
            var s = history.Current;
            var old = s.Steps[index];
            var updated = old with { Name = StepName.Text.Trim(), Notes = StepNotes.Text, Enabled = StepEnabled.IsChecked == true, Duration = Parse(DurationBox.Text),
                Text = old.Kind == StepKind.Text ? TextContent.Text : null,
                Control = old.Kind == StepKind.Input ? inputs[(string)InputBox.SelectedItem] : null,
                TargetId = old.Kind == StepKind.Input ? (NamedPointBox.SelectedItem as PointChoice)?.Id : null,
                Position = (NamedPointBox.SelectedItem as PointChoice)?.Id is not null ? null : EditPoint(old.Position, PositionX.Text, PositionY.Text), EndPosition = EditPoint(old.EndPosition, EndX.Text, EndY.Text),
                Children = old.Kind == StepKind.TimedGroup ? children.Select(c => new TimedAction(c.Id, Parse(c.Offset), inputs[c.Input], Parse(c.Duration),
                    c.Target?.Id is not null ? null : EditPoint(c.Position, c.X, c.Y), EditPoint(c.EndPosition, c.EndX, c.EndY), c.Target?.Id)).ToArray() : null };
            // Array identity must not turn an unchanged inspector into an undo entry.
            if (updated with { Children = old.Children } == old && (updated.Children ?? []).SequenceEqual(old.Children ?? [])) return true;
            history.Commit(SequenceEdits.Replace(s, index, 1, [updated], points));
            Refresh(index);
            MessageLabel.Text = "Action updated. Active playback uses its original snapshot.";
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or InvalidOperationException)
        { MessageLabel.Text = ex.Message; return false; }
    }
    private void AddChild_Click(object sender, RoutedEventArgs e) => children.Add(new() { Id = Guid.NewGuid(), Input = "Space", Offset = "400", Duration = "50" });
    private void RemoveChild_Click(object sender, RoutedEventArgs e) { if (ChildrenGrid.SelectedItem is ChildRow row) children.Remove(row); }
    private void Undo_Click(object sender, RoutedEventArgs e) { history.Undo(); Refresh(StepsList.SelectedIndex); MessageLabel.Text = "Undo restored the previous draft."; }
    private void Redo_Click(object sender, RoutedEventArgs e) { history.Redo(); Refresh(StepsList.SelectedIndex); MessageLabel.Text = "Redo restored the edit."; }
    private void Move(int direction)
    {
        int index = StepsList.SelectedIndex, target = index + direction;
        if (index < 0 || target < 0 || target >= history.Current.Steps.Count) return;
        Edit(s => { var copy = s.Steps.ToArray(); (copy[index], copy[target]) = (copy[target], copy[index]); return s with { Steps = copy }; }, "Action moved.");
        Refresh(target);
    }
    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var indices = SelectedIndices(); if (indices.Length == 0) return;
        Edit(s => SequenceEdits.Replace(s, indices[^1] + 1, 0, SequenceEdits.CopyWithNewIds(indices.Select(i => s.Steps[i])), points), "Selected actions duplicated with independent identities.");
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var indices = SelectedIndices().ToHashSet();
        if (indices.Count != 0) Edit(s => s with { Steps = s.Steps.Where((_, i) => !indices.Contains(i)).ToArray() }, "Selected actions removed. Undo is available.");
    }
    private int[] SelectedIndices() => StepsList.SelectedItems.Cast<object>().Select(item => StepsList.Items.IndexOf(item)).Order().ToArray();
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        copied = SequenceEdits.Snapshot(new(SelectedIndices().Select(i => history.Current.Steps[i]).ToArray())).Steps;
        MessageLabel.Text = $"{copied.Count} actions copied within this editor.";
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        int after = SelectedIndices().LastOrDefault(-1) + 1;
        if (copied.Count != 0) Edit(s => SequenceEdits.Replace(s, after, 0, SequenceEdits.CopyWithNewIds(copied), points), "Copied actions inserted after the selection. Undo is available.");
    }
    private void Steps_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control) return;
        if (e.Key == System.Windows.Input.Key.C) { Copy_Click(sender, e); e.Handled = true; }
        if (e.Key == System.Windows.Input.Key.V) { Paste_Click(sender, e); e.Handled = true; }
    }
    private void PreviewTimingEdit(Func<SequenceDefinition, SequenceDefinition> change)
    {
        try
        {
            var next = change(history.Current);
            var before = SequenceCompiler.Compile(history.Current, points); var after = SequenceCompiler.Compile(next, points);
            if (AppDialog.Show(this, $"Duration: {before.Duration.TotalSeconds:0.###} → {after.Duration.TotalSeconds:0.###} seconds.\n\nOnly idle waits change. Holds, drags and timed-group internals keep their timing. No input will be sent.\n\nApply this edit? Undo remains available.", "Preview timing edit", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                Edit(_ => next, "Idle timing updated after preview. Holds and overlap preserved.");
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { MessageLabel.Text = ex.Message; }
    }
    private void FixedGaps_Click(object sender, RoutedEventArgs e) => PreviewTimingEdit(s => SequenceEdits.ChangeIdleGaps(s, _ => Parse(GapBox.Text), points));
    private void CapGaps_Click(object sender, RoutedEventArgs e) => PreviewTimingEdit(s => SequenceEdits.ChangeIdleGaps(s, gap => gap > Parse(GapBox.Text) ? Parse(GapBox.Text) : gap, points));
    private void RemoveWaits_Click(object sender, RoutedEventArgs e) => PreviewTimingEdit(s => s with { Steps = s.Steps.Where(step => step.Kind != StepKind.Wait).ToArray() });
    private void Timing_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyActionEdit()) return;
        var dialog = new TimingWindow(this, SequenceCompiler.Compile(history.Current, points), Label);
        if (dialog.ShowDialog() == true && dialog.SelectedStepIndex is { } index)
        { StepsList.SelectedIndex = index; StepsList.ScrollIntoView(StepsList.SelectedItem); }
    }
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyActionEdit()) return;
        var plan = SequenceCompiler.Compile(history.Current, points);
        AppDialog.Show(this, $"NO INPUT WILL BE SENT\n\n{plan.Duration.TotalSeconds:0.###}s fixed timing; ≤ {plan.WorstCaseDuration.TotalSeconds:0.###}s including bounded decisions, before click-cap stretching.\nBranch bodies are conditional; the transitions below show the outer path only.\n\n"
            + string.Join("\n", plan.Conditions.Take(10).Select(c => $"{c.Name}: {c.Definition.Kind}, timeout {c.Definition.Policy.Timeout.TotalSeconds:0.###}s, {c.Definition.Policy.TimeoutAction}")) + "\n\n" +
            string.Join("\n", plan.Inputs.Take(30).Select(x => $"{x.Offset.TotalMilliseconds,8:0.###} ms  {Label(x.Control)} {(x.Down ? "down / scroll" : "up")}")) +
            (plan.Inputs.Count > 30 ? "\n…remaining transitions omitted from this summary" : ""), "Rehearsal · no input");
    }
    private void Run_Click(object sender, RoutedEventArgs e) { if (ApplyActionEdit()) Start(history.Current with { Repetition = new() }); }
    private void RunConfigured_Click(object sender, RoutedEventArgs e) { if (ApplyActionEdit()) Start(history.Current); }
    private void FinishLoop_Click(object sender, RoutedEventArgs e)
    { MessageLabel.Text = runtime.FinishSequenceLoop() ? "Finishing this sequence loop; Stop All still stops immediately." : "Start sequence playback first. This command does not start a run."; }
    private void RunSelected_Click(object sender, RoutedEventArgs e) { var indices = SelectedIndices(); if (ApplyActionEdit() && indices.Length > 0) Start(history.Current with { Steps = indices.Select(i => history.Current.Steps[i]).ToArray(), Repetition = new() }); }
    private void Start(SequenceDefinition source)
    {
        try
        {
            var plan = SequenceCompiler.Compile(source, points);
            if (runtime.BringTargetForward()) runtime.TryStart(plan, TimeSpan.FromSeconds(3));
            MessageLabel.Text = runtime.Snapshot.Reason;
        }
        catch (ArgumentException ex) { MessageLabel.Text = ex.Message; }
    }
    private void Stop_Click(object sender, RoutedEventArgs e) { recordCountdown?.Cancel(); recorder?.Stop(); runtime.Stop(); MessageLabel.Text = "Stopping playback or capture and releasing owned input."; }
    private async void Record_Click(object sender, RoutedEventArgs e) => await Record(false);
    private async void ReplaceRecord_Click(object sender, RoutedEventArgs e) => await Record(true);
    private void PauseCapture_Click(object sender, RoutedEventArgs e) { recorder?.Pause(); RefreshRuntime(); }
    private async void ResumeCapture_Click(object sender, RoutedEventArgs e)
    {
        if (recorder is not { Paused: true } capture || recordCountdown is null || resumingCapture) return;
        resumingCapture = true; RefreshRuntime();
        try
        {
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            MessageLabel.Text = "Recording resumes in 3 seconds. Release held keys and buttons.";
            await Task.Delay(TimeSpan.FromSeconds(3), recordCountdown.Token);
            capture.Resume();
        }
        catch (OperationCanceledException) { MessageLabel.Text = "Resume cancelled."; }
        catch (InvalidOperationException ex) { MessageLabel.Text = ex.Message; }
        finally { resumingCapture = false; RefreshRuntime(); }
    }
    private void RecordHotkey() => Dispatcher.BeginInvoke(async () =>
    {
        if (recorder is not null) recorder.Stop();
        else if (recordCountdown is not null) await recordCountdown.CancelAsync();
        else await Record(false);
    });
    private async void ApplyRecordKey_Click(object sender, RoutedEventArgs e) => await SetRecordKey();
    private async Task SetRecordKey()
    {
        try
        {
            bool ready = await runtime.Control.ConfigureRecordingAsync(0x70 + RecordKeyBox.SelectedIndex);
            RecordKeyStatus.Text = ready ? $"{RecordKeyBox.SelectedItem}: record / finish" : "Key is busy. Choose another record key.";
        }
        catch (InvalidOperationException ex) { RecordKeyStatus.Text = ex.Message; }
    }
    private async Task Record(bool replace)
    {
        if (recordBusy || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) return;
        var indices = SelectedIndices(); int selection = indices.FirstOrDefault(-1);
        if (replace && (selection < 0 || indices[^1] - selection + 1 != indices.Length)) { MessageLabel.Text = "Select one contiguous region of actions to replace."; return; }
        var channels = new RecordingChannels(CaptureKeysBox.IsChecked == true, CaptureMouseBox.IsChecked == true, CaptureScrollBox.IsChecked == true, CapturePathsBox.IsChecked == true);
        if (!channels.Keyboard && !channels.MouseButtons && !channels.Scroll && !channels.PointerPath) { MessageLabel.Text = "Choose at least one recording channel."; return; }
        recordBusy = true;
        using var countdown = new CancellationTokenSource(); recordCountdown = countdown;
        void CancelCountdown(string reason) { _ = countdown.CancelAsync(); }
        runtime.Control.StopRequested += CancelCountdown;
        RefreshRuntime();
        try
        {
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            MessageLabel.Text = "Recording starts in 3 seconds. Perform your actions, then press the record key again to finish.";
            await Task.Delay(TimeSpan.FromSeconds(3), countdown.Token);
            using (var capture = new DesktopRecorder(runtime, channels))
            {
                recorder = capture;
                RefreshRuntime();
                string reason = await capture.Finished;
                var draft = await capture.GetDraftAsync();
                recorder = null;
                if (history.Current.KeyboardLayout is { } existingLayout && draft.Sequence.KeyboardLayout is { } capturedLayout
                    && !string.Equals(existingLayout, capturedLayout, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("This draft uses another keyboard layout. Record into a separate sequence instead.");
                if (!draft.Sequence.Steps.Any(s => s.Kind != StepKind.Wait)) { MessageLabel.Text = "No supported input captured. " + reason; return; }
                string warnings = string.Join("\n", draft.Warnings);
                if (AppDialog.Show(this, $"{reason}\n\n{draft.Sequence.Steps.Count} editable actions, including idle waits.\n{warnings}\n\nKeep this recording in the draft? Playback remains stopped.", "Review recording", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                { MessageLabel.Text = "Recording discarded. Existing actions are unchanged."; return; }
                Edit(s => SequenceEdits.Replace(s, replace ? selection : s.Steps.Count, replace ? indices.Length : 0, draft.Sequence.Steps, points)
                    with { KeyboardLayout = s.KeyboardLayout ?? draft.Sequence.KeyboardLayout },
                    replace ? "Selected action replaced. Undo restores the original region." : "Recording appended. Review and edit before saving.");
            }
        }
        catch (OperationCanceledException) { MessageLabel.Text = "Recording countdown cancelled. No capture started."; }
        catch (Exception ex) { MessageLabel.Text = "Recording stopped: " + ex.Message; }
        finally
        {
            runtime.Control.StopRequested -= CancelCountdown;
            recorder = null; recordCountdown = null; recordBusy = false; RefreshRuntime();
        }
    }
    private void Cleanup_Click(object sender, RoutedEventArgs e) { runtime.RetryCleanup(); MessageLabel.Text = runtime.Snapshot.Reason; RefreshRuntime(); }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        if (history.Current.Steps.Count == 0) { MessageLabel.Text = "Add an action before saving."; return; }
        if (!ApplyActionEdit()) return;
        saving = true; IsEnabled = false;
        try
        {
            if (SaveDraftAsync is not null) await SaveDraftAsync(history.Current);
            saving = false;
            DialogResult = true;
        }
        catch (Exception ex) { MessageLabel.Text = "Save failed; your draft and undo history are still open. Resolve the problem and retry Save. " + ex.Message; }
        finally { saving = false; IsEnabled = true; }
    }
    private void Discard_Click(object sender, RoutedEventArgs e) { discarded = true; Close(); }
    private void Editor_Closing(object? sender, CancelEventArgs e)
    {
        if (saving) { e.Cancel = true; return; }
        if (recordBusy) { recordCountdown?.Cancel(); recorder?.Stop(); e.Cancel = true; MessageLabel.Text = "Capture is ending. Review or discard the draft before closing."; return; }
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending)
        { runtime.Stop("Editor closing."); e.Cancel = true; MessageLabel.Text = "Wait for cleanup before closing. Release physical holds, then use Retry cleanup if needed."; return; }
        if (DialogResult != true && history.CanUndo && !discarded && AppDialog.Show(this, "Discard unsaved sequence changes?", "Close editor", MessageBoxButton.YesNo) != MessageBoxResult.Yes) e.Cancel = true;
    }
}
