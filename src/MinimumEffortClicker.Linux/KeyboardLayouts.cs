namespace MinimumEffortClicker.Linux;

/// <summary>
/// Saved recordings store the Windows keyboard-layout handle they were made with. Common GNOME xkb
/// layouts are reported with the same identity so a macro recorded on Windows still plays here.
/// </summary>
public static class KeyboardLayouts
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"] = "04090409", ["us+dvorak"] = "F0020409", ["gb"] = "08090809", ["de"] = "04070407", ["fr"] = "040C040C",
        ["es"] = "0C0A0C0A", ["it"] = "04100410", ["se"] = "041D041D", ["no"] = "04140414", ["dk"] = "04060406",
        ["fi"] = "040B040B", ["nl"] = "04130413", ["pt"] = "08160816", ["br"] = "04160416", ["ru"] = "04190419",
        ["pl"] = "04150415", ["ch"] = "08070807", ["jp"] = "04110411", ["be"] = "080C080C", ["ie"] = "18091809"
    };

    /// <summary>Maps a helper value such as "xkb:us" to a layout identity, or null when unknown.</summary>
    public static string? FromGnome(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        string id = source.StartsWith("xkb:", StringComparison.Ordinal) ? source[4..] : source;
        return Known.TryGetValue(id, out var handle) ? handle : "linux:" + source;
    }
}
