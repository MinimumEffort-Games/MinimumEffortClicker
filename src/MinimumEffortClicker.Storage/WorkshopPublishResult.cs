namespace MinimumEffortClicker.Storage;

/// <summary>
/// What a share ended with. <paramref name="ItemGone"/> means the item to update no longer exists (deleted on Steam),
/// so the caller can forget it and share as a new item. <paramref name="NeedsLegalAgreement"/> means Steam keeps the
/// item hidden until the user accepts the Workshop agreement on the item's page.
/// </summary>
public sealed record WorkshopPublishResult(bool Success, ulong ItemId, string Message, bool NeedsLegalAgreement = false, bool ItemGone = false);
