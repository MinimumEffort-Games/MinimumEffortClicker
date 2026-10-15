namespace MinimumEffortClicker.Core;

/// <summary>
/// The status overlay's shortcut line. Windows and Linux both read it from here so the two overlays always say the same
/// thing, and the keys shown are the ones the user actually bound.
/// </summary>
public static class OverlayHints
{
    public const string BothMouseButtons = "LMB+RMB";

    public static string StartStop(string startKey) => $"{startKey} to Start/Stop";

    /// <summary>
    /// While the Action List runs, the line names the emergency stop the user chose; while only toggles are on, the stop
    /// key stops them. With both running, the stop key is named "Stop All" because it stops the Action List too.
    /// </summary>
    public static string WhileRunning(string startKey, string stopKey, EmergencyStopMode mode, bool actionsActive, bool togglesActive)
    {
        var line = StartStop(startKey);
        if (actionsActive && mode == EmergencyStopMode.BothMouseButtons) line += $" - {BothMouseButtons} to Stop";
        if (actionsActive && (togglesActive || mode == EmergencyStopMode.AnyInput)) return line + $" - {stopKey} to Stop All";
        return togglesActive ? line + $" - {stopKey} to Stop All Toggles" : line;
    }

    /// <summary>
    /// The overlay's short form of an Action List run: "Actions · 10 actions · until stopped" becomes
    /// "10 Actions · Looping". Other activities are shown unchanged.
    /// </summary>
    public static string Activity(string activity)
    {
        const string prefix = "Actions · ";
        if (!activity.StartsWith(prefix, StringComparison.Ordinal)) return activity;
        var parts = activity[prefix.Length..].Split(" · ");
        for (int i = 0; i < parts.Length; i++)
            parts[i] = parts[i] switch
            {
                "until stopped" => "Looping",
                "once" => "Once",
                var part when part.EndsWith(" actions", StringComparison.Ordinal) => part[..^" actions".Length] + " Actions",
                var part when part.EndsWith(" action", StringComparison.Ordinal) => part[..^" action".Length] + " Action",
                var part when part.EndsWith(" loops", StringComparison.Ordinal) => part[..^" loops".Length] + " Loops",
                var part => part
            };
        return string.Join(" · ", parts);
    }
}
