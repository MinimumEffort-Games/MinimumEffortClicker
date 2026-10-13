using Tmds.DBus.Protocol;

namespace MinimumEffortClicker.Linux;

/// <summary>Desktop notification bubbles (org.freedesktop.Notifications), the Linux counterpart of tray balloons.</summary>
public static class DesktopNotifications
{
    private static DBusConnection? connection;
    private static uint lastId;

    public static void Show(string title, string message)
    {
        try
        {
            if (connection is null)
            {
                var created = new DBusConnection(DBusAddress.Session ?? throw new InvalidOperationException());
                if (!Task.Run(() => created.ConnectAsync().AsTask()).Wait(TimeSpan.FromSeconds(2))) { created.Dispose(); return; }
                connection = created;
            }
            string body = message.Length > 250 ? message[..250] + "…" : message;
            lastId = PowerSession.Call(connection, "org.freedesktop.Notifications", "/org/freedesktop/Notifications", "org.freedesktop.Notifications", "Notify", "susssasa{sv}i",
                (ref MessageWriter w) =>
                {
                    w.WriteString("Minimum Effort Clicker"); w.WriteUInt32(lastId); w.WriteString("input-mouse");
                    w.WriteString(title); w.WriteString(body);
                    w.WriteArray(Array.Empty<string>());
                    var dictionary = w.WriteDictionaryStart(); w.WriteDictionaryEnd(dictionary);
                    w.WriteInt32(4000);
                }, (m, _) => m.GetBodyReader().ReadUInt32());
        }
        catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException or AggregateException or InvalidOperationException) { /* Notifications are optional. */ }
    }
}
