using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Linux;

/// <summary>
/// Translates the portable key identities used by saved macros and settings (Windows set-1 scan
/// codes for keys, Windows virtual-key numbers for shortcuts) to Linux evdev codes and back.
/// Linux evdev codes 1–88 equal set-1 scan codes; extended keys and F13–F24 need a table.
/// </summary>
public static class LinuxKeys
{
    public const int ButtonLeft = 0x110, ButtonRight = 0x111, ButtonMiddle = 0x112, ButtonSide = 0x113, ButtonExtra = 0x114;

    private static readonly Dictionary<int, int> ExtendedScan = new()
    {
        [0x11C] = 96, [0x11D] = 97, [0x135] = 98, [0x137] = 99, [0x138] = 100, [0x145] = 69,
        [0x147] = 102, [0x148] = 103, [0x149] = 104, [0x14B] = 105, [0x14D] = 106, [0x14F] = 107,
        [0x150] = 108, [0x151] = 109, [0x152] = 110, [0x153] = 111, [0x15B] = 125, [0x15C] = 126, [0x15D] = 127
    };

    private static readonly Dictionary<int, int> VirtualKeys = BuildVirtualKeys();
    // Keypad 5 and Clear share one physical key; report the keypad identity.
    private static readonly Dictionary<int, int> EvdevToVirtual = VirtualKeys.Where(p => p.Key != 12).ToDictionary(p => p.Value, p => p.Key);
    private static readonly Dictionary<int, int> EvdevToScanCode = BuildReverseScan();

    /// <summary>Evdev key code for a saved scan code, or 0 when Linux has no equivalent.</summary>
    public static int EvdevFromScanCode(int scan)
    {
        if ((scan & 0x100) != 0) return ExtendedScan.GetValueOrDefault(scan);
        int code = scan & 0xFF;
        return code switch
        {
            0x45 => 119, // Pause; Num Lock is the extended 0x145.
            0x54 => 99, // Alt+Print Screen (SysRq).
            >= 0x64 and <= 0x6E => 183 + code - 0x64, // F13–F23
            0x76 => 194, // F24
            >= 1 and <= 0x58 => code,
            _ => 0
        };
    }

    /// <summary>Saved scan-code identity for a physical evdev key, or null for keys macros cannot store.</summary>
    public static int? ScanCodeFromEvdev(int evdev) => EvdevToScanCode.TryGetValue(evdev, out int scan) ? scan : null;

    /// <summary>Evdev key or button code for a Windows virtual-key shortcut identity, or 0.</summary>
    public static int EvdevFromVirtualKey(int virtualKey) => VirtualKeys.GetValueOrDefault(virtualKey);
    public static int? VirtualKeyFromEvdev(int evdev) => EvdevToVirtual.TryGetValue(evdev, out int key) ? key : null;

    public static int EvdevButton(PointerButton button) => button switch
    {
        PointerButton.Left => ButtonLeft, PointerButton.Right => ButtonRight, PointerButton.Middle => ButtonMiddle,
        PointerButton.XButton1 => ButtonSide, PointerButton.XButton2 => ButtonExtra,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };
    public static PointerButton? ButtonFromEvdev(int code) => code switch
    {
        ButtonLeft => PointerButton.Left, ButtonRight => PointerButton.Right, ButtonMiddle => PointerButton.Middle,
        ButtonSide or 0x116 => PointerButton.XButton1, ButtonExtra or 0x115 => PointerButton.XButton2, _ => null
    };
    /// <summary>GNOME Shell (Clutter) button numbers used by the helper's virtual pointer.</summary>
    public static uint ClutterButton(PointerButton button) => button switch
    {
        PointerButton.Left => 1, PointerButton.Middle => 2, PointerButton.Right => 3,
        PointerButton.XButton1 => 8, PointerButton.XButton2 => 9,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    /// <summary>GNOME accelerator name for a keyboard shortcut, or null when it cannot be grabbed.</summary>
    public static string? Accelerator(int virtualKey) => virtualKey switch
    {
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F),
        >= 0x41 and <= 0x5A => ((char)(virtualKey + 32)).ToString(),
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x60 and <= 0x69 => "KP_" + (virtualKey - 0x60),
        8 => "BackSpace", 9 => "Tab", 13 => "Return", 19 => "Pause", 20 => "Caps_Lock", 27 => "Escape", 0x20 => "space",
        0x21 => "Page_Up", 0x22 => "Page_Down", 0x23 => "End", 0x24 => "Home", 0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
        0x2C => "Print", 0x2D => "Insert", 0x2E => "Delete", 0x6A => "KP_Multiply", 0x6B => "KP_Add", 0x6D => "KP_Subtract",
        0x6E => "KP_Decimal", 0x6F => "KP_Divide", 0x90 => "Num_Lock", 0x91 => "Scroll_Lock",
        _ => null
    };

    public static bool IsModifier(int evdev) => evdev is 29 or 97 or 42 or 54 or 56 or 100 or 125 or 126;

    private static Dictionary<int, int> BuildVirtualKeys()
    {
        var keys = new Dictionary<int, int>
        {
            [1] = ButtonLeft, [2] = ButtonRight, [4] = ButtonMiddle, [5] = ButtonSide, [6] = ButtonExtra,
            [8] = 14, [9] = 15, [12] = 76, [13] = 28, [19] = 119, [20] = 58, [27] = 1, [0x20] = 57,
            [0x21] = 104, [0x22] = 109, [0x23] = 107, [0x24] = 102, [0x25] = 105, [0x26] = 103, [0x27] = 106, [0x28] = 108,
            [0x2C] = 99, [0x2D] = 110, [0x2E] = 111, [0x30] = 11,
            [0x60] = 82, [0x61] = 79, [0x62] = 80, [0x63] = 81, [0x64] = 75, [0x65] = 76, [0x66] = 77, [0x67] = 71, [0x68] = 72, [0x69] = 73,
            [0x6A] = 55, [0x6B] = 78, [0x6C] = 121, [0x6D] = 74, [0x6E] = 83, [0x6F] = 98,
            [0x7A] = 87, [0x7B] = 88, [0x90] = 69, [0x91] = 70,
            [0xA0] = 42, [0xA1] = 54, [0xA2] = 29, [0xA3] = 97, [0xA4] = 56, [0xA5] = 100,
            [0xBA] = 39, [0xBB] = 13, [0xBC] = 51, [0xBD] = 12, [0xBE] = 52, [0xBF] = 53, [0xC0] = 41,
            [0xDB] = 26, [0xDC] = 43, [0xDD] = 27, [0xDE] = 40, [0xE2] = 86
        };
        for (int digit = 1; digit <= 9; digit++) keys[0x30 + digit] = 1 + digit;
        string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        int[] letterCodes = [30, 48, 46, 32, 18, 33, 34, 35, 23, 36, 37, 38, 50, 49, 24, 25, 16, 19, 31, 20, 22, 47, 17, 45, 21, 44];
        for (int i = 0; i < letters.Length; i++) keys[letters[i]] = letterCodes[i];
        for (int f = 0; f < 10; f++) keys[0x70 + f] = 59 + f; // F1–F10
        for (int f = 0; f < 12; f++) keys[0x7C + f] = 183 + f; // F13–F24
        return keys;
    }

    private static Dictionary<int, int> BuildReverseScan()
    {
        var reverse = new Dictionary<int, int>();
        // Extended identities first: Print Screen is saved as 0x137, not the Alt+Print 0x54 alias.
        foreach (int scan in Enumerable.Range(0x100, 0x100).Concat(Enumerable.Range(1, 0xFF)))
        {
            int evdev = EvdevFromScanCode(scan);
            if (evdev != 0) reverse.TryAdd(evdev, scan);
        }
        return reverse;
    }
}
