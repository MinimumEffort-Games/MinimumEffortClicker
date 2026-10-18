using System.Runtime.InteropServices;
using Avalonia;
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

    /// <summary>The width of the X11 screen in X11 pixels, or null without an X display.</summary>
    public static int? ScreenWidth()
    {
        try
        {
            lock (gate)
            {
                if (display == 0) display = XOpenDisplay(null);
                return display == 0 ? null : XDisplayWidth(display, XDefaultScreen(display));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

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
    /// Lets clicks reach the window only inside the given rectangles (window pixels); everywhere else they pass through
    /// to the app beneath it. An empty list makes the whole window click-through.
    /// </summary>
    public static bool SetInputRegion(Window window, IReadOnlyList<PixelRect> areas)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle) return false;
        try
        {
            lock (gate)
            {
                if (display == 0) display = XOpenDisplay(null);
                if (display == 0) return false;
                var rectangles = areas.Select(a => new XRectangle { X = (short)a.X, Y = (short)a.Y, Width = (ushort)a.Width, Height = (ushort)a.Height }).ToArray();
                XShapeCombineRectangles(display, handle.Handle, ShapeInput, 0, 0, rectangles, rectangles.Length, ShapeSet, Unsorted);
                XFlush(display);
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XRectangle { public short X, Y; public ushort Width, Height; }

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

    /// <summary>
    /// Asks GNOME to keep the window above normal windows (_NET_WM_STATE_ABOVE). Avalonia's Topmost does not reach the
    /// window manager for these click-through windows, so the request is sent here once the window is mapped.
    /// </summary>
    public static bool SetAbove(Window window)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle) return false;
        try
        {
            lock (gate)
            {
                if (display == 0) display = XOpenDisplay(null);
                if (display == 0) return false;
                var message = new ClientMessage
                {
                    Type = 33, SendEvent = 1, Display = display, Window = handle.Handle, Format = 32,
                    MessageType = XInternAtom(display, "_NET_WM_STATE", false),
                    Data0 = 1, // add
                    Data1 = XInternAtom(display, "_NET_WM_STATE_ABOVE", false),
                    Data2 = XInternAtom(display, "_NET_WM_STATE_SKIP_TASKBAR", false),
                    Data3 = 1, // a normal application asked
                };
                XSendEvent(display, XDefaultRootWindow(display), false, (1 << 19) | (1 << 20), ref message);
                // KWin marks a window that is shown without taking focus as wanting attention, which can light the app
                // up in the task bar; these windows never want it.
                message.Data0 = 0; // remove
                message.Data1 = XInternAtom(display, "_NET_WM_STATE_DEMANDS_ATTENTION", false);
                message.Data2 = 0;
                XSendEvent(display, XDefaultRootWindow(display), false, (1 << 19) | (1 << 20), ref message);
                XFlush(display);
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>
    /// Keep on top for the main window: adds or removes only _NET_WM_STATE_ABOVE, leaving its task bar entry alone.
    /// Sent as well as Avalonia's Topmost, which does not reliably reach GNOME and KWin through Xwayland.
    /// </summary>
    public static bool SetKeepAbove(Window window, bool above)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle) return false;
        try
        {
            lock (gate)
            {
                if (display == 0) display = XOpenDisplay(null);
                if (display == 0) return false;
                var message = new ClientMessage
                {
                    Type = 33, SendEvent = 1, Display = display, Window = handle.Handle, Format = 32,
                    MessageType = XInternAtom(display, "_NET_WM_STATE", false),
                    Data0 = above ? 1 : 0, // add or remove
                    Data1 = XInternAtom(display, "_NET_WM_STATE_ABOVE", false),
                    Data3 = 1, // a normal application asked
                };
                XSendEvent(display, XDefaultRootWindow(display), false, (1 << 19) | (1 << 20), ref message);
                XFlush(display);
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>XClientMessageEvent, padded to the size of an XEvent.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    private struct ClientMessage
    {
        public int Type; public nint Serial; public int SendEvent; public nint Display; public nint Window; public nint MessageType; public int Format;
        public nint Data0, Data1, Data2, Data3, Data4;
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
    [DllImport("libX11.so.6")] private static extern nint XInternAtom(nint display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern nint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern int XDefaultScreen(nint display);
    [DllImport("libX11.so.6")] private static extern int XDisplayWidth(nint display, int screen);
    [DllImport("libX11.so.6")] private static extern int XSendEvent(nint display, nint window, bool propagate, nint mask, ref ClientMessage message);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(nint display, nint window, int kind, int x, int y, nint rectangles, int count, int operation, int ordering);
    [DllImport("libXext.so.6", EntryPoint = "XShapeCombineRectangles")] private static extern void XShapeCombineRectangles(nint display, nint window, int kind, int x, int y, XRectangle[] rectangles, int count, int operation, int ordering);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineMask(nint display, nint window, int kind, int x, int y, nint mask, int operation);
}
