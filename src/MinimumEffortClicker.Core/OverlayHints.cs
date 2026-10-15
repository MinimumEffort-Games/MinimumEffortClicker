namespace MinimumEffortClicker.Core;

/// <summary>
/// The status overlay's wording. Windows and Linux both read it from here so the two overlays always say the same
/// thing, and the keys shown are the ones the user actually bound.
/// </summary>
public static class OverlayHints
{
    /// <summary>The start key, on the left of the overlay's top line.</summary>
    public static string StartStop(string startKey) => $"{startKey} to Start/Stop";

    /// <summary>The one stop while anything runs, in red beside the overlay's buttons.</summary>
    public static string StopAll(string stopKey) => $"{stopKey} to Stop All";

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
