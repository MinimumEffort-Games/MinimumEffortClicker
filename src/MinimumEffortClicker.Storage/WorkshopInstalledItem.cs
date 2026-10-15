namespace MinimumEffortClicker.Storage;

/// <summary>A subscribed Workshop item that Steam has downloaded into <paramref name="Folder"/>.</summary>
public sealed record WorkshopInstalledItem(ulong Id, string Folder);
