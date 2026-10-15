using System.Diagnostics;
using MinimumEffortClicker.Storage;
using Steamworks;

namespace MinimumEffortClicker.Steam;

/// <summary>
/// The Steam Workshop through Steamworks.NET. It goes online only through the Steam client: when the user shares a
/// macro, and when Steam fetches items the user subscribed to. Every call runs on the UI thread, and so do Steam's
/// results, because <see cref="Pump"/> runs the callbacks from the app's timer.
/// </summary>
public sealed class SteamWorkshopClient : IWorkshopClient
{
    public const uint AppId = 5440990;
    private static readonly AppId_t App = new(AppId);
    // Steam forgets a pending result whose CallResult is collected, so each one is held until it reports.
    private readonly HashSet<object> pending = [];
    private readonly List<IDisposable> callbacks = [];
    private (UGCUpdateHandle_t Handle, IProgress<double> Progress)? uploading;
    private bool disposed;

    public bool Available { get; }
    public string? UnavailableReason { get; }
    public event Action? ItemsChanged;

    private SteamWorkshopClient(string? unavailableReason)
    {
        Available = unavailableReason is null; UnavailableReason = unavailableReason;
        if (!Available) return;
        callbacks.Add(Callback<ItemInstalled_t>.Create(e => { if (e.m_unAppID == App) ItemsChanged?.Invoke(); }));
        callbacks.Add(Callback<RemoteStoragePublishedFileSubscribed_t>.Create(e => { if (e.m_nAppID == App) ItemsChanged?.Invoke(); }));
        callbacks.Add(Callback<RemoteStoragePublishedFileUnsubscribed_t>.Create(e => { if (e.m_nAppID == App) ItemsChanged?.Invoke(); }));
    }

    /// <summary>
    /// Steam's rule for a Steam app started outside Steam: Steam starts it again and this copy exits. True means exit
    /// now. A developer copy with steam_appid.txt beside the exe is allowed to run directly.
    /// </summary>
    public static bool RestartThroughSteam()
    {
        try { return SteamAPI.RestartAppIfNecessary(App); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return false; }
    }

    /// <summary>Connects to the running Steam client. Without it the clicker works as usual and the Workshop says why it is off.</summary>
    public static SteamWorkshopClient Start()
    {
        try
        {
            return SteamAPI.Init() ? new(null)
                : new("Steam is not running, or this Steam account does not own Minimum Effort Clicker. Start Steam and open the clicker from your library.");
        }
        catch (DllNotFoundException) { return new("This Steam build is missing steam_api64.dll. Verify the files in Steam (Properties > Installed Files)."); }
        catch (Exception ex) when (ex is EntryPointNotFoundException or BadImageFormatException) { return new("This Steam build has a steam_api64.dll from another Steamworks SDK version (it needs 1.60). Verify the files in Steam."); }
    }

    public void Pump()
    {
        if (!Available || disposed) return;
        SteamAPI.RunCallbacks();
        if (uploading is { } upload && SteamUGC.GetItemUpdateProgress(upload.Handle, out ulong sent, out ulong total) != EItemUpdateStatus.k_EItemUpdateStatusInvalid && total > 0)
            upload.Progress.Report(Math.Clamp((double)sent / total, 0, 1));
    }

    public Task<WorkshopPublishResult> PublishAsync(WorkshopUpload upload, IProgress<double>? progress = null)
    {
        if (!Available) return Task.FromResult(new WorkshopPublishResult(false, 0, UnavailableReason!));
        if (uploading is not null || pending.Count > 0) return Task.FromResult(new WorkshopPublishResult(false, 0, "A share is still being uploaded. Wait for it to finish."));
        var done = new TaskCompletionSource<WorkshopPublishResult>();
        if (upload.ExistingItem is { } existing) { Submit(new PublishedFileId_t(existing), upload, progress, done, needsAgreement: false); return done.Task; }
        CallResult<CreateItemResult_t>? create = null;
        create = CallResult<CreateItemResult_t>.Create((result, ioFailure) =>
        {
            pending.Remove(create!);
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            { done.TrySetResult(new(false, 0, Failure("Steam could not create the Workshop item", result.m_eResult, ioFailure))); return; }
            Submit(result.m_nPublishedFileId, upload, progress, done, result.m_bUserNeedsToAcceptWorkshopLegalAgreement);
        });
        pending.Add(create);
        create.Set(SteamUGC.CreateItem(App, EWorkshopFileType.k_EWorkshopFileTypeCommunity));
        return done.Task;
    }

    private void Submit(PublishedFileId_t item, WorkshopUpload upload, IProgress<double>? progress, TaskCompletionSource<WorkshopPublishResult> done, bool needsAgreement)
    {
        var handle = SteamUGC.StartItemUpdate(App, item);
        SteamUGC.SetItemTitle(handle, upload.Title);
        SteamUGC.SetItemDescription(handle, upload.Description);
        SteamUGC.SetItemVisibility(handle, (ERemoteStoragePublishedFileVisibility)(int)upload.Visibility);
        SteamUGC.SetItemContent(handle, upload.ContentFolder);
        if (upload.PreviewImage is { } preview) SteamUGC.SetItemPreview(handle, preview);
        CallResult<SubmitItemUpdateResult_t>? submit = null;
        submit = CallResult<SubmitItemUpdateResult_t>.Create((result, ioFailure) =>
        {
            pending.Remove(submit!); uploading = null;
            ulong id = item.m_PublishedFileId;
            if (!ioFailure && result.m_eResult == EResult.k_EResultFileNotFound && upload.ExistingItem is not null)
                done.TrySetResult(new(false, id, "The Workshop item for this macro was deleted on Steam.", ItemGone: true));
            else if (ioFailure || result.m_eResult != EResult.k_EResultOK)
                done.TrySetResult(new(false, id, Failure("Steam could not upload the macro", result.m_eResult, ioFailure)));
            else
            {
                bool agreement = needsAgreement || result.m_bUserNeedsToAcceptWorkshopLegalAgreement;
                done.TrySetResult(new(true, id, agreement
                    ? "Shared, but Steam keeps it hidden until you accept the Workshop agreement on the item's page."
                    : upload.ExistingItem is null ? "Shared on the Workshop." : "Workshop item updated.", agreement));
            }
        });
        pending.Add(submit);
        if (progress is not null) uploading = (handle, progress);
        submit.Set(SteamUGC.SubmitItemUpdate(handle, upload.ChangeNote));
    }

    private static string Failure(string what, EResult result, bool ioFailure) =>
        ioFailure ? what + ": the connection to Steam failed. Check that Steam is online and try again."
        : result switch
        {
            EResult.k_EResultInsufficientPrivilege => what + ": this account may not post to the Workshop (new or limited accounts cannot).",
            EResult.k_EResultBanned => what + ": this account is banned from posting to the Workshop.",
            EResult.k_EResultTimeout or EResult.k_EResultServiceUnavailable or EResult.k_EResultBusy => what + ": Steam is busy. Try again in a minute.",
            EResult.k_EResultNotLoggedOn => what + ": Steam is offline. Go online in Steam and try again.",
            EResult.k_EResultLimitExceeded => what + ": the Steam Cloud space for this account is full.",
            _ => $"{what} ({result})."
        };

    public IReadOnlyList<WorkshopInstalledItem> InstalledItems()
    {
        if (!Available) return [];
        uint count = SteamUGC.GetNumSubscribedItems();
        var ids = new PublishedFileId_t[count];
        count = SteamUGC.GetSubscribedItems(ids, count);
        var items = new List<WorkshopInstalledItem>();
        foreach (var id in ids.Take((int)count))
        {
            var state = (EItemState)SteamUGC.GetItemState(id);
            if (state.HasFlag(EItemState.k_EItemStateInstalled) && SteamUGC.GetItemInstallInfo(id, out _, out string folder, 1024, out _))
                items.Add(new(id.m_PublishedFileId, folder));
            // Subscribed but not on disk yet, or outdated: ask Steam to fetch it; ItemInstalled reports when it lands.
            if ((!state.HasFlag(EItemState.k_EItemStateInstalled) || state.HasFlag(EItemState.k_EItemStateNeedsUpdate)) && !state.HasFlag(EItemState.k_EItemStateDownloading))
                SteamUGC.DownloadItem(id, false);
        }
        return items;
    }

    public void OpenWorkshop() => OpenSteam($"steam://url/SteamWorkshopPage/{AppId}");
    public void OpenItem(ulong itemId) => OpenSteam($"steam://url/CommunityFilePage/{itemId}");
    // The Steam overlay cannot draw over a desktop app, so pages open in the Steam client itself.
    private static void OpenSteam(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var callback in callbacks) callback.Dispose();
        callbacks.Clear(); pending.Clear(); uploading = null;
        if (Available) SteamAPI.Shutdown();
    }
}
