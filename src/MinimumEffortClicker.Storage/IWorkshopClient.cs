namespace MinimumEffortClicker.Storage;

/// <summary>
/// The Workshop as the app sees it. Only the Steam build has a real one; the GitHub build has none, and tests use a
/// fake. Calls come from the UI thread; <see cref="Pump"/> runs Steam's callbacks there.
/// </summary>
public interface IWorkshopClient : IDisposable
{
    /// <summary>False while Steam is not running or refused the app; <see cref="UnavailableReason"/> says why.</summary>
    bool Available { get; }
    string? UnavailableReason { get; }
    /// <summary>Subscriptions or downloads changed; read <see cref="InstalledItems"/> again.</summary>
    event Action? ItemsChanged;
    void Pump();
    Task<WorkshopPublishResult> PublishAsync(WorkshopUpload upload, IProgress<double>? progress = null);
    /// <summary>Subscribed items that are on disk. Asks Steam to download subscribed items that are not there yet.</summary>
    IReadOnlyList<WorkshopInstalledItem> InstalledItems();
    void OpenWorkshop();
    void OpenItem(ulong itemId);
}
