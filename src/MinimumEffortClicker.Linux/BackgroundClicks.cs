using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Linux;

public sealed partial class DesktopRuntime
{
    // Windows can post click messages to a background window without moving the cursor. Wayland and
    // X11 apps accept input only through the compositor, so cursor-free clicks are never available.
    private volatile bool cursorFreeActive;
    private bool CursorFreeActive => cursorFreeActive;
    private static bool BackgroundTargetAvailable => false;
    private static bool SendBackgroundClick(InputTransition transition) => false;
}
