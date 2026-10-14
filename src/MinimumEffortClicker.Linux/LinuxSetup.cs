using System.Diagnostics;

namespace MinimumEffortClicker.Linux;

/// <summary>Detects the desktop session and installs the GNOME helper for the current user.</summary>
public static class LinuxSetup
{
    public const string HelperUuid = "me-clicker-helper@minimumeffort-games.github.io";
    public const string InputRuleScript = "setup-input-access.sh";

    public static string SessionType => Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown";
    public static string Desktop => Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "unknown";
    public static bool IsGnome => Desktop.Split(':').Any(d => d.Equals("GNOME", StringComparison.OrdinalIgnoreCase));

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
        var source = BundledHelperDirectory ?? throw new InvalidOperationException("The GNOME helper files are missing from this copy of the app.");
        string executable = Environment.ProcessPath is { } process ? Path.GetFullPath(new FileInfo(process).ResolveLinkTarget(true)?.FullName ?? process)
            : throw new InvalidOperationException("Cannot find this program's path.");
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet")
            throw new InvalidOperationException("Start the clicker from its own executable (not 'dotnet …dll') so the helper can recognise it.");
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

    public static string InputAccessCommand => InputRuleScriptPath is { } script ? $"sudo bash '{script}'" : "The input access script is missing from this copy of the app.";
    /// <summary>
    /// Removes the rule again. The rule only gives the person signed in at this computer read access (logind's uaccess),
    /// instead of adding the account to the input group. Null when the script is missing.
    /// </summary>
    public static string? InputAccessRemoveCommand => InputRuleScriptPath is { } script ? $"sudo bash '{script}' --remove" : null;

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
