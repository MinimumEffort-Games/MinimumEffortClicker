using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace MinimumEffortClicker.App;

/// <summary>
/// X11 window details Avalonia does not expose. The app runs through XWayland on GNOME Wayland,
/// and GNOME honours these for X11 windows. Failures leave the window usable, just not click-through.
/// </summary>
internal static class X11Windows
{
    private const int ShapeInput = 2, ShapeSet = 0, Unsorted = 0;
    private static readonly object gate = new();
    private static nint display;

    /// <summary>Gives the window an empty input region so clicks pass to the app beneath it.</summary>
    public static bool SetClickThrough(Window window, bool clickThrough)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle) return false;
        try
        {
            lock (gate)
            {
                if (display == 0) display = XOpenDisplay(null);
                if (display == 0) return false;
                if (clickThrough) XShapeCombineRectangles(display, handle.Handle, ShapeInput, 0, 0, 0, 0, ShapeSet, Unsorted);
                else XShapeCombineMask(display, handle.Handle, ShapeInput, 0, 0, 0, ShapeSet);
                XFlush(display);
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>
    /// Asks the window manager never to give this window keyboard focus (the X11 "no input" model),
    /// so clicking it leaves the target app focused, like WS_EX_NOACTIVATE on Windows.
    /// </summary>
    public static bool SetNoFocus(Window window, bool noFocus)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle) return false;
        try
        {
            lock (gate)
            {
                if (display == 0) display = XOpenDisplay(null);
                if (display == 0) return false;
                var hints = new WmHints();
                nint existing = XGetWMHints(display, handle.Handle);
                if (existing != 0) { hints = Marshal.PtrToStructure<WmHints>(existing); XFree(existing); }
                hints.Flags |= 1; // InputHint
                hints.Input = noFocus ? 0 : 1;
                XSetWMHints(display, handle.Handle, ref hints);
                XFlush(display);
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WmHints
    {
        public nint Flags; public int Input; public int InitialState; public nint IconPixmap; public nint IconWindow;
        public int IconX, IconY; public nint IconMask; public nint WindowGroup;
    }

    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern nint XGetWMHints(nint display, nint window);
    [DllImport("libX11.so.6")] private static extern int XSetWMHints(nint display, nint window, ref WmHints hints);
    [DllImport("libX11.so.6")] private static extern int XFree(nint data);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(nint display, nint window, int kind, int x, int y, nint rectangles, int count, int operation, int ordering);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineMask(nint display, nint window, int kind, int x, int y, nint mask, int operation);
}
