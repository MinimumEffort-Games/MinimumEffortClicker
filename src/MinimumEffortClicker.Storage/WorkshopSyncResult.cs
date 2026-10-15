namespace MinimumEffortClicker.Storage;

/// <summary>
/// What one pass over the subscribed macros changed in Macros\Workshop. <paramref name="Refused"/> names each macro
/// left out because it presses a system shortcut, with the shortcuts it presses.
/// </summary>
public sealed record WorkshopSyncResult(int Added, int Updated, int Removed, int Skipped, IReadOnlyList<string> Refused)
{
    public bool Changed => Added + Updated + Removed > 0;
}
