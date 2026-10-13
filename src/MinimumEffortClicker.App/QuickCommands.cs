using System.Globalization;
using System.Windows;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private static readonly PointerButton[] MouseChoices = [PointerButton.Left, PointerButton.Middle, PointerButton.Right, PointerButton.XButton1, PointerButton.XButton2];
    private PointerButton MouseChoice { get => MouseChoices[Math.Max(0, ButtonBox.SelectedIndex)]; set => ButtonBox.SelectedIndex = Array.IndexOf(MouseChoices, value); }
    private string? guidedEditor;
    private ClientPoint? quickPoint;
    private async void PingLocation_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || quickOperation) return;
        try
        {
            var step = ReadQuickAction();
            var frame = runtime.GetTargetFrame();
            var plan = SequenceCompiler.Compile(new([step])).AdaptClickPoints(frame.Width, frame.Height, frame.Dpi, ResizeClicksBox.SelectedIndex == 0);
            var point = plan.Inputs.FirstOrDefault(i => i.Down && i.Control.Kind == InputKind.MouseButton)?.Position
                ?? throw new ArgumentException("Set a saved click location first.");
            if (!runtime.IsDesktopMode && !runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            var marker = new TargetPreviewWindow(runtime, [new(Guid.NewGuid(), "Click location", point)], false, TimeSpan.FromSeconds(5), true);
            marker.Start(); await marker.Finished;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ReasonLabel.Text = ex.Message; }
    }

    private async void PickQuickPoint_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || quickOperation) return;
        quickOperation = true;
        WindowState? minimizedFrom = null;
        using var cancellation = new CancellationTokenSource(); quickCancellation = cancellation;
        void Cancel(string reason) { _ = cancellation.CancelAsync(); }
        runtime.Control.StopRequested += Cancel;
        RefreshStatus();
        try
        {
            if (runtime.IsDesktopMode && WindowState != WindowState.Minimized)
            { minimizedFrom = WindowState; WindowState = WindowState.Minimized; }
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            await Task.Delay(350, cancellation.Token);
            var picker = new TargetPreviewWindow(runtime, [], true);
            picker.Start();
            if (await picker.Finished is { } point)
            {
                quickPoint = point; useCursorLocation = false; ShowQuickCoordinates();
            }
            Activate();
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
        finally { runtime.Control.StopRequested -= Cancel; quickCancellation = null; quickOperation = false; RestoreDesktopOperationWindow(minimizedFrom); RefreshStatus(); }
    }

    private void RestoreDesktopOperationWindow(WindowState? previous)
    {
        // Restore only minimization performed by this operation. An explicit tray hide stays hidden.
        if (previous is null || !IsVisible || allowClose || closing || WindowState != WindowState.Minimized) return;
        WindowState = previous.Value;
        Activate();
    }

    private void Wizard_Click(object sender, RoutedEventArgs e) => Navigate(0);
    private void GuideQuick_Click(object sender, RoutedEventArgs e)
    {
        Navigate(0);
        ReasonLabel.Text = "Choose a Click target, choose an action, then Add Action. Select any list row to edit it. Start runs the list in order.";
    }
    private async void GuideRecord_Click(object sender, RoutedEventArgs e) => await StartGuidedEditor("record");
    private async void GuideVision_Click(object sender, RoutedEventArgs e) => await StartGuidedEditor("vision");
    private async Task StartGuidedEditor(string guide)
    {
        if (runtime is null) return;
        var wizard = new SetupWizard(this, runtime, guide);
        if (wizard.ShowDialog() != true) return;
        RefreshTargetChoices();
        guidedEditor = guide;
        try { await OpenSequence(new([])); }
        finally { guidedEditor = null; }
    }

}
