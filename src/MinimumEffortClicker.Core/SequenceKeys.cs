namespace MinimumEffortClicker.Core;

/// <summary>Physical scan positions for sequence authoring. Labels describe US positions, not typed text.</summary>
public static class SequenceKeys
{
    public static bool IsModifier(InputControl input) => input.Kind == InputKind.ScanCode && input.Code is 0x1D or 0x11D or 0x2A or 0x36 or 0x38 or 0x138;
    public static IReadOnlyDictionary<string, int> Supported { get; } = Build();
    public static IReadOnlyDictionary<string, int> Logical { get; } = BuildLogical();
    private static IReadOnlyDictionary<string, int> BuildLogical()
    {
        var keys = Enumerable.Range('A', 26).Concat(Enumerable.Range('0', 10)).ToDictionary(c => "Logical " + (char)c, c => c);
        foreach (var (name, code) in new (string, int)[] { ("Space", 0x20), ("Enter", 0x0D), ("Tab", 0x09), ("Escape", 0x1B), ("Backspace", 0x08), ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28) })
            keys["Logical " + name] = code;
        return keys.AsReadOnly();
    }
    private static IReadOnlyDictionary<string, int> Build()
    {
        var keys = HelperPlan.SupportedKeys.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (name, code) in new (string, int)[] {
            ("Left Ctrl",0x1D), ("Right Ctrl",0x11D), ("Left Shift",0x2A), ("Right Shift",0x36),
            ("Left Alt",0x38), ("Right Alt",0x138), ("Escape",0x01), ("Backspace",0x0E),
            ("Insert",0x152), ("Delete",0x153), ("Home",0x147), ("End",0x14F),
            ("Page Up",0x149), ("Page Down",0x151), ("Minus",0x0C), ("Equals",0x0D),
            ("Left bracket",0x1A), ("Right bracket",0x1B), ("Semicolon",0x27), ("Quote",0x28),
            ("Backtick",0x29), ("Backslash",0x2B), ("Comma",0x33), ("Period",0x34), ("Slash",0x35) }) keys[name] = code;
        for (int i = 1; i <= 10; i++) keys["F" + i] = 0x3A + i;
        keys["F11"] = 0x57; keys["F12"] = 0x58;
        for (int i = 13; i <= 23; i++) keys["F" + i] = 0x64 + i - 13;
        keys["F24"] = 0x76;
        foreach (var (name, code) in new (string, int)[] { ("Caps Lock",0x3A), ("Num Lock",0x145), ("Scroll Lock",0x46),
            ("Numpad 0",0x52), ("Numpad 1",0x4F), ("Numpad 2",0x50), ("Numpad 3",0x51), ("Numpad 4",0x4B),
            ("Numpad 5",0x4C), ("Numpad 6",0x4D), ("Numpad 7",0x47), ("Numpad 8",0x48), ("Numpad 9",0x49),
            ("Numpad .",0x53), ("Numpad +",0x4E), ("Numpad -",0x4A), ("Numpad *",0x37), ("Numpad /",0x135),
            ("Numpad Enter",0x11C), ("OEM 102",0x56), ("Print Screen",0x137) }) keys[name] = code;
        return keys.AsReadOnly();
    }
}
