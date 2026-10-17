using System.Diagnostics;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Linux;

/// <summary>Detects the desktop session and sets up its helper for the current user: the GNOME extension, or KDE access.</summary>
public static class LinuxSetup
{
    public const string HelperUuid = "me-clicker-helper@minimumeffort-games.github.io";
    public const string InputRuleScript = "setup-input-access.sh";

    public static string SessionType => Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown";
    public static string Desktop => Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "unknown";
    public static bool IsGnome => Desktop.Split(':').Any(d => d.Equals("GNOME", StringComparison.OrdinalIgnoreCase));
    public static bool IsKde => Desktop.Split(':').Any(d => d.Equals("KDE", StringComparison.OrdinalIgnoreCase));
    /// <summary>What the helper for this desktop is called in status lines.</summary>
    public static string HelperName => IsKde ? "KDE helper" : "GNOME helper";
    /// <summary>Running on a Steam Deck (its controller shortcuts default to the back buttons).</summary>
    public static bool IsSteamDeck { get; } = SteamDeckHardware.Is(ReadDmi("sys_vendor"), ReadDmi("product_name"));
    private static string? ReadDmi(string name)
    {
        try { return File.ReadAllText("/sys/class/dmi/id/" + name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static string SessionSummary => $"Session: {(SessionType == "wayland" ? "Wayland" : SessionType == "x11" ? "X11" : SessionType)} · Desktop: {Desktop.Replace(':', ' ')}";

    /// <summary>The packaged extension next to the app, or the source copy in a development checkout.</summary>
    public static string? BundledHelperDirectory
    {
        get
        {
            foreach (var root in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "../../../../../linux") })
            {
                var candidate = Path.GetFullPath(Path.Combine(root, "gnome-extension", HelperUuid));
                if (File.Exists(Path.Combine(candidate, "extension.js"))) return candidate;
            }
            return null;
        }
    }

    public static string? InputRuleScriptPath
    {
        get
        {
            foreach (var root in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "../../../../../linux") })
            {
                var candidate = Path.GetFullPath(Path.Combine(root, InputRuleScript));
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }

    public static string ExtensionsDirectory => Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data ? data
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "gnome-shell", "extensions");
    public static string AllowListPath => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "me-clicker-helper", "allowed-clients");

    /// <summary>
    /// Copies the helper into the user's GNOME extensions, allows this executable to use it and
    /// enables it. GNOME on Wayland loads newly installed extensions at the next login.
    /// </summary>
    public static string InstallHelper()
    {
        string executable = Executable();
        if (IsKde) return AllowOnKde(executable);
        var source = BundledHelperDirectory ?? throw new InvalidOperationException("The GNOME helper files are missing from this copy of the app.");
        var destination = Path.Combine(ExtensionsDirectory, HelperUuid);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        Directory.CreateDirectory(Path.GetDirectoryName(AllowListPath)!);
        var allowed = File.Exists(AllowListPath) ? File.ReadAllLines(AllowListPath).Where(l => l.StartsWith('/') && File.Exists(l)).ToList() : [];
        if (!allowed.Contains(executable)) allowed.Add(executable);
        File.WriteAllLines(AllowListPath, allowed);
        bool enabledNow = Run("gnome-extensions", "enable", HelperUuid);
        if (!enabledNow)
        {
            // Not loaded yet: GNOME reads this list at login and enables the helper then.
            string current = Read("gsettings", "get", "org.gnome.shell", "enabled-extensions").Trim();
            if (!current.Contains(HelperUuid, StringComparison.Ordinal))
            {
                string list = current is "@as []" or "" ? $"['{HelperUuid}']" : current.TrimEnd(']') + $", '{HelperUuid}']";
                Run("gsettings", "set", "org.gnome.shell", "enabled-extensions", list);
            }
            Run("gsettings", "set", "org.gnome.shell", "disable-user-extensions", "false");
        }
        return enabledNow ? "The GNOME helper is installed and enabled." : "The GNOME helper is installed. Log out and back in once so GNOME loads it.";
    }

    private static string Executable()
    {
        string executable = Environment.ProcessPath is { } process ? Path.GetFullPath(new FileInfo(process).ResolveLinkTarget(true)?.FullName ?? process)
            : throw new InvalidOperationException("Cannot find this program's path.");
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet")
            throw new InvalidOperationException("Start the clicker from its own executable (not 'dotnet …dll') so the helper can recognise it.");
        return executable;
    }

    public static string ApplicationsDirectory => Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data ? data
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "applications");

    /// <summary>
    /// KWin lets a program capture the screen (ScreenShot2) and send input (fake input) when a .desktop file starts
    /// exactly that executable and names those interfaces. install.sh adds them to the launcher; this hidden entry covers
    /// a copy started from anywhere else. KWin notices it within a few seconds; no log-out is needed.
    /// </summary>
    private static string AllowOnKde(string executable)
    {
        Directory.CreateDirectory(ApplicationsDirectory);
        File.WriteAllText(KdeAccessFile(executable), KdeAccessEntry(executable));
        return "KDE now lets this copy of the app capture the screen and send input. The KDE helper is ready in a few seconds.";
    }

    /// <summary>One hidden entry per copy of the app, named after its path.</summary>
    public static string KdeAccessFile(string executable) => Path.Combine(ApplicationsDirectory,
        "io.github.MinimumEffortGames.Clicker-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(executable)))[..12] + ".desktop");

    public static string KdeAccessEntry(string executable) => $"""
        [Desktop Entry]
        Type=Application
        Name=Minimum Effort Clicker (KDE access for {executable})
        Exec="{executable}"
        NoDisplay=true
        X-KDE-DBUS-Restricted-Interfaces=org.kde.KWin.ScreenShot2
        X-KDE-Wayland-Interfaces=org_kde_kwin_fake_input

        """.ReplaceLineEndings("\n"); // A Windows checkout gives this text CRLF line ends; desktop entries use LF.

    public static string InputAccessCommand => InputRuleScriptPath is { } script ? $"sudo bash '{script}'" : "The input access script is missing from this copy of the app.";
    /// <summary>
    /// Removes the rule again. The rule only gives the person signed in at this computer read access (logind's uaccess),
    /// instead of adding the account to the input group. Null when the script is missing.
    /// </summary>
    public static string? InputAccessRemoveCommand => InputRuleScriptPath is { } script ? $"sudo bash '{script}' --remove" : null;
    /// <summary>
    /// True when the rule setup-input-access.sh installs is there. Some systems (SteamOS) already let the person at the
    /// computer read the keyboard and mouse; there is then nothing of ours to turn off.
    /// </summary>
    public static bool InputRuleInstalled => File.Exists("/etc/udev/rules.d/70-minimum-effort-clicker-input.rules");

    /// <summary>
    /// The system's own password window for apps (polkit's pkexec), the one software updates use. Null on a system without
    /// it; the terminal command above still works there.
    /// </summary>
    public static string? SystemPasswordPrompt => new[] { "/usr/bin/pkexec", "/bin/pkexec" }.FirstOrDefault(File.Exists);

    /// <summary>
    /// Turns physical input access on or off by running the same script as the terminal command, as administrator through
    /// the system password window. Only ever called from a button the user pressed: elevation is never
    /// automatic, and Linux itself asks for the password.
    /// </summary>
    public static async Task<(bool Done, string Message)> SetInputAccessAsync(bool on)
    {
        if (InputRuleScriptPath is not { } script) return (false, "The input access script is missing from this copy of the app.");
        if (SystemPasswordPrompt is not { } prompt) return (false, "This system has no password window for apps (pkexec). Copy the command and run it in a terminal instead.");
        var start = new ProcessStartInfo(prompt) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/bin/bash"); start.ArgumentList.Add(script);
        if (!on) start.ArgumentList.Add("--remove");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The password window could not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string detail = ((await error).Trim() is { Length: > 0 } text ? text : (await output).Trim()).Split('\n').LastOrDefault() ?? "";
        return DescribeInputAccessResult(process.ExitCode, on, detail);
    }

    /// <summary>What a pkexec run of the input-access script means: 126 = the password window was closed, 127 = not
    /// allowed or no password agent running, anything else non-zero = the script itself failed.</summary>
    public static (bool Done, string Message) DescribeInputAccessResult(int exitCode, bool on, string detail) => exitCode switch
    {
        0 => (true, on ? "Input access is on." : "Input access is off."),
        126 => (false, "Cancelled: the password window was closed. Nothing changed."),
        127 => (false, "The system did not allow it, or no password window could open. Copy the command and run it in a terminal instead."),
        _ => (false, "The setup script failed: " + detail)
    };

    private static bool Run(string program, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return false;
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
    private static string Read(string program, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return "";
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
        catch (System.ComponentModel.Win32Exception) { return ""; }
    }
}
