using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace MinimumEffortClicker.Linux;

/// <summary>
/// Keeps the desktop awake while playback runs: a GNOME session inhibitor (idle and suspend) when
/// GNOME is present, otherwise a logind sleep/idle inhibitor. Both end with this object or the app.
/// Created and disposed on the scheduler thread; never changes the saved power settings.
/// </summary>
internal sealed class PowerSession : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly bool enabled;
    private DBusConnection? session;
    private uint cookie;
    private SafeFileHandle? logindLock;
    private bool disposed;
    public bool Succeeded { get; }

    public PowerSession(bool keepAwake)
    {
        enabled = keepAwake;
        Succeeded = !enabled || Acquire();
    }

    private bool Acquire()
    {
        try
        {
            session ??= Connect(DBusAddress.Session);
            if (session is not null)
            {
                cookie = Call(session, "org.gnome.SessionManager", "/org/gnome/SessionManager", "org.gnome.SessionManager", "Inhibit", "susu",
                    (ref MessageWriter w) => { w.WriteString("MinimumEffortClicker"); w.WriteUInt32(0); w.WriteString("Minimum Effort Clicker playback is running"); w.WriteUInt32(4 | 8); },
                    (m, _) => m.GetBodyReader().ReadUInt32());
                return true;
            }
        }
        catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException or AggregateException) { cookie = 0; }
        try
        {
            using var system = Connect(DBusAddress.System) ?? throw new TimeoutException();
            logindLock = Call(system, "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", "Inhibit", "ssss",
                (ref MessageWriter w) => { w.WriteString("sleep:idle"); w.WriteString("Minimum Effort Clicker"); w.WriteString("Playback is running"); w.WriteString("block"); },
                (m, _) => m.GetBodyReader().ReadHandle<SafeFileHandle>());
            return true;
        }
        catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException or AggregateException) { return false; }
    }

    private void Release()
    {
        if (cookie != 0 && session is not null)
        {
            try
            {
                Call(session, "org.gnome.SessionManager", "/org/gnome/SessionManager", "org.gnome.SessionManager", "Uninhibit", "u",
                    (ref MessageWriter w) => w.WriteUInt32(cookie), (_, _) => true);
            }
            catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException or AggregateException) { /* The inhibitor ends with the connection below. */ }
            cookie = 0;
        }
        logindLock?.Dispose(); logindLock = null;
    }

    public void SetPaused(bool paused)
    {
        if (disposed || !enabled || !Succeeded) return;
        if (paused) Release();
        else if (!Acquire()) throw new InvalidOperationException("The desktop rejected the playback power-state transition.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (enabled && Succeeded) Release();
        session?.Dispose();
    }

    private static DBusConnection? Connect(string? address)
    {
        if (address is null) return null;
        var connection = new DBusConnection(address);
        if (!Task.Run(() => connection.ConnectAsync().AsTask()).Wait(Timeout)) { connection.Dispose(); throw new TimeoutException(); }
        return connection;
    }

    internal static T Call<T>(DBusConnection connection, string destination, string path, string @interface, string member, string? signature,
        WriteArguments? write, MessageValueReader<T> read)
    {
        MessageBuffer message;
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination, path, @interface, member, signature, MessageFlags.None);
            write?.Invoke(ref writer);
            message = writer.CreateMessage();
        }
        finally { writer.Dispose(); }
        var task = connection.CallMethodAsync(message, read, null);
        if (!task.Wait(Timeout)) throw new TimeoutException();
        return task.Result;
    }
}

public static class PcPower
{
    // Only called after an explicitly reviewed, in-memory schedule and a cancellable app countdown.
    public static void RequestShutdown()
    {
        try
        {
            using var system = new DBusConnection(DBusAddress.System ?? throw new InvalidOperationException("The system bus is unavailable."));
            if (!Task.Run(() => system.ConnectAsync().AsTask()).Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
            PowerSession.Call(system, "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", "PowerOff", "b",
                (ref MessageWriter w) => w.WriteBool(false), (_, _) => true);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException or AggregateException)
        {
            throw new InvalidOperationException("Linux could not receive the shutdown request: " + (ex.InnerException ?? ex).Message, ex);
        }
    }
}
