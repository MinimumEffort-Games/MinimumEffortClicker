namespace MinimumEffortClicker.Storage;

/// <summary>
/// One share from start to finish: copy the saved macro into an upload folder, create or update its Workshop item,
/// share again as a new item when the old one was deleted on Steam, and remember the item for next time. The temporary
/// upload folder is always removed.
/// </summary>
public sealed class WorkshopPublisher(IWorkshopClient client, WorkshopShares shares, string workFolder)
{
    /// <param name="previewImage">The chosen picture, or null to keep the item's current one.</param>
    /// <param name="defaultPreview">Makes the picture a new item gets when none was chosen (the app logo).</param>
    public async Task<WorkshopPublishResult> ShareAsync(Guid macroId, string macroFile, string title, string description,
        WorkshopVisibility visibility, string? previewImage, Func<string> defaultPreview, IProgress<double>? progress = null)
    {
        var previous = shares.Find(macroId);
        string content = await Task.Run(() => WorkshopMacros.PrepareUpload(macroFile, workFolder));
        try
        {
            var upload = new WorkshopUpload(previous?.ItemId, title, description, visibility, content,
                previewImage ?? (previous is null ? defaultPreview() : null), previous is null ? "First version." : "Updated from Minimum Effort Clicker.");
            var result = await client.PublishAsync(upload, progress);
            if (result.ItemGone)
            {
                shares.Set(macroId, null);
                result = await client.PublishAsync(upload with { ExistingItem = null, PreviewImage = previewImage ?? defaultPreview(), ChangeNote = "First version." }, progress);
            }
            if (result.Success) shares.Set(macroId, new(result.ItemId, title, description, visibility));
            return result;
        }
        finally { try { Directory.Delete(content, true); } catch (IOException) { /* Windows clears the temp folder. */ } }
    }
}
