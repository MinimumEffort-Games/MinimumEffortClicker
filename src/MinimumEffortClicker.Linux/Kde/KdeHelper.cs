using System.Text.Json.Nodes;
using Tmds.DBus.Protocol;

namespace MinimumEffortClicker.Linux.Kde;

/// <summary>
/// The desktop helper on KDE Plasma (Wayland). Nothing to install into Plasma: KWin itself answers. Windows, focus,
/// pointer and monitors come from a KWin script (<see cref="KWinScript"/>), pixels from ScreenShot2
/// (<see cref="KWinCapture"/>), input from a separate guard process (<see cref="KdeInputGuard"/>), shortcuts from
/// KGlobalAccel (<see cref="KdeShortcuts"/>), the layout from org.kde.keyboard and the lock state from
/// org.freedesktop.ScreenSaver. ScreenShot2 and fake input need the clicker's .desktop file (see install.sh).
/// </summary>
public sealed class KdeHelper : IDesktopHelper
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);
    private readonly DBusConnection connection;
    private readonly WindowIds ids = new();
    private readonly System.Collections.Concurrent.BlockingCollection<Action> signals = new(256);
    private readonly Thread signalThread;
    private readonly List<IDisposable> subscriptions = [];
    private KWinScript? script;
    private KdeShortcuts? shortcuts;
    private KdeInput? input;
    private volatile bool locked, disposed;
    // KWin moves focus the moment it is asked, and the scheduler checks the target hundreds of times a second. Focus is
    // therefore reported from the last focus event the app has already handled, so the runtime hears that the target
    // left the front (and pauses) before any check can see it gone, the order GNOME gives.
    private long deliveredFocus;

    public string Name => "KDE helper";
    public event Action<ulong>? FocusChanged;
    public event Action<ulong>? WindowClosed;
    public event Action<uint, bool>? AcceleratorChanged;

    private KdeHelper(DBusConnection connection)
    {
        this.connection = connection;
        // Handlers run here, never on the D-Bus reader: one that asks KWin something must not wait on that reader.
        signalThread = new Thread(() =>
        {
            foreach (var action in signals.GetConsumingEnumerable())
            {
                try { action(); } catch (Exception) { /* A failing observer must not end signal delivery. */ }
            }
        }) { IsBackground = true, Name = "MEC KDE helper signals" };
        signalThread.Start();
    }

    /// <summary>Connects to KWin and checks that this copy of the app may capture the screen and send input.</summary>
    public static KdeHelper Connect()
    {
        var address = DBusAddress.Session ?? throw new HelperException("No desktop session bus is available.") { NotInstalled = true };
        var connection = new DBusConnection(address);
        KdeHelper? helper = null;
        try
        {
            connectStage = "the session bus";
            if (!Task.Run(() => connection.ConnectAsync().AsTask()).Wait(CallTimeout)) throw new HelperException("The desktop session bus did not respond.");
            helper = new KdeHelper(connection);
            helper.Start();
            return helper;
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        { connectStage = "cleaning up after a failed connect"; helper?.Dispose(); connection.Dispose(); throw Translate(ex.InnerException); }
        catch { connectStage = "cleaning up after a failed connect"; helper?.Dispose(); connection.Dispose(); throw; }
        finally { connectStage = null; }
    }

    // Which part of connecting is under way, for the app's startup log: a Steam Deck start stalled with nothing logged (2026-10-19).
    private static volatile string? connectStage;
    /// <summary>The part of <see cref="Connect"/> under way, or null while no connect runs.</summary>
    public static string? ConnectStage => connectStage;

    private void Start()
    {
        connectStage = "the screen lock state";
        if (ScreenLocked(connection) is not { } isLocked)
            throw new HelperException("KWin's screen lock service is not answering; this does not look like a KDE Plasma session.") { NotInstalled = true };
        locked = isLocked;
        connectStage = "following the screen lock";
        var watch = Task.Run(() => connection.WatchSignalAsync(null, null, "org.freedesktop.ScreenSaver", "ActiveChanged",
            (m, _) => m.GetBodyReader().ReadBool(), (Notification<bool> n) => { if (n.HasValue) locked = n.Value; }, ObserverFlags.None, false, null).AsTask());
        if (!watch.Wait(CallTimeout)) throw new HelperException("Could not follow the screen lock.");
        subscriptions.Add(watch.Result);
        connectStage = "removing old shortcuts";
        KdeShortcuts.RemoveAll(connection); // Left behind by a clicker that could not clean up.
        connectStage = "loading the KWin window script";
        script = new KWinScript(connection, OnScriptEvent);
        script.Load();
        // Focus events start arriving now; until the first one, the focus is what KWin says at this moment.
        connectStage = "asking KWin for the focus";
        Interlocked.CompareExchange(ref deliveredFocus, (long)ids.For((string?)script.Ask("focus") ?? ""), 0);
        connectStage = "the screen capture check";
        try { KWinCapture.Area(connection, 0, 0, 1, 1); }
        catch (HelperException ex) when (ex.AccessDenied)
        { throw new HelperException("KDE does not allow this copy of the app to capture the screen. Choose Allow this copy of the app in Settings.", ex) { AccessDenied = true }; }
        connectStage = "starting the input guard";
        input = new KdeInput();
        input.Start();
        connectStage = "following KDE shortcuts";
        shortcuts = new KdeShortcuts(connection, Dispatch);
        shortcuts.Changed += (action, pressed) => AcceleratorChanged?.Invoke(action, pressed);
    }

    private void Dispatch(Action action) { if (!disposed) signals.TryAdd(action); }

    private void OnScriptEvent(string text)
    {
        if (text.Length < 2) return;
        string window = text[2..];
        if (text[0] == 'f')
        {
            ulong id = ids.For(window);
            Dispatch(() =>
            {
                try { FocusChanged?.Invoke(id); }
                finally { Interlocked.Exchange(ref deliveredFocus, (long)id); }
            });
        }
        else if (text[0] == 'c' && ids.Known(window) is { } closed) Dispatch(() => WindowClosed?.Invoke(closed));
    }

    private KWinScript Script => script ?? throw new ObjectDisposedException(nameof(KdeHelper));
    private KdeInput Input => input ?? throw new ObjectDisposedException(nameof(KdeHelper));

    public (int X, int Y) GetPointer() => Point(Script.Ask("pointer"));

    public IReadOnlyList<HelperMonitor> GetMonitors() =>
        Script.Ask("monitors")!.AsArray().Select(m => new HelperMonitor((int)m![0]!, (int)m[1]!, (int)m[2]!, (int)m[3]!, (double)m[4]!)).ToArray();

    public IReadOnlyList<HelperWindow> ListWindows() =>
        Script.Ask("windows")!.AsArray().Select(w => new HelperWindow(ids.For((string)w![0]!), (string)w[1]!, (int)w[2]!)).ToArray();

    public ulong GetFocusWindow() => (ulong)Interlocked.Read(ref deliveredFocus);

    public HelperWindowState GetWindow(ulong id) => ids.Name(id) is { } name ? State(Script.Ask("window", q => q["i"] = name)) : default;

    public HelperTargetState QueryTarget(ulong id)
    {
        string? name = ids.Name(id);
        var answer = Script.Ask("target", q => q["i"] = name ?? "")!;
        var window = State(answer["w"]);
        var (x, y) = Point(answer["p"]);
        return new((ulong)Interlocked.Read(ref deliveredFocus), window.Exists, window.Minimized, window.ProcessId, window.X, window.Y, window.Width,
            window.Height, window.Scale, x, y, !locked);
    }

    public (ulong Id, int ProcessId) WindowAt(int x, int y, string skipTitle)
    {
        var answer = Script.Ask("at", q => { q["x"] = x; q["y"] = y; q["k"] = skipTitle; })!;
        return (ids.For((string)answer[0]!), (int)answer[1]!);
    }

    public (bool InputReady, string Detail) GetShellState() => (!locked, locked ? "locked" : "unlocked");

    public string GetKeyboardLayout()
    {
        uint index = Call(connection, "org.kde.keyboard", "/Layouts", "org.kde.KeyboardLayouts", "getLayout", null, null, (m, _) => m.GetBodyReader().ReadUInt32());
        var layouts = Call(connection, "org.kde.keyboard", "/Layouts", "org.kde.KeyboardLayouts", "getLayoutsList", null, null, (m, _) =>
        {
            var r = m.GetBodyReader(); var names = new List<string>();
            var end = r.ReadArrayStart(DBusType.Struct);
            while (r.HasNext(end)) { r.AlignStruct(); names.Add(r.ReadString()); r.ReadString(); r.ReadString(); }
            return names;
        });
        return index < layouts.Count ? Layout(layouts[(int)index], index, ReadKxkbrc()) : "";
    }

    /// <summary>"xkb:de" or "xkb:us+dvorak"; KDE's keyboard service leaves the variant out, kxkbrc keeps it.</summary>
    internal static string Layout(string layout, uint index, string? kxkbrc)
    {
        string variant = "";
        foreach (var line in (kxkbrc ?? "").Split('\n'))
        {
            if (!line.StartsWith("VariantList=", StringComparison.Ordinal)) continue;
            var variants = line["VariantList=".Length..].Trim().Split(',');
            if (index < variants.Length) variant = variants[index].Trim();
        }
        return "xkb:" + layout + (variant.Length > 0 ? "+" + variant : "");
    }

    private static string? ReadKxkbrc()
    {
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } home ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        try { return File.ReadAllText(Path.Combine(config, "kxkbrc")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public bool ActivateWindow(ulong id) => ids.Name(id) is { } name && (bool)Script.Ask("activate", q => q["i"] = name)!;

    /// <summary>KWin reports the pointer rounded to whole pixels, so moves go to whole pixels too and read back exactly.</summary>
    public void PointerMove(double x, double y) => Input.Send(FormattableString.Invariant($"m {Math.Floor(x)} {Math.Floor(y)}"));
    public void PointerMoveBy(double dx, double dy) => Input.Send(FormattableString.Invariant($"l {dx} {dy}"));
    public void PointerButton(uint button, bool pressed) => Input.Send($"b {LinuxButton(button)} {(pressed ? 1 : 0)}");
    public void Scroll(uint direction, uint steps)
    {
        if (direction > 3 || steps is < 1 or > 100) throw new HelperException("Unsupported scroll.");
        // Fake input has no notch count, so Xwayland counts 10 axis units as one notch: X11 apps and most games get
        // exactly one notch per step (measured); Wayland apps get 10 pixels of smooth scrolling. Up and left are negative.
        int value = (int)steps * 10 * (direction is 0 or 2 ? -1 : 1);
        Input.Send($"s {(direction < 2 ? 0 : 1)} {value}");
    }
    public void Key(int evdev, bool pressed)
    {
        if (evdev is < 1 or > 0x2ff) throw new HelperException("Unsupported key code.");
        Input.Send($"k {evdev} {(pressed ? 1 : 0)}");
    }
    public void ReleaseAll() => Input.Send("r");

    /// <summary>The helper's button numbers (1 left, 2 middle, 3 right, 8 back, 9 forward) as Linux button codes.</summary>
    internal static uint LinuxButton(uint button) => button switch
    {
        1 => 0x110, 2 => 0x112, 3 => 0x111, 8 => 0x113, 9 => 0x114,
        _ => throw new HelperException("Unsupported pointer button.")
    };

    public HelperImage CaptureRaw(int x, int y, int width, int height) => KWinCapture.Area(connection, x, y, width, height);

    public HelperImage CaptureWindowRaw(ulong id, int x, int y, int width, int height)
    {
        var window = GetWindow(id);
        if (!window.Exists || window.Minimized || ids.Name(id) is not { } name) throw new HelperException("The window is closed or minimized.");
        var image = KWinCapture.Window(connection, name);
        double scaleX = (double)image.Width / Math.Max(1, window.Width), scaleY = (double)image.Height / Math.Max(1, window.Height);
        return KWinCapture.Crop(image, (int)Math.Round(x * scaleX), (int)Math.Round(y * scaleY), (int)Math.Round(width * scaleX), (int)Math.Round(height * scaleY));
    }

    public uint GrabAccelerator(string accelerator) => (shortcuts ?? throw new ObjectDisposedException(nameof(KdeHelper))).Grab(accelerator);
    public void UngrabAccelerator(uint action) => shortcuts?.Ungrab(action);

    private HelperWindowState State(JsonNode? state) => state is null ? default
        : new(true, (bool)state["m"]!, (int)state["p"]!, (string)state["t"]!, (int)state["x"]!, (int)state["y"]!, (int)state["w"]!, (int)state["h"]!, (double)state["s"]!);

    private static (int X, int Y) Point(JsonNode? point) => ((int)point![0]!, (int)point[1]!);

    /// <summary>Whether the screen is locked; null when org.freedesktop.ScreenSaver does not answer.</summary>
    internal static bool? ScreenLocked(DBusConnection connection)
    {
        try { return Call(connection, "org.freedesktop.ScreenSaver", "/ScreenSaver", "org.freedesktop.ScreenSaver", "GetActive", null, null, (m, _) => m.GetBodyReader().ReadBool()); }
        catch (HelperException) { return null; }
    }

    /// <summary>One blocking D-Bus call with a bounded wait; failures become <see cref="HelperException"/>.</summary>
    internal static T Call<T>(DBusConnection connection, string service, string path, string @interface, string member, string? signature,
        WriteArguments? write, MessageValueReader<T> read, TimeSpan? timeout = null)
    {
        MessageBuffer message;
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(service, path, @interface, member, signature, MessageFlags.None);
            write?.Invoke(ref writer);
            message = writer.CreateMessage();
        }
        finally { writer.Dispose(); }
        var task = connection.CallMethodAsync(message, read, null);
        try
        {
            if (!task.Wait(timeout ?? CallTimeout)) throw new HelperException($"KDE did not answer in time ({member}).");
            return task.Result;
        }
        catch (AggregateException ex) when (ex.InnerException is not null) { throw Translate(ex.InnerException); }
    }

    private static Exception Translate(Exception error) => error switch
    {
        HelperException => error,
        DBusErrorReplyException reply when reply.ErrorName.EndsWith(".NoAuthorized", StringComparison.Ordinal)
            => new HelperException(reply.ErrorMessage, error) { AccessDenied = true },
        DBusErrorReplyException reply when reply.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner"
            => new HelperException("KWin is not answering. The KDE helper needs a KDE Plasma session.", error) { NotInstalled = true },
        DBusErrorReplyException reply => new HelperException(reply.ErrorMessage, error),
        _ => new HelperException("KDE is unavailable: " + error.Message, error)
    };

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        shortcuts?.Dispose();
        input?.Dispose();
        script?.Dispose();
        foreach (var subscription in subscriptions) subscription.Dispose();
        signals.CompleteAdding();
        if (signalThread != Thread.CurrentThread) signalThread.Join(TimeSpan.FromSeconds(2));
        connection.Dispose();
    }

    /// <summary>KWin names windows with UUIDs; the app works with numbers. Numbers are never reused.</summary>
    internal sealed class WindowIds
    {
        private readonly object gate = new();
        private readonly Dictionary<string, ulong> numbers = [];
        private readonly Dictionary<ulong, string> names = [];
        private ulong next = 1;

        /// <summary>The number for a KWin window id; 0 for none.</summary>
        public ulong For(string name)
        {
            if (name.Length == 0) return 0;
            lock (gate)
            {
                if (numbers.TryGetValue(name, out var id)) return id;
                id = next++; numbers[name] = id; names[id] = name;
                return id;
            }
        }
        public ulong? Known(string name) { lock (gate) return numbers.TryGetValue(name, out var id) ? id : null; }
        public string? Name(ulong id) { lock (gate) return names.TryGetValue(id, out var name) ? name : null; }
    }
}
