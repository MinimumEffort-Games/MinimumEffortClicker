using MinimumEffortClicker.Linux.Kde;

namespace MinimumEffortClicker.Linux;

public sealed record HelperMonitor(int X, int Y, int Width, int Height, double Scale);
public sealed record HelperWindow(ulong Id, string Title, int ProcessId);
public readonly record struct HelperWindowState(bool Exists, bool Minimized, int ProcessId, string Title, int X, int Y, int Width, int Height, double Scale);
public readonly record struct HelperTargetState(ulong Focus, bool Exists, bool Minimized, int ProcessId,
    int X, int Y, int Width, int Height, double Scale, int PointerX, int PointerY, bool ShellReady);
/// <summary>Captured pixels as RGB or RGBA rows; may be larger than asked when the monitor is scaled.</summary>
public sealed record HelperImage(int Width, int Height, int RowStride, int Channels, byte[] Pixels);

public sealed class HelperException(string message, Exception? inner = null) : Exception(message, inner)
{
    public bool AccessDenied { get; init; }
    public bool NotInstalled { get; init; }
}

/// <summary>
/// What the app needs from the desktop on Wayland, where it cannot see other windows, read the pointer, capture the
/// screen or send input by itself. GNOME answers through the ME Clicker Shell extension, KDE Plasma through KWin.
/// Positions are logical desktop pixels. Window ids are only meaningful to the helper that gave them out. Every call
/// has a bounded wait and throws <see cref="HelperException"/> when the desktop cannot answer.
/// </summary>
public interface IDesktopHelper : IDisposable
{
    /// <summary>"GNOME helper" or "KDE helper", for status lines.</summary>
    string Name { get; }

    event Action<ulong>? FocusChanged;
    event Action<ulong>? WindowClosed;
    /// <summary>A grabbed shortcut was pressed (true) or released (false).</summary>
    event Action<uint, bool>? AcceleratorChanged;

    (int X, int Y) GetPointer();
    /// <summary>Every monitor; the primary one first.</summary>
    IReadOnlyList<HelperMonitor> GetMonitors();
    IReadOnlyList<HelperWindow> ListWindows();
    ulong GetFocusWindow();
    /// <summary>One window's client area.</summary>
    HelperWindowState GetWindow(ulong id);
    /// <summary>Focus, the window's client area and the pointer in one answer, plus whether input reaches app windows now.</summary>
    HelperTargetState QueryTarget(ulong id);
    /// <summary>The topmost visible window at a point, skipping windows with this title.</summary>
    (ulong Id, int ProcessId) WindowAt(int x, int y, string skipTitle);
    (bool InputReady, string Detail) GetShellState();
    /// <summary>The current layout as "xkb:layout" or "xkb:layout+variant".</summary>
    string GetKeyboardLayout();
    bool ActivateWindow(ulong id);

    void PointerMove(double x, double y);
    /// <summary>Button numbers 1 left, 2 middle, 3 right, 8 back, 9 forward.</summary>
    void PointerButton(uint button, bool pressed);
    /// <summary>Direction 0 up, 1 down, 2 left, 3 right; steps are wheel notches.</summary>
    void Scroll(uint direction, uint steps);
    void Key(int evdev, bool pressed);
    void ReleaseAll();

    HelperImage CaptureRaw(int x, int y, int width, int height);
    /// <summary>A region of one window's own content (client-relative), unaffected by windows above it.</summary>
    HelperImage CaptureWindowRaw(ulong id, int x, int y, int width, int height);

    /// <summary>Grabs a key named like "F8" or "KP_1" for every app; throws when it is already used.</summary>
    uint GrabAccelerator(string accelerator);
    void UngrabAccelerator(uint action);
}

/// <summary>Connects the helper for the desktop this session runs.</summary>
public static class DesktopHelper
{
    public static IDesktopHelper Connect() => LinuxSetup.IsKde ? KdeHelper.Connect() : GnomeHelper.Connect();
}
