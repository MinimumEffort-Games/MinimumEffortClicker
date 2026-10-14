using Tmds.DBus.Protocol;

namespace MinimumEffortClicker.Linux;

/// <summary>Writes call arguments. MessageWriter is a ref struct, so it must be passed by reference.</summary>
public delegate void WriteArguments(ref MessageWriter writer);

/// <summary>
/// Blocking client for the ME Clicker GNOME Shell helper extension. Every call has a bounded wait so
/// a stalled compositor can never hang the scheduler. Signals arrive on a D-Bus reader thread.
/// </summary>
public sealed class GnomeHelper : IDesktopHelper
{
    public const string BusName = "io.github.MinimumEffortGames.ClickerHelper";
    public const string ObjectPath = "/io/github/MinimumEffortGames/ClickerHelper";
    public const string InterfaceName = "io.github.MinimumEffortGames.ClickerHelper1";
    public const uint RequiredVersion = 1;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);
    private readonly DBusConnection connection;
    private readonly List<IDisposable> subscriptions = [];
    // Signals are handled on one ordered thread, never on the D-Bus reader thread: a handler that
    // calls the helper back must not wait for a reply that only the blocked reader could deliver.
    private readonly System.Collections.Concurrent.BlockingCollection<Action> signals = new(256);
    private Thread? signalThread;
    private volatile bool disposed;

    public string Name => "GNOME helper";
    public event Action<ulong>? FocusChanged;
    public event Action<ulong>? WindowClosed;
    public event Action<uint, bool>? AcceleratorChanged;

    private GnomeHelper(DBusConnection connection) => this.connection = connection;

    /// <summary>Connects and checks that the helper is installed, enabled and allows this program.</summary>
    public static GnomeHelper Connect()
    {
        var address = DBusAddress.Session ?? throw new HelperException("No desktop session bus is available.") { NotInstalled = true };
        var connection = new DBusConnection(address);
        try
        {
            if (!Task.Run(() => connection.ConnectAsync().AsTask()).Wait(CallTimeout)) throw new HelperException("The desktop session bus did not respond.");
            var helper = new GnomeHelper(connection);
            uint version = helper.Call("GetVersion", null, null, (m, _) => m.GetBodyReader().ReadUInt32());
            if (version < RequiredVersion) throw new HelperException("The ME Clicker GNOME helper is out of date. Install it again from Settings.") { NotInstalled = true };
            helper.Subscribe();
            return helper;
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        { connection.Dispose(); throw Translate(ex.InnerException); }
        catch { connection.Dispose(); throw; }
    }

    private void Subscribe()
    {
        signalThread = new Thread(() =>
        {
            foreach (var action in signals.GetConsumingEnumerable())
            {
                try { action(); } catch (Exception) { /* A failing observer must not end signal delivery. */ }
            }
        }) { IsBackground = true, Name = "MEC helper signals" };
        signalThread.Start();
        Watch("FocusChanged", (m, _) => m.GetBodyReader().ReadUInt64(), id => FocusChanged?.Invoke(id));
        Watch("WindowClosed", (m, _) => m.GetBodyReader().ReadUInt64(), id => WindowClosed?.Invoke(id));
        Watch("AcceleratorActivated", (m, _) => m.GetBodyReader().ReadUInt32(), action => AcceleratorChanged?.Invoke(action, true));
        Watch("AcceleratorDeactivated", (m, _) => m.GetBodyReader().ReadUInt32(), action => AcceleratorChanged?.Invoke(action, false));
    }
    private void Watch<T>(string signal, MessageValueReader<T> read, Action<T> handle)
    {
        // Subscribe off any UI synchronization context: the blocking wait below must not deadlock it.
        var task = Task.Run(() => connection.WatchSignalAsync(BusName, ObjectPath, InterfaceName, signal, read,
            (Notification<T> notification) => { if (notification.HasValue && !disposed) { var value = notification.Value; signals.TryAdd(() => handle(value)); } },
            ObserverFlags.None, false, null).AsTask());
        if (!task.Wait(CallTimeout)) throw new HelperException("Could not subscribe to GNOME helper notifications.");
        subscriptions.Add(task.Result);
    }

    public (int X, int Y) GetPointer() => Call("GetPointer", null, null, (m, _) => { var r = m.GetBodyReader(); return (r.ReadInt32(), r.ReadInt32()); });

    public IReadOnlyList<HelperMonitor> GetMonitors() => Call("GetMonitors", null, null, (m, _) =>
    {
        var r = m.GetBodyReader(); var list = new List<HelperMonitor>();
        var end = r.ReadArrayStart(DBusType.Struct);
        while (r.HasNext(end)) { r.AlignStruct(); list.Add(new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble())); }
        return (IReadOnlyList<HelperMonitor>)list;
    });

    public IReadOnlyList<HelperWindow> ListWindows() => Call("ListWindows", null, null, (m, _) =>
    {
        var r = m.GetBodyReader(); var list = new List<HelperWindow>();
        var end = r.ReadArrayStart(DBusType.Struct);
        while (r.HasNext(end)) { r.AlignStruct(); list.Add(new(r.ReadUInt64(), r.ReadString(), r.ReadInt32())); }
        return (IReadOnlyList<HelperWindow>)list;
    });

    public ulong GetFocusWindow() => Call("GetFocusWindow", null, null, (m, _) => m.GetBodyReader().ReadUInt64());

    public HelperWindowState GetWindow(ulong id) => Call("GetWindow", "t", (ref MessageWriter w) => w.WriteUInt64(id), (m, _) =>
    {
        var r = m.GetBodyReader();
        return new HelperWindowState(r.ReadBool(), r.ReadBool(), r.ReadInt32(), r.ReadString(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble());
    });

    public HelperTargetState QueryTarget(ulong id) => Call("QueryTarget", "t", (ref MessageWriter w) => w.WriteUInt64(id), (m, _) =>
    {
        var r = m.GetBodyReader();
        return new HelperTargetState(r.ReadUInt64(), r.ReadBool(), r.ReadBool(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
            r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadInt32(), r.ReadInt32(), r.ReadBool());
    });

    public (ulong Id, int ProcessId) WindowAt(int x, int y, string skipTitle) => Call("WindowAt", "iis", (ref MessageWriter w) => { w.WriteInt32(x); w.WriteInt32(y); w.WriteString(skipTitle); },
        (m, _) => { var r = m.GetBodyReader(); return (r.ReadUInt64(), r.ReadInt32()); });

    public (bool InputReady, string Detail) GetShellState() => Call("GetShellState", null, null, (m, _) => { var r = m.GetBodyReader(); return (r.ReadBool(), r.ReadString()); });

    public string GetKeyboardLayout() => Call("GetKeyboardLayout", null, null, (m, _) => m.GetBodyReader().ReadString());

    public bool ActivateWindow(ulong id) => Call("ActivateWindow", "t", (ref MessageWriter w) => w.WriteUInt64(id), (m, _) => m.GetBodyReader().ReadBool());

    public void PointerMove(double x, double y) => Send("PointerMove", "dd", (ref MessageWriter w) => { w.WriteDouble(x); w.WriteDouble(y); });
    public void PointerButton(uint button, bool pressed) => Send("PointerButton", "ub", (ref MessageWriter w) => { w.WriteUInt32(button); w.WriteBool(pressed); });
    public void Scroll(uint direction, uint steps) => Send("Scroll", "uu", (ref MessageWriter w) => { w.WriteUInt32(direction); w.WriteUInt32(steps); });
    public void Key(int evdev, bool pressed) => Send("Key", "ub", (ref MessageWriter w) => { w.WriteUInt32((uint)evdev); w.WriteBool(pressed); });
    public void ReleaseAll() => Send("ReleaseAll", null, null);

    public HelperImage CaptureRaw(int x, int y, int width, int height) => Call("CaptureRaw", "iiii", (ref MessageWriter w) => { w.WriteInt32(x); w.WriteInt32(y); w.WriteInt32(width); w.WriteInt32(height); },
        (m, _) => { var r = m.GetBodyReader(); return new HelperImage(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadArrayOfByte()); }, CaptureTimeout);

    /// <summary>A region of one window's own content (client-relative), unaffected by windows above it.</summary>
    public HelperImage CaptureWindowRaw(ulong id, int x, int y, int width, int height) => Call("CaptureWindowRaw", "tiiii",
        (ref MessageWriter w) => { w.WriteUInt64(id); w.WriteInt32(x); w.WriteInt32(y); w.WriteInt32(width); w.WriteInt32(height); },
        (m, _) => { var r = m.GetBodyReader(); return new HelperImage(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadArrayOfByte()); }, CaptureTimeout);

    public uint GrabAccelerator(string accelerator) => Call("GrabAccelerator", "s", (ref MessageWriter w) => w.WriteString(accelerator), (m, _) => m.GetBodyReader().ReadUInt32());
    public void UngrabAccelerator(uint action) => Send("UngrabAccelerator", "u", (ref MessageWriter w) => w.WriteUInt32(action));

    private void Send(string member, string? signature, WriteArguments? write) => Call(member, signature, write, (_, _) => true);

    private T Call<T>(string member, string? signature, WriteArguments? write, MessageValueReader<T> read, TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        MessageBuffer message;
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(BusName, ObjectPath, InterfaceName, member, signature, MessageFlags.None);
            write?.Invoke(ref writer);
            message = writer.CreateMessage();
        }
        finally { writer.Dispose(); }
        var task = connection.CallMethodAsync(message, read, null);
        try
        {
            if (!task.Wait(timeout ?? CallTimeout)) throw new HelperException("The GNOME helper did not respond in time.");
            return task.Result;
        }
        catch (AggregateException ex) when (ex.InnerException is not null) { throw Translate(ex.InnerException); }
    }

    private static Exception Translate(Exception error) => error switch
    {
        HelperException => error,
        DBusErrorReplyException reply when reply.ErrorName.EndsWith(".AccessDenied", StringComparison.Ordinal)
            => new HelperException("The ME Clicker GNOME helper does not allow this copy of the app. Install the helper again from Settings.", error) { AccessDenied = true },
        DBusErrorReplyException reply when reply.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner"
            => new HelperException("The ME Clicker GNOME helper is not running. Install it from Settings, then log out and back in.", error) { NotInstalled = true },
        DBusErrorReplyException reply => new HelperException(reply.ErrorMessage, error),
        _ => new HelperException("The GNOME helper is unavailable: " + error.Message, error)
    };

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var subscription in subscriptions) subscription.Dispose();
        signals.CompleteAdding();
        if (signalThread is not null && signalThread != Thread.CurrentThread) signalThread.Join(TimeSpan.FromSeconds(2));
        connection.Dispose();
    }
}
