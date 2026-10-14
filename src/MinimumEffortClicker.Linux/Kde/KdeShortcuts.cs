using Tmds.DBus.Protocol;

namespace MinimumEffortClicker.Linux.Kde;

/// <summary>
/// Global shortcuts through KDE's KGlobalAccel (served by KWin). KGlobalAccel accepts a key that is already taken and
/// then never fires it, so each key is checked against every other owner first. While registered, the key does not
/// also reach the app in front, as on GNOME. The entries show in System Settings → Shortcuts while the clicker runs and
/// are removed when it ends (by the input guard if the clicker crashed).
/// </summary>
internal sealed class KdeShortcuts : IDisposable
{
    public const string Component = "minimum-effort-clicker";
    private const string FriendlyName = "Minimum Effort Clicker";
    private const string Service = "org.kde.kglobalaccel", Path = "/kglobalaccel", Interface = "org.kde.KGlobalAccel";
    private const string ComponentInterface = "org.kde.kglobalaccel.Component";
    private const uint SetPresent = 2, NoAutoloading = 4;

    private readonly DBusConnection connection;
    private readonly object gate = new();
    private readonly Dictionary<uint, string> actions = [];
    private readonly List<IDisposable> subscriptions = [];
    private uint next = 1;

    /// <summary>action id, pressed</summary>
    public event Action<uint, bool>? Changed;

    public KdeShortcuts(DBusConnection connection, Action<Action> dispatch)
    {
        this.connection = connection;
        foreach (var (signal, pressed) in new[] { ("globalShortcutPressed", true), ("globalShortcutReleased", false) })
        {
            var task = Task.Run(() => connection.WatchSignalAsync(Service, null, ComponentInterface, signal,
                (m, _) => { var r = m.GetBodyReader(); return (Component: r.ReadString(), Action: r.ReadString()); },
                (Notification<(string Component, string Action)> n) =>
                {
                    if (!n.HasValue || n.Value.Component != Component) return;
                    uint? id;
                    lock (gate) id = actions.FirstOrDefault(a => a.Value == n.Value.Action) is { Value: not null } found ? found.Key : null;
                    if (id is { } action) dispatch(() => Changed?.Invoke(action, pressed));
                }, ObserverFlags.None, false, null).AsTask());
            if (!task.Wait(TimeSpan.FromSeconds(2))) throw new HelperException("Could not follow KDE shortcut presses.");
            subscriptions.Add(task.Result);
        }
    }

    /// <summary>Registers a key such as "F8" or "KP_1"; throws when KDE or another app already uses it.</summary>
    public uint Grab(string accelerator)
    {
        int key = QtKey(accelerator) ?? throw new HelperException($"{accelerator} cannot be a KDE shortcut.");
        string action = "key-" + accelerator;
        string? owner = Owners(key).FirstOrDefault(o => o.Component != Component).Name;
        if (owner is not null) throw new HelperException($"{accelerator} is already used by another shortcut ({owner}).");
        string[] id = [Component, action, FriendlyName, accelerator];
        KdeHelper.Call(connection, Service, Path, Interface, "doRegister", "as", (ref MessageWriter w) => w.WriteArray(id), (_, _) => true);
        bool granted = KdeHelper.Call(connection, Service, Path, Interface, "setShortcutKeys", "asa(ai)u", (ref MessageWriter w) =>
        {
            w.WriteArray(id);
            var list = w.WriteArrayStart(DBusType.Struct);
            w.WriteStructureStart(); w.WriteArray(new[] { key, 0, 0, 0 });
            w.WriteArrayEnd(list);
            w.WriteUInt32(SetPresent | NoAutoloading);
        }, (m, _) =>
        {
            var r = m.GetBodyReader(); bool found = false;
            var end = r.ReadArrayStart(DBusType.Struct);
            while (r.HasNext(end)) { r.AlignStruct(); if (r.ReadArrayOfInt32() is [var first, ..] && first == key) found = true; }
            return found;
        });
        if (!granted) { Unregister(connection, action); throw new HelperException($"{accelerator} is already used by another shortcut."); }
        lock (gate) { actions[next] = action; return next++; }
    }

    public void Ungrab(uint id)
    {
        string? action;
        lock (gate) { if (!actions.Remove(id, out action)) return; }
        Unregister(connection, action);
    }

    private IEnumerable<(string Component, string Name)> Owners(int key) =>
        KdeHelper.Call(connection, Service, Path, Interface, "getGlobalShortcutsByKey", "i", (ref MessageWriter w) => w.WriteInt32(key), (m, _) =>
        {
            // KGlobalShortcutInfo: (uniqueName, friendlyName, componentUniqueName, componentFriendlyName, contextUniqueName,
            // contextFriendlyName, keys, defaultKeys)
            var r = m.GetBodyReader(); var owners = new List<(string, string)>();
            var end = r.ReadArrayStart(DBusType.Struct);
            while (r.HasNext(end))
            {
                r.AlignStruct();
                r.ReadString(); string friendly = r.ReadString(); string component = r.ReadString(); string componentName = r.ReadString();
                r.ReadString(); r.ReadString(); SkipKeys(ref r); SkipKeys(ref r);
                owners.Add((component, $"{componentName}: {friendly}"));
            }
            return owners;
        });

    private static void SkipKeys(ref Reader r)
    {
        var end = r.ReadArrayStart(DBusType.Int32);
        while (r.HasNext(end)) r.ReadInt32();
    }

    private static void Unregister(DBusConnection connection, string action)
    {
        try
        {
            KdeHelper.Call(connection, Service, Path, Interface, "unregister", "ss",
                (ref MessageWriter w) => { w.WriteString(Component); w.WriteString(action); }, (m, _) => m.GetBodyReader().ReadBool());
        }
        catch (HelperException) { /* Already gone. */ }
    }

    /// <summary>Removes every clicker shortcut from KDE, including ones a crashed clicker left behind.</summary>
    public static void RemoveAll()
    {
        if (DBusAddress.Session is not { } address) return;
        try
        {
            using var connection = new DBusConnection(address);
            if (!Task.Run(() => connection.ConnectAsync().AsTask()).Wait(TimeSpan.FromSeconds(2))) return;
            RemoveAll(connection);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or AggregateException or InvalidOperationException or HelperException) { }
    }

    public static void RemoveAll(DBusConnection connection)
    {
        string componentPath;
        try
        {
            componentPath = KdeHelper.Call(connection, Service, Path, Interface, "getComponent", "s", (ref MessageWriter w) => w.WriteString(Component),
                (m, _) => m.GetBodyReader().ReadObjectPathAsString());
        }
        catch (HelperException) { return; } // Never registered.
        var names = KdeHelper.Call(connection, Service, componentPath, ComponentInterface, "shortcutNames", null, null, (m, _) => m.GetBodyReader().ReadArrayOfString());
        foreach (var name in names) Unregister(connection, name);
    }

    /// <summary>Qt key code for the accelerator names <see cref="LinuxKeys.Accelerator"/> produces.</summary>
    internal static int? QtKey(string accelerator)
    {
        const int Keypad = 0x20000000;
        if (accelerator.Length == 1 && char.IsAsciiLetterOrDigit(accelerator[0])) return char.ToUpperInvariant(accelerator[0]);
        if (accelerator.Length > 1 && accelerator[0] == 'F' && int.TryParse(accelerator[1..], out int f) && f is >= 1 and <= 35) return 0x01000030 + f - 1;
        if (accelerator.Length == 4 && accelerator.StartsWith("KP_", StringComparison.Ordinal) && char.IsAsciiDigit(accelerator[3])) return Keypad | accelerator[3];
        return accelerator switch
        {
            "BackSpace" => 0x01000003, "Tab" => 0x01000001, "Return" => 0x01000004, "Pause" => 0x01000008, "Caps_Lock" => 0x01000024,
            "Escape" => 0x01000000, "space" => 0x20, "Page_Up" => 0x01000016, "Page_Down" => 0x01000017, "End" => 0x01000011,
            "Home" => 0x01000010, "Left" => 0x01000012, "Up" => 0x01000013, "Right" => 0x01000014, "Down" => 0x01000015,
            "Print" => 0x01000009, "Insert" => 0x01000006, "Delete" => 0x01000007, "KP_Multiply" => Keypad | '*', "KP_Add" => Keypad | '+',
            "KP_Subtract" => Keypad | '-', "KP_Decimal" => Keypad | '.', "KP_Divide" => Keypad | '/', "Num_Lock" => 0x01000025,
            "Scroll_Lock" => 0x01000026,
            _ => null
        };
    }

    public void Dispose()
    {
        foreach (var subscription in subscriptions) subscription.Dispose();
        string[] remaining;
        lock (gate) { remaining = actions.Values.ToArray(); actions.Clear(); }
        foreach (var action in remaining) Unregister(connection, action);
    }
}
