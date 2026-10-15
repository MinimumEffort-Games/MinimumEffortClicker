using Tmds.DBus.Protocol;

namespace MinimumEffortClicker.Linux;

/// <summary>
/// Reports the Linux equivalents of the Windows session-switch and power-mode events: the screen
/// locking (GNOME's screensaver, or org.freedesktop.ScreenSaver that KWin serves on KDE) and the computer preparing to sleep (logind). Both stop playback.
/// </summary>
public sealed class SessionWatcher : IDisposable
{
    private readonly List<IDisposable> subscriptions = [];
    private readonly List<DBusConnection> connections = [];
    private volatile bool disposed;
    public event Action? SessionChanged;
    public event Action? PowerChanged;

    public SessionWatcher()
    {
        if (LinuxSetup.IsKde) Watch(DBusAddress.Session, "org.freedesktop.ScreenSaver", "/ScreenSaver", "org.freedesktop.ScreenSaver", "ActiveChanged", active => { if (active) SessionChanged?.Invoke(); });
        else Watch(DBusAddress.Session, "org.gnome.ScreenSaver", "/org/gnome/ScreenSaver", "org.gnome.ScreenSaver", "ActiveChanged", active => { if (active) SessionChanged?.Invoke(); });
        Watch(DBusAddress.System, "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", "PrepareForSleep", start => { if (start) PowerChanged?.Invoke(); });
    }

    private void Watch(string? address, string sender, string path, string @interface, string signal, Action<bool> handle)
    {
        if (address is null) return;
        try
        {
            var connection = new DBusConnection(address);
            if (!Task.Run(() => connection.ConnectAsync().AsTask()).Wait(TimeSpan.FromSeconds(2))) { connection.Dispose(); return; }
            connections.Add(connection);
            var task = Task.Run(() => connection.WatchSignalAsync(sender, path, @interface, signal, (m, _) => m.GetBodyReader().ReadBool(),
                (Notification<bool> notification) =>
                {
                    if (!notification.HasValue || disposed) return;
                    bool value = notification.Value;
                    ThreadPool.QueueUserWorkItem(_ => { try { handle(value); } catch (Exception) { /* Observers must not end the watch. */ } });
                }, ObserverFlags.None, false, null).AsTask());
            if (task.Wait(TimeSpan.FromSeconds(2))) subscriptions.Add(task.Result);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or AggregateException or InvalidOperationException) { /* Optional: the app still stops on helper loss. */ }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var subscription in subscriptions) subscription.Dispose();
        foreach (var connection in connections) connection.Dispose();
    }
}
