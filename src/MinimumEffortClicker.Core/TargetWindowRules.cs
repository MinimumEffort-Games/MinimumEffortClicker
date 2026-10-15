namespace MinimumEffortClicker.Core;

/// <summary>
/// Which windows the Click target list offers, following the rules Windows uses for Alt+Tab. Background programs keep
/// visible helper windows that nobody can see or click: for example a printer update service has a 0×0 tool window and
/// a scanner monitor a 14×14 window owned by another one. Those are left out; real app windows, minimised ones included,
/// stay.
/// </summary>
public static class TargetWindowRules
{
    /// <summary>Smaller than this in either direction cannot be a window someone means to click into.</summary>
    public const int MinimumSize = 50;

    public static bool IsChoosable(bool toolWindow, bool appWindow, bool owned, int width, int height, bool minimized)
    {
        // A window marked as an app window always shows in Alt+Tab, even when it is owned or styled as a tool window.
        if (!appWindow && (toolWindow || owned)) return false;
        // A minimised window reports a placeholder rectangle; it is still a real target once restored.
        return minimized || width >= MinimumSize && height >= MinimumSize;
    }
}
