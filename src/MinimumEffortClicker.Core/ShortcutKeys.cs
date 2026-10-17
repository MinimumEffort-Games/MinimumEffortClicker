namespace MinimumEffortClicker.Core;

/// <summary>Windows virtual-key identities for local, single-key or mouse-button shortcuts.</summary>
public static class ShortcutKeys
{
    /// <summary>F8 starts and stops Actions, F9 stops everything, F10 starts and stops recording.</summary>
    public const int DefaultStartKey = 0x77, DefaultStopKey = 0x78, DefaultRecordKey = 0x79;
    /// <summary>F7 takes a screenshot (User, 2026-10-17).</summary>
    public const int DefaultScreenshotKey = 0x76;
    public static bool IsMouse(int key) => key is 1 or 2 or 4 or 5 or 6;
    public static bool Supported(int key) => IsMouse(key) || key is 8 or 9 or 12 or 13 or 19 or 20 or 27
        or >= 0x20 and <= 0x28 or 0x2C or 0x2D or 0x2E or >= 0x30 and <= 0x39
        or >= 0x41 and <= 0x5A or >= 0x60 and <= 0x87 or 0x90 or 0x91
        or >= 0xA0 and <= 0xA5 or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;
    // Keep the established OS registration route for F1–F11; other keys/buttons use existing hooks.
    // This avoids consuming ordinary typing globally and supports mouse and debugger-reserved F12.
    public static bool UsesHook(int key) => key is < 0x70 or > 0x7A;
    public static int MouseKey(PointerButton button) => button switch
    { PointerButton.Left => 1, PointerButton.Right => 2, PointerButton.Middle => 4, PointerButton.XButton1 => 5, PointerButton.XButton2 => 6, _ => throw new ArgumentException("Unknown mouse button.") };
    public static InputControl MouseInput(int key) => InputControl.Mouse(key switch
    { 1 => PointerButton.Left, 2 => PointerButton.Right, 4 => PointerButton.Middle, 5 => PointerButton.XButton1, 6 => PointerButton.XButton2, _ => throw new ArgumentException("Choose a mouse button.") });
    /// <summary>Short mouse button names, the same everywhere: LMB, RMB, MMB, M4 (back) and M5 (forward).</summary>
    public static string MouseLabel(PointerButton button) => button switch
    { PointerButton.Left => "LMB", PointerButton.Right => "RMB", PointerButton.Middle => "MMB", PointerButton.XButton1 => "M4", _ => "M5" };
    public static string Label(int key) => key switch
    {
        0 => "Choose a key…", >= 1 and <= 6 when IsMouse(key) => MouseLabel((PointerButton)MouseInput(key).Code),
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F), >= 0x60 and <= 0x69 => "Numpad " + (key - 0x60),
        8 => "Backspace", 9 => "Tab", 12 => "Clear", 13 => "Enter", 19 => "Pause", 20 => "Caps Lock", 27 => "Escape", 32 => "Space",
        33 => "Page Up", 34 => "Page Down", 35 => "End", 36 => "Home", 37 => "Left", 38 => "Up", 39 => "Right", 40 => "Down",
        44 => "Print Screen", 45 => "Insert", 46 => "Delete", 0x6A => "Numpad *", 0x6B => "Numpad +", 0x6C => "Separator",
        0x6D => "Numpad -", 0x6E => "Numpad .", 0x6F => "Numpad /", 0x90 => "Num Lock", 0x91 => "Scroll Lock",
        0xA0 => "Left Shift", 0xA1 => "Right Shift", 0xA2 => "Left Ctrl", 0xA3 => "Right Ctrl", 0xA4 => "Left Alt", 0xA5 => "Right Alt",
        0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/", 0xC0 => "Grave (`)",
        0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'", 0xDF => "OEM 8", 0xE2 => "OEM 102", _ => "Key " + key.ToString("X2")
    };
}
