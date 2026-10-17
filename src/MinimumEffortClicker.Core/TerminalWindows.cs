namespace MinimumEffortClicker.Core;

/// <summary>
/// Terminals and command lines, on Windows and Linux. Key presses from a Workshop macro never go to one: there a few
/// letters and Enter would run a command, so a shared macro could do harm on someone else's PC. A window counts when its
/// program or its window class is a known terminal.
/// </summary>
public static class TerminalWindows
{
    private static readonly HashSet<string> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows: the console host and shells, Windows Terminal, and common third-party terminals.
        "cmd", "powershell", "powershell_ise", "pwsh", "conhost", "openconsole", "windowsterminal", "wt", "wsl", "wslhost",
        "bash", "mintty", "conemu", "conemu64", "cmder", "putty", "kitty", "alacritty", "wezterm", "wezterm-gui", "hyper",
        "tabby", "fluent terminal", "warp", "termius", "mobaxterm", "git-bash",
        // Linux: terminal emulators by program name and by desktop app id.
        "gnome-terminal", "gnome-terminal-server", "org.gnome.terminal", "kgx", "org.gnome.console", "ptyxis",
        "org.gnome.ptyxis", "konsole", "org.kde.konsole", "yakuake", "org.kde.yakuake", "xterm", "uxterm", "rxvt",
        "urxvt", "st", "foot", "footclient", "tilix", "com.gexperts.tilix", "terminator", "xfce4-terminal", "lxterminal",
        "mate-terminal", "qterminal", "terminology", "guake", "tilda", "cool-retro-term", "blackbox", "contour",
        "warp-terminal", "ghostty", "com.mitchellh.ghostty", "sakura", "deepin-terminal", "io.elementary.terminal",
        "org.wezfurlong.wezterm", "zutty", "kmscon",
    };

    private static readonly HashSet<string> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "PseudoConsoleWindow", "mintty", "VirtualConsoleClass", "PuTTY",
    };

    /// <summary>True for a terminal, by program name (with or without ".exe" or ".desktop") or window class.</summary>
    public static bool Is(string? program, string? windowClass = null)
    {
        string name = Path.GetFileName(program?.Trim() ?? "");
        foreach (var suffix in (string[])[".exe", ".desktop"])
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
        return name.Length > 0 && Programs.Contains(name) || windowClass is { Length: > 0 } && Classes.Contains(windowClass.Trim());
    }
}
