namespace MinimumEffortClicker.Storage;

/// <summary>What one pass over the subscribed macros changed in Macros\Workshop.</summary>
public sealed record WorkshopSyncResult(int Added, int Updated, int Removed, int Skipped)
{
    public bool Changed => Added + Updated + Removed > 0;
}
