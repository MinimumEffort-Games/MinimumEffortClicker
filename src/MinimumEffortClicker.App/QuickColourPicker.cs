using System.Windows;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private async void QuickEyedropper_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || operations.Has(Operation.Quick)) return;
        operations.Begin(Operation.Quick); operations.Begin(Operation.Dialog);
        using var cancellation = new CancellationTokenSource(); quickCancellation = cancellation;
        void Cancel(string reason) { _ = cancellation.CancelAsync(); }
        runtime.Control.StopRequested += Cancel; RefreshStatus();
        try
        {
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            await Task.Delay(350, cancellation.Token);
            var frame = runtime.GetTargetFrame();
            var snapshot = await Task.Run(() => runtime.CaptureAuthoringPreview(cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested(); Activate();
            var picker = new RegionPicker(this, snapshot, frame, 1);
            using var abortPicker = cancellation.Token.Register(() => Dispatcher.BeginInvoke(() => picker.Close()));
            if (picker.ShowDialog() != true || picker.Region is not { } point) return;
            // Sampling a new colour preserves an already chosen search region.
            quickCondition = new(ConditionKindBox.SelectedIndex == 1 ? ConditionKind.ColorDisappears : quickCondition?.Kind == ConditionKind.PixelColor ? ConditionKind.PixelColor : ConditionKind.RegionColor,
                quickCondition?.Policy ?? new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30)),
                quickCondition?.Region ?? point, picker.Color, quickCondition?.Tolerance ?? 8);
            ConditionLabel.Text = ConditionSummary(quickCondition); ShowConditionColour();
            TestResultLabel.Text = "Colour selected. Add Action or Save changes to keep it.";
        }
        catch (OperationCanceledException) { ReasonLabel.Text = "Colour selection cancelled."; }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
        finally { runtime.Control.StopRequested -= Cancel; quickCancellation = null; operations.End(Operation.Quick); operations.End(Operation.Dialog); RefreshStatus(); }
    }
}
