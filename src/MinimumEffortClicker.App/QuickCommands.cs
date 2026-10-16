using System.Windows;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private static readonly PointerButton[] MouseChoices = [PointerButton.Left, PointerButton.Middle, PointerButton.Right, PointerButton.XButton1, PointerButton.XButton2];
    private PointerButton MouseChoice { get => ButtonPicker.Selected ?? PointerButton.Left; set => ButtonPicker.Selected = value; }
    private ClientPoint? quickPoint;
    private async void PingLocation_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || operations.Has(Operation.Quick)) return;
        const int pulses = 3;
        StepAside? away = null;
        try
        {
            var screen = ClickScreenPoint(ReadQuickAction()) ?? throw new ArgumentException("Set a saved click location first.");
            // The target is shown while the ring pulses: the clicker steps aside on Desktop, a window target comes forward.
            away = StepAsideForTarget(runtime.IsDesktopMode);
            if (!runtime.IsDesktopMode && !runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            // The same ring as a tested click, in red and three times, exactly on the saved location; click-through.
            ClickPingWindow.At(screen.X, screen.Y, ClickPingWindow.Location, times: pulses);
            await Task.Delay(ClickPingWindow.Pulse * pulses);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ReasonLabel.Text = ex.Message; }
        finally { ComeBack(away); }
    }

    /// <summary>
    /// Where a click action clicks on the screen, worked out the way playback does (the saved location adapted to the
    /// Click target's current size). Null for a click at the cursor. Ping location and the tested-click ring use it.
    /// </summary>
    private (int X, int Y)? ClickScreenPoint(SequenceStep step)
    {
        var frame = runtime!.GetTargetFrame();
        var plan = SequenceCompiler.Compile(new([step with { Enabled = true }]), quickPoints).AdaptClickPoints(frame.Width, frame.Height, frame.Dpi, ResizeClicksBox.SelectedIndex == 0);
        if (plan.Inputs.FirstOrDefault(i => i.Down && i.Control.Kind is InputKind.MouseButton or InputKind.Scroll or InputKind.HorizontalScroll)?.Position is not { } point) return null;
        return point.Resolve(frame.X, frame.Y, frame.Width, frame.Height, frame.Dpi);
    }

    /// <summary>The physical screen rectangle a colour wait watches, worked out against the Click target's current frame.</summary>
    private (int Left, int Top, int Width, int Height) AreaScreenRect(CaptureRegion region)
    {
        var frame = runtime!.GetTargetFrame();
        var topLeft = region.Origin.Resolve(frame.X, frame.Y, frame.Width, frame.Height, frame.Dpi);
        var bottomRight = (region.Origin with { X = region.Origin.X + region.Width, Y = region.Origin.Y + region.Height })
            .Resolve(frame.X, frame.Y, frame.Width, frame.Height, frame.Dpi);
        return (topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    /// <summary>Wait until a colour: outlines the watched area three times on the target, then the clicker comes back.</summary>
    private async void PingScanArea_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || operations.Has(Operation.Quick)) return;
        const int pulses = 3;
        StepAside? away = null;
        try
        {
            if (ReadQuickCondition().Region is not { } region) throw new ArgumentException("Select an area first.");
            var area = AreaScreenRect(region);
            away = StepAsideForTarget(runtime.IsDesktopMode);
            if (!runtime.IsDesktopMode && !runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            AreaPingWindow.Around(area.Left, area.Top, area.Width, area.Height, ClickPingWindow.Location, times: pulses);
            await Task.Delay(ClickPingWindow.Pulse * pulses);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ReasonLabel.Text = ex.Message; }
        finally { ComeBack(away); }
    }

    private async void PickQuickPoint_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || operations.Has(Operation.Quick)) return;
        operations.Begin(Operation.Quick);
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
            var picker = new TargetPreviewWindow(runtime);
            picker.Start();
            if (await picker.Finished is { } point)
            {
                quickPoint = point; useCursorLocation = false; ShowQuickCoordinates();
            }
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
        finally { runtime.Control.StopRequested -= Cancel; quickCancellation = null; operations.End(Operation.Quick); ComeBack(away); RefreshStatus(); }
    }
}
