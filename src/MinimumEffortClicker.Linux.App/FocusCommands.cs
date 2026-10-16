using Avalonia.Controls;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    /// <summary>What an Action List feature did to make room for its target, so <see cref="ComeBack"/> undoes exactly that.</summary>
    private sealed record StepAside(WindowState? MinimizedFrom);

    /// <summary>
    /// User (2026-10-15): every Action List feature that needs the target steps aside, and the clicker comes back when it
    /// is done. With Desktop as target the clicker minimises (when <paramref name="minimize"/>); with a window target that
    /// window is brought forward by the caller. Toggles never call this: they are used while playing.
    /// </summary>
    private StepAside StepAsideForTarget(bool minimize)
    {
        if (!minimize || WindowState == WindowState.Minimized) return new(null);
        var from = WindowState; WindowState = WindowState.Minimized;
        return new(from);
    }

    /// <summary>
    /// Brings the clicker back in front, restored, after the target had the focus: the list ended or was stopped, a
    /// location was pinged or set, an action was tested, a colour was picked, a recording ended. A clicker hidden to the
    /// top bar, or minimised by the user rather than by the feature, stays where it is.
    /// </summary>
    private void ComeBack(StepAside? away)
    {
        if (away is null || !IsVisible || allowClose || closing) return;
        if (WindowState == WindowState.Minimized)
        {
            if (away.MinimizedFrom is not { } previous) return;
            WindowState = previous;
        }
        Activate();
        // GNOME refuses to raise an X11 window that is not focused and only says it "is ready"; the desktop helper can.
        runtime?.ActivateOwnWindow(Title ?? "");
    }
}
