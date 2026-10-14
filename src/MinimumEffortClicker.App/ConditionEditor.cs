using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

internal sealed class ConditionEditor : Window
{
    public ConditionDefinition? Result { get; private set; }
    public ConditionEditor(Window owner, DesktopRuntime runtime, ConditionDefinition? original, bool coloursOnly = false)
    {
        Owner = owner; Title = coloursOnly ? "Wait for colour" : "Wait for image or colour"; Width = 510; Height = 580;
        MinWidth = 480; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "TextPrimary");
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox) })
            if (owner.TryFindResource(type) is Style style) Resources[type] = style;
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var panel = new StackPanel();
        grid.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = grid;
        var fieldRows = new Dictionary<TextBox, StackPanel>();
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) });
        TextBox Field(string label, string value)
        {
            var row = new StackPanel(); row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) });
            var box = new TextBox { Text = value }; AutomationProperties.SetName(box, label); row.Children.Add(box); panel.Children.Add(row); fieldRows[box] = row; return box;
        }
        Label("Choose what to wait for and select an area in your app. The wait keeps looking until it matches, then the next action runs. F9 stops it.");
        Label("Wait until");
        var kind = new ComboBox { ItemsSource = new[] { "A region changes from its first sample", "A pixel matches an RGB colour", "The selected window is in the foreground", "An image appears in a region", "An image disappears from a region", "A colour appears anywhere in a region", "A colour disappears from the selected area" }, SelectedIndex = (int)(original?.Kind ?? ConditionKind.ImageAppears) };
        if (coloursOnly)
        {
            var choices = new[] { new { Label = "A Colour appears", Value = ConditionKind.RegionColor }, new { Label = "A Colour disappears", Value = ConditionKind.ColorDisappears } };
            kind.ItemsSource = choices; kind.DisplayMemberPath = "Label"; kind.SelectedValuePath = "Value";
            kind.SelectedValue = original?.Kind == ConditionKind.ColorDisappears ? ConditionKind.ColorDisappears : ConditionKind.RegionColor;
        }
        int KindIndex() => coloursOnly ? (int)(ConditionKind)kind.SelectedValue : kind.SelectedIndex;
        AutomationProperties.SetName(kind, "Condition type"); panel.Children.Add(kind);
        var selectArea = new Button { Content = "Select area… (drag on a snapshot)", Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(selectArea);
        var region = original?.Region;
        var x = Field("Client X", (region?.Origin.X ?? 0).ToString()); var y = Field("Client Y", (region?.Origin.Y ?? 0).ToString());
        var width = Field("Region width · 1–256 pixels", (region?.Width ?? 1).ToString()); var height = Field("Region height · 1–256 pixels", (region?.Height ?? 1).ToString());
        var rgb = Field("RGB colour · six hexadecimal digits", (original?.Color ?? 0xFFFFFF).ToString("X6"));
        var pickColour = new Button { Content = "Eyedropper · pick colour…", Margin = new Thickness(0, 6, 0, 6) }; panel.Children.Add(pickColour);
        var colourPreview = new Border { Height = 28, Margin = new Thickness(0, 0, 0, 6) }; panel.Children.Add(colourPreview);
        void UpdateColour()
        {
            colourPreview.Background = int.TryParse(rgb.Text.Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 0xFFFFFF
                ? new SolidColorBrush(Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value)) : Brushes.Transparent;
        }
        rgb.TextChanged += (_, _) => UpdateColour(); UpdateColour();
        var tolerance = Field("Per-channel tolerance · 0–255", (original?.Tolerance ?? 8).ToString());
        var fraction = Field("Changed fraction · greater than 0, up to 1", (original?.ChangedFraction ?? 0.1).ToString(CultureInfo.CurrentCulture));
        var recalibrate = new CheckBox { Content = "Calibrate against the selected window's current size and DPI", IsChecked = region is null, Margin = new Thickness(0, 8, 0, 12) };
        panel.Children.Add(recalibrate);
        recalibrate.SetResourceReference(ForegroundProperty, "TextPrimary");
        ImageTemplate? template = original?.Template;
        var templatePanel = new StackPanel(); panel.Children.Add(templatePanel);
        var thumbnail = new Image { MaxWidth = 128, MaxHeight = 128, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        var templateLabel = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        var captureButton = new Button { Content = "Capture template from this area" };
        templatePanel.Children.Add(thumbnail); templatePanel.Children.Add(templateLabel); templatePanel.Children.Add(captureButton);
        void ShowTemplate()
        {
            if (template is null) { thumbnail.Source = null; templateLabel.Text = "Capture a 1–64 pixel region, then enlarge the search region if needed. Templates are stored locally with the saved profile and included in exports."; return; }
            var sample = template.Decode();
            thumbnail.Source = BitmapSource.Create(sample.Width, sample.Height, 96, 96, PixelFormats.Bgra32, null, sample.Pixels.ToArray(), sample.Width * 4);
            templateLabel.Text = $"Template: {sample.Width} × {sample.Height} pixels. Stored only when you save the profile. Multiple matches stop playback for review.";
        }
        ShowTemplate();
        var policy = original?.Policy ?? new(TimeSpan.FromMilliseconds(500));
        var interval = Field("Scan interval · milliseconds (100–5000)", policy.SampleInterval.TotalMilliseconds.ToString(CultureInfo.CurrentCulture));
        var stable = Field("Consecutive fresh matching samples · 1–10", policy.StableSamples.ToString());
        var advancedPanel = new StackPanel();
        foreach (var field in new[] { x, y, width, height, tolerance, fraction, interval, stable })
        {
            panel.Children.Remove(fieldRows[field]); advancedPanel.Children.Add(fieldRows[field]);
        }
        panel.Children.Remove(recalibrate); advancedPanel.Children.Add(recalibrate);
        var advanced = new Expander { Header = "Advanced: coordinates, scan speed and matching", Content = advancedPanel, Margin = new Thickness(0, 8, 0, 8) };
        advanced.SetResourceReference(ForegroundProperty, "TextPrimary"); panel.Children.Add(advanced);
        Label("If the Click target window goes behind another window, the wait pauses and continues by itself when the window is back in front.");
        void RefreshFields()
        {
            bool visual = KindIndex() != 2;
            selectArea.Visibility = visual ? Visibility.Visible : Visibility.Collapsed;
            foreach (var field in new[] { x, y, tolerance }) fieldRows[field].Visibility = visual ? Visibility.Visible : Visibility.Collapsed;
            foreach (var field in new[] { width, height }) fieldRows[field].Visibility = KindIndex() == 0 || KindIndex() >= 3 ? Visibility.Visible : Visibility.Collapsed;
            fieldRows[fraction].Visibility = KindIndex() == 0 ? Visibility.Visible : Visibility.Collapsed;
            templatePanel.Visibility = KindIndex() is 3 or 4 ? Visibility.Visible : Visibility.Collapsed;
            fieldRows[rgb].Visibility = KindIndex() is 1 or 5 or 6 ? Visibility.Visible : Visibility.Collapsed;
            pickColour.Visibility = colourPreview.Visibility = fieldRows[rgb].Visibility;
            recalibrate.Visibility = visual ? Visibility.Visible : Visibility.Collapsed;
            x.IsEnabled = y.IsEnabled = tolerance.IsEnabled = recalibrate.IsEnabled = visual;
            width.IsEnabled = height.IsEnabled = KindIndex() == 0 || KindIndex() >= 3;
            fraction.IsEnabled = KindIndex() == 0;
            rgb.IsEnabled = KindIndex() is 1 or 5 or 6;
        }
        kind.SelectionChanged += (_, _) => RefreshFields(); RefreshFields();
        var footer = new StackPanel(); Grid.SetRow(footer, 1); grid.Children.Add(footer);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) }; footer.Children.Add(error);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var save = new Button { Content = "Use condition", IsDefault = true }; var cancel = new Button { Content = "Cancel", IsCancel = true };
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        CancellationTokenSource? pending = null;
        bool closed = false;
        Closed += (_, _) => { closed = true; if (pending is not null) _ = pending.CancelAsync(); };
        CaptureRegion ReadRegion()
        {
            int px = int.Parse(x.Text), py = int.Parse(y.Text);
            var frame = recalibrate.IsChecked == true || region is null ? runtime.GetTargetFrame() : null;
            // Desktop mode stores areas in absolute screen pixels so they work on any monitor.
            var point = frame is null ? region!.Origin with { X = px, Y = py }
                : new ClientPoint(px, py, frame.Width, frame.Height, frame.Dpi, runtime.IsDesktopMode ? CoordinateSpace.Desktop : CoordinateSpace.WindowClient);
            var result = new CaptureRegion(point, KindIndex() == 1 ? 1 : int.Parse(width.Text), KindIndex() == 1 ? 1 : int.Parse(height.Text));
            result.Validate(); return result;
        }
        captureButton.Click += async (_, _) =>
        {
            using var countdown = new CancellationTokenSource(); pending = countdown;
            void StopCapture(string reason) { _ = countdown.CancelAsync(); }
            runtime.Control.StopRequested += StopCapture;
            panel.IsEnabled = save.IsEnabled = false;
            try
            {
                var selectedRegion = ReadRegion();
                if (selectedRegion.Width > 64 || selectedRegion.Height > 64) throw new ArgumentException("Capture at most 64 × 64 pixels, then enlarge the search region.");
                RasterSample sample;
                if (selectedRegion.Origin.Space == CoordinateSpace.Desktop)
                {
                    // A monitor area: photograph the monitors with the clicker out of the way, then cut the area out.
                    if (Owner is not MainWindow main) throw new InvalidOperationException("Open this from the clicker's Actions page.");
                    error.Text = "Capturing from the monitor. Stop All cancels.";
                    var (monitors, pictures) = await main.CaptureMonitorPicturesAsync(countdown.Token);
                    sample = MainWindow.CutFromMonitorPictures(monitors, pictures, selectedRegion);
                }
                else
                {
                    // Read from the window itself; it can stay behind this dialog.
                    error.Text = "Capturing from the selected app. Stop All cancels.";
                    sample = await Task.Run(() => runtime.CaptureTemplate(selectedRegion, countdown.Token), countdown.Token);
                }
                countdown.Token.ThrowIfCancellationRequested();
                template = ImageTemplate.FromSample(sample); ShowTemplate();
                error.Text = "Template captured into this draft. Playback remains stopped.";
            }
            catch (OperationCanceledException) { error.Text = "Template capture cancelled."; }
            catch (Exception ex) { error.Text = ex.Message; AppLog.Failure("Capture image template", ex); if (!closed) Activate(); }
            finally
            {
                runtime.Control.StopRequested -= StopCapture; pending = null;
                if (!closed) { panel.IsEnabled = save.IsEnabled = true; RefreshFields(); }
            }
        };
        async Task SelectArea(bool colourOnly)
        {
            panel.IsEnabled = save.IsEnabled = false;
            using var selection = new CancellationTokenSource(); pending = selection;
            void StopSelection(string reason) { _ = selection.CancelAsync(); }
            runtime.Control.StopRequested += StopSelection;
            try
            {
                int side = colourOnly || KindIndex() == 1 ? 1 : 256;
                RegionPicker picker;
                if (runtime.IsDesktopMode)
                {
                    // Desktop mode: pick on any monitor.
                    if (Owner is not MainWindow main) throw new InvalidOperationException("Open this from the clicker's Actions page.");
                    var (monitors, pictures) = await main.CaptureMonitorPicturesAsync(selection.Token);
                    selection.Token.ThrowIfCancellationRequested();
                    if (closed) return;
                    Activate();
                    picker = new RegionPicker(this, monitors, pictures, runtime.GetTargetFrame(), side);
                }
                else
                {
                    // The picture is read from the window itself, so nothing moves and this dialog stays in front.
                    var (snapshot, frame) = await Task.Run(() => runtime.CaptureTargetPicture(selection.Token), selection.Token);
                    selection.Token.ThrowIfCancellationRequested();
                    if (closed) return;
                    picker = new RegionPicker(this, snapshot, frame, side);
                }
                using var abortPicker = selection.Token.Register(() => Dispatcher.BeginInvoke(() => picker.Close()));
                if (picker.ShowDialog() == true && picker.Region is { } chosen)
                {
                    if (!colourOnly || region is null)
                    {
                        region = chosen; recalibrate.IsChecked = false;
                        x.Text = chosen.Origin.X.ToString(); y.Text = chosen.Origin.Y.ToString();
                        width.Text = chosen.Width.ToString(); height.Text = chosen.Height.ToString();
                    }
                    if (KindIndex() is 1 or 5 or 6) rgb.Text = picker.Color.ToString("X6");
                    if (KindIndex() is 3 or 4 && template is null && chosen.Width <= 64 && chosen.Height <= 64)
                    { template = ImageTemplate.FromSample(picker.SelectedPixels()); ShowTemplate(); }
                    error.Text = colourOnly ? "Colour selected. Your existing search area is preserved." : "Area selected. Resize the app only before selecting a new area.";
                }
            }
            catch (OperationCanceledException) { error.Text = "Selection cancelled."; }
            catch (Exception ex) { error.Text = ex.Message; AppLog.Failure(colourOnly ? "Eyedropper" : "Select area", ex); if (!closed) Activate(); }
            finally { runtime.Control.StopRequested -= StopSelection; pending = null; if (!closed) { panel.IsEnabled = save.IsEnabled = true; RefreshFields(); } }
        }
        selectArea.Click += async (_, _) => await SelectArea(false);
        pickColour.Click += async (_, _) => await SelectArea(true);
        save.Click += (_, _) =>
        {
            try
            {
                CaptureRegion? calibrated = null;
                if (KindIndex() != 2)
                {
                    calibrated = ReadRegion();
                }
                var definition = new ConditionDefinition((ConditionKind)KindIndex(),
                    new(TimeSpan.FromMilliseconds(double.Parse(interval.Text, CultureInfo.CurrentCulture)), int.Parse(stable.Text)),
                    calibrated, KindIndex() is 1 or 5 or 6 ? int.Parse(rgb.Text.Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0,
                    KindIndex() == 2 ? (byte)0 : byte.Parse(tolerance.Text), KindIndex() == 0 ? double.Parse(fraction.Text, CultureInfo.CurrentCulture) : 1,
                    KindIndex() is 3 or 4 ? template : null);
                definition.Validate(); Result = definition; DialogResult = true;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or InvalidOperationException) { error.Text = ex.Message; }
        };
    }
}
