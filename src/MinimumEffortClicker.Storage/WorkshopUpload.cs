namespace MinimumEffortClicker.Storage;

/// <summary>One share: a new item when <paramref name="ExistingItem"/> is null, otherwise an update of that item.</summary>
public sealed record WorkshopUpload(ulong? ExistingItem, string Title, string Description, WorkshopVisibility Visibility,
    string ContentFolder, string? PreviewImage, string ChangeNote);
