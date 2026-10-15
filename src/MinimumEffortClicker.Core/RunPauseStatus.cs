namespace MinimumEffortClicker.Core;

/// <summary>
/// What the status line and the run log say when the engine pauses or unpauses a run. Windows and Linux share it so
/// both keep the real reason a run ended.
/// </summary>
public static class RunPauseStatus
{
    public const string TargetLost = "Paused: the target window is not in front. It continues by itself when the window is back in front. Stop All cancels.";
    public const string TargetBack = "The target window is back in front. Continuing.";
    public const string ResumedByUser = "Resumed explicitly after target and physical-input checks.";
    public const string PausedByUser = "Paused with input released. Resume rechecks the target and continues remaining timing or retries this condition.";

    /// <summary>
    /// The new state and reason, or null to keep both. The engine also unpauses from its cleanup when a paused run is
    /// cancelled; that is not a resume, so the stop reason set by whoever stopped the run is kept for the log.
    /// </summary>
    public static (string State, string Reason)? After(bool paused, bool automatic, bool cancelling, string currentReason)
    {
        if (!paused && cancelling) return null;
        if (paused) return ("Paused", automatic ? TargetLost : PausedByUser);
        return ("Running", currentReason == TargetBack ? TargetBack : ResumedByUser);
    }
}
