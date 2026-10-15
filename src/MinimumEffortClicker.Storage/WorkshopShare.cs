namespace MinimumEffortClicker.Storage;

/// <summary>
/// A macro's Workshop item and the text it was last shared with. Steam replaces the title and description on every
/// update, so the share window starts from these instead of blank fields.
/// </summary>
public sealed record WorkshopShare(ulong ItemId, string Title, string Description, WorkshopVisibility Visibility);
