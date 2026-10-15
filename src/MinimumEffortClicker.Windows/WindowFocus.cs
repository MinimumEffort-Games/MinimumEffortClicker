using System.Runtime.InteropServices;

namespace MinimumEffortClicker.Windows;

/// <summary>
/// Brings one of the clicker's own windows back in front after the Click target had the focus. Windows refuses a
/// background app that asks for the foreground; joining the front window's input queue for a moment is the allowed way
/// to hand focus back, and it sends no input to anyone.
/// </summary>
public static class WindowFocus
{
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint window);

    public static bool BringToFront(nint window)
    {
        if (window == 0 || !ProbeNative.IsWindow(window)) return false;
        if (ProbeNative.IsIconic(window)) DesktopNative.ShowWindow(window, 9); // SW_RESTORE
        if (ProbeNative.GetForegroundWindow() == window || ProbeNative.SetForegroundWindow(window) && ProbeNative.GetForegroundWindow() == window) return true;
        nint front = ProbeNative.GetForegroundWindow();
        uint frontThread = front == 0 ? 0 : ProbeNative.GetWindowThreadProcessId(front, out _), own = ProbeNative.GetCurrentThreadId();
        bool joined = frontThread != 0 && frontThread != own && AttachThreadInput(own, frontThread, true);
        try { BringWindowToTop(window); ProbeNative.SetForegroundWindow(window); }
        finally { if (joined) AttachThreadInput(own, frontThread, false); }
        return ProbeNative.GetForegroundWindow() == window;
    }
}
