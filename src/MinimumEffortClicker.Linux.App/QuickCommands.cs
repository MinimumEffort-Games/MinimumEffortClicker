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
    private void PingLocation_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || quickOperation) return;
        try
        {
            var screen = ClickScreenPoint(ReadQuickAction()) ?? throw new ArgumentException("Set a saved click location first.");
            if (!runtime.IsDesktopMode && !runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            // The same ring as a tested click, in red and three times, exactly on the saved location.
            ClickPingWindow.At(screen.X, screen.Y, ClickPingWindow.Location, times: 3);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ReasonLabel.Text = ex.Message; }
    }

    /// <summary>
    /// Where a click action clicks on the desktop, worked out the way playback does (the saved location adapted to the
    /// Click target's current size). Null for a click at the cursor. Ping click location and the tested-click ring use it.
    /// </summary>
    private (int X, int Y)? ClickScreenPoint(SequenceStep step)
    {
        var frame = runtime!.GetTargetFrame();
        var plan = SequenceCompiler.Compile(new([step with { Enabled = true }]), quickPoints).AdaptClickPoints(frame.Width, frame.Height, frame.Dpi, ResizeClicksBox.SelectedIndex == 0);
        if (plan.Inputs.FirstOrDefault(i => i.Down && i.Control.Kind == InputKind.MouseButton)?.Position is not { } point) return null;
        var screen = point.Resolve(frame.X, frame.Y, frame.Width, frame.Height, frame.Dpi);
        return (screen.X, screen.Y);
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
        // GNOME refuses to raise an X11 window that is not focused and only says it "is ready"; the shell helper can.
        runtime?.ActivateOwnWindow(Title ?? "");
    }

    /// <summary>
    /// After asking to minimise, waits until GNOME shows the clicker minimised and its animation has finished, so a
    /// picture taken next shows what is behind it. Gives up after two seconds when the helper cannot tell.
    /// </summary>
    private async Task WaitUntilMinimized(CancellationToken stop)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        bool asked = false;
        while (DateTime.UtcNow < deadline && runtime?.OwnWindowMinimized(Title ?? "") == false)
        {
            // GNOME may not have taken the first request (the app can believe it is minimised while it is on screen): ask again once.
            if (!asked && DateTime.UtcNow > deadline - TimeSpan.FromSeconds(1.5)) { asked = true; WindowState = WindowState.Normal; WindowState = WindowState.Minimized; }
            await Task.Delay(50, stop);
        }
        // GNOME's minimise animation takes about 0.4 s; the window is drawn until it ends.
        await Task.Delay(500, stop);
    }


}
