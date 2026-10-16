using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using System.Globalization;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private static readonly PointerButton[] MouseChoices = [PointerButton.Left, PointerButton.Middle, PointerButton.Right, PointerButton.XButton1, PointerButton.XButton2];
    private PointerButton MouseChoice { get => MouseChoices[Math.Max(0, ButtonBox.SelectedIndex)]; set => ButtonBox.SelectedIndex = Array.IndexOf(MouseChoices, value); }
    private ClientPoint? quickPoint;
    private async void PingLocation_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || quickOperation) return;
        const int pulses = 3;
        StepAside? away = null;
        try
        {
            var screen = ClickScreenPoint(ReadQuickAction()) ?? throw new ArgumentException("Set a saved click location first.");
            // The target is shown while the ring pulses: the clicker steps aside on Desktop, a window target comes forward.
            away = StepAsideForTarget(runtime.IsDesktopMode);
            if (!runtime.IsDesktopMode && !runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            // The same ring as a tested click, in red and three times, exactly on the saved location.
            ClickPingWindow.At(screen.X, screen.Y, ClickPingWindow.Location, times: pulses);
            await Task.Delay(ClickPingWindow.Pulse * pulses);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ReasonLabel.Text = ex.Message; }
        finally { ComeBack(away); }
    }

    /// <summary>
    /// Where a click action clicks on the desktop, worked out the way playback does (the saved location adapted to the
    /// Click target's current size). Null for a click at the cursor. Ping click location and the tested-click ring use it.
    /// </summary>
    private (int X, int Y)? ClickScreenPoint(SequenceStep step)
    {
        var frame = runtime!.GetTargetFrame();
        var plan = SequenceCompiler.Compile(new([step with { Enabled = true }]), quickPoints).AdaptClickPoints(frame.Width, frame.Height, frame.Dpi, ResizeClicksBox.SelectedIndex == 0);
        if (plan.Inputs.FirstOrDefault(i => i.Down && i.Control.Kind is InputKind.MouseButton or InputKind.Scroll or InputKind.HorizontalScroll)?.Position is not { } point) return null;
        var screen = point.Resolve(frame.X, frame.Y, frame.Width, frame.Height, frame.Dpi);
        return (screen.X, screen.Y);
    }

    private async void PickQuickPoint_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || quickOperation) return;
        quickOperation = true;
        StepAside? away = null;
        using var cancellation = new CancellationTokenSource(); quickCancellation = cancellation;
        void Cancel(string reason) { _ = cancellation.CancelAsync(); }
        runtime.Control.StopRequested += Cancel;
        RefreshStatus();
        try
        {
            away = StepAsideForTarget(runtime.IsDesktopMode);
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            await Task.Delay(350, cancellation.Token);
            var picker = new TargetPreviewWindow(runtime, [], true);
            picker.Start();
            if (await picker.Finished is { } point)
            {
                quickPoint = point; useCursorLocation = false; ShowQuickCoordinates();
            }
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
        finally { runtime.Control.StopRequested -= Cancel; quickCancellation = null; quickOperation = false; ComeBack(away); RefreshStatus(); }
    }

    /// <summary>
    /// After asking to minimise, waits until GNOME shows the clicker minimised and its animation has finished, so a
    /// picture taken next shows what is behind it. Gives up after two seconds when the helper cannot tell.
    /// </summary>
    private Task WaitUntilMinimized(CancellationToken stop) => WaitUntilMinimized([this], stop);
    private async Task WaitUntilMinimized(IReadOnlyList<Window> windows, CancellationToken stop)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        bool asked = false;
        bool Showing() => windows.Any(w => runtime?.OwnWindowMinimized(w.Title ?? "") == false);
        while (DateTime.UtcNow < deadline && Showing())
        {
            // GNOME may not have taken the first request (the app can believe it is minimised while it is on screen): ask again once.
            if (!asked && DateTime.UtcNow > deadline - TimeSpan.FromSeconds(1.5))
            {
                asked = true;
                foreach (var window in windows) { window.WindowState = WindowState.Normal; window.WindowState = WindowState.Minimized; }
            }
            await Task.Delay(50, stop);
        }
        // GNOME's minimise animation takes about 0.4 s; the window is drawn until it ends.
        await Task.Delay(500, stop);
    }


}
