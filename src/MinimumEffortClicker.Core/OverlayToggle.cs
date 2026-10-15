namespace MinimumEffortClicker.Core;

/// <summary>One enabled toggle as the status overlay lists it: "Name · Key · State".</summary>
public sealed record OverlayToggle(string Name, string Key, string State, bool Active)
{
    public const string ReadyState = "Ready", OnState = "On", ReleasePendingState = "Release pending";

    /// <summary>The overlay paints "On" in blue so a running toggle stands out from the ready ones.</summary>
    public bool On => State == OnState;

    /// <summary>The state word for a toggle; a paused run shows the run's own state (for example "Paused") instead of "On".</summary>
    public static string StateFor(bool cleanupPending, bool active, bool playbackActive, string runtimeState) =>
        cleanupPending ? ReleasePendingState : !active ? ReadyState : playbackActive ? OnState : runtimeState;
}
