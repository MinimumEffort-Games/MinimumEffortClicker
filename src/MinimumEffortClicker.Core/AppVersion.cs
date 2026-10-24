namespace MinimumEffortClicker.Core;

/// <summary>
/// The version as people read it, for Settings and Help (User, 2026-10-23: Steam shows no build number, so the app says
/// which build runs). The build stamps "1.0.0+&lt;full commit&gt;"; this shows "1.0.0 (617701c)".
/// </summary>
public static class AppVersion
{
    public static string Display(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "unknown";
        int plus = informationalVersion.IndexOf('+');
        if (plus < 0) return informationalVersion;
        string commit = informationalVersion[(plus + 1)..];
        return commit.Length == 0 ? informationalVersion[..plus] : $"{informationalVersion[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
    }
}
