using System.Runtime.InteropServices;

namespace MinimumEffortClicker.Linux;

public enum EvdevEventKind { Key, Motion, Wheel, HorizontalWheel }
public readonly record struct EvdevEvent(EvdevEventKind Kind, int Code, int Value);

/// <summary>
/// Read-only observer of physical keyboards and mice through /dev/input. It never grabs a device,
/// so every key still reaches the desktop. Input sent through the GNOME helper's virtual devices
/// does not appear here, which is how physical input is told apart from playback.
/// </summary>
public sealed class EvdevMonitor : IDisposable
{
    private const ushort EvKey = 1, EvRel = 2, EvAbs = 3;
    private const ushort RelX = 0, RelY = 1, RelHWheel = 6, RelWheel = 8;
    private readonly object gate = new();
    private readonly Dictionary<string, int> devices = [];
    private readonly HashSet<int> down = [];
    private readonly Thread thread;
    private readonly int[] wake = new int[2];
    private volatile bool disposed;
    private DateTime nextScan;

    public event Action<EvdevEvent>? Observed;
    public event Action<Exception>? Failed;

    /// <summary>Physical keyboards or mice that this account can read.</summary>
    public static string InputDirectory { get; set; } = "/dev/input";

    public EvdevMonitor()
    {
        if (Native.pipe(wake) != 0) throw new IOException("Could not create the input monitor wake pipe.");
        Rescan();
        thread = new Thread(Loop) { IsBackground = true, Name = "MEC evdev" };
        thread.Start();
    }

    /// <summary>True when at least one keyboard is readable now, which hotkeys and recording need.</summary>
    public static bool KeyboardAccessible()
    {
        foreach (var path in EventNodes())
        {
            int fd = Native.open(path, Native.ORdOnly | Native.ONonBlock | Native.OCloExec);
            if (fd < 0) continue;
            try { if (Classify(fd) is { Keyboard: true }) return true; }
            finally { Native.close(fd); }
        }
        return false;
    }
    /// <summary>True when input nodes exist but this account cannot open them.</summary>
    public static bool AccessDenied() => EventNodes().Any() && !EventNodes().Any(p => Native.access(p, 4) == 0);

    public bool IsDown(int evdevCode) { lock (gate) return down.Contains(evdevCode); }
    public int[] Down { get { lock (gate) return [.. down]; } }

    private static IEnumerable<string> EventNodes()
    {
        try { return Directory.GetFiles(InputDirectory, "event*").OrderBy(p => p, StringComparer.Ordinal).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private void Rescan()
    {
        nextScan = DateTime.UtcNow.AddSeconds(2);
        var present = EventNodes().ToHashSet();
        lock (gate)
        {
            foreach (var gone in devices.Keys.Where(p => !present.Contains(p)).ToArray())
            { Native.close(devices[gone]); devices.Remove(gone); }
            foreach (var path in present)
            {
                if (devices.ContainsKey(path)) continue;
                int fd = Native.open(path, Native.ORdOnly | Native.ONonBlock | Native.OCloExec);
                if (fd < 0) continue;
                if (Classify(fd) is not { } kind || !kind.Keyboard && !kind.Mouse) { Native.close(fd); continue; }
                devices[path] = fd;
                // Seed held keys/buttons so a key already down at startup is still known as physical.
                var state = new byte[96];
                if (Native.ioctl(fd, Ioc(0x18, state.Length), state) >= 0)
                    for (int code = 0; code < state.Length * 8; code++)
                        if ((state[code / 8] & (1 << (code % 8))) != 0) down.Add(code);
            }
        }
    }

    private static (bool Keyboard, bool Mouse)? Classify(int fd)
    {
        var types = new byte[4];
        if (Native.ioctl(fd, Ioc(0x20, types.Length), types) < 0 || (types[0] & (1 << EvKey)) == 0) return null;
        var keys = new byte[96];
        if (Native.ioctl(fd, Ioc(0x20 + EvKey, keys.Length), keys) < 0) return null;
        bool Has(int code) => (keys[code / 8] & (1 << (code % 8))) != 0;
        bool keyboard = Has(30) && Has(44) && Has(57); // A, Z, Space
        // Mice report relative motion; touchpads report absolute finger positions.
        bool mouse = Has(LinuxKeys.ButtonLeft) && (types[0] & (1 << EvRel | 1 << EvAbs)) != 0;
        return (keyboard, mouse);
    }
    private static nuint Ioc(int number, int size) => (nuint)((2u << 30) | ((uint)size << 16) | ('E' << 8) | (uint)number);

    private unsafe void Loop()
    {
        var buffer = new byte[24 * 64];
        try
        {
            while (!disposed)
            {
                if (DateTime.UtcNow >= nextScan) Rescan();
                Native.PollFd[] fds;
                string[] paths;
                lock (gate)
                {
                    paths = [.. devices.Keys];
                    fds = new Native.PollFd[paths.Length + 1];
                    fds[0] = new() { Fd = wake[0], Events = 1 };
                    for (int i = 0; i < paths.Length; i++) fds[i + 1] = new() { Fd = devices[paths[i]], Events = 1 };
                }
                int ready = Native.poll(fds, (nuint)fds.Length, 1000);
                if (disposed) return;
                if (ready <= 0) continue;
                for (int i = 1; i < fds.Length; i++)
                {
                    if ((fds[i].Revents & (Native.PollErr | Native.PollHup | Native.PollNval)) != 0) { Forget(paths[i - 1]); continue; }
                    if ((fds[i].Revents & 1) == 0) continue;
                    nint count;
                    fixed (byte* data = buffer) count = Native.read(fds[i].Fd, data, (nuint)buffer.Length);
                    if (count <= 0) { if (Marshal.GetLastPInvokeError() is not (11 or 4)) Forget(paths[i - 1]); continue; }
                    for (int offset = 0; offset + 24 <= count; offset += 24)
                    {
                        ushort type = BitConverter.ToUInt16(buffer, offset + 16), code = BitConverter.ToUInt16(buffer, offset + 18);
                        int value = BitConverter.ToInt32(buffer, offset + 20);
                        Dispatch(type, code, value);
                    }
                }
            }
        }
        catch (Exception ex) { if (!disposed) Failed?.Invoke(ex); }
    }

    private void Forget(string path)
    {
        lock (gate) { if (devices.Remove(path, out int fd)) Native.close(fd); }
    }

    private void Dispatch(ushort type, ushort code, int value)
    {
        if (type == EvKey)
        {
            if (value == 2) return; // Typematic repeat: the key was already reported down.
            bool changed;
            lock (gate) changed = value != 0 ? down.Add(code) : down.Remove(code);
            if (changed) Observed?.Invoke(new(EvdevEventKind.Key, code, value));
        }
        else if (type == EvRel)
        {
            if (code is RelX or RelY && value != 0) Observed?.Invoke(new(EvdevEventKind.Motion, code, value));
            else if (code == RelWheel && value != 0) Observed?.Invoke(new(EvdevEventKind.Wheel, code, value));
            else if (code == RelHWheel && value != 0) Observed?.Invoke(new(EvdevEventKind.HorizontalWheel, code, value));
        }
        else if (type == EvAbs && code is 0 or 1 or 0x35 or 0x36) Observed?.Invoke(new(EvdevEventKind.Motion, code, value));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Native.write(wake[1], [1], 1);
        thread.Join(TimeSpan.FromSeconds(2));
        lock (gate) { foreach (int fd in devices.Values) Native.close(fd); devices.Clear(); }
        Native.close(wake[0]); Native.close(wake[1]);
    }

    private static unsafe class Native
    {
        public const int ORdOnly = 0, ONonBlock = 0x800, OCloExec = 0x80000;
        public const short PollErr = 8, PollHup = 16, PollNval = 32;
        [StructLayout(LayoutKind.Sequential)] public struct PollFd { public int Fd; public short Events; public short Revents; }
        [DllImport("libc", SetLastError = true)] public static extern int open(string path, int flags);
        [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] public static extern int access(string path, int mode);
        [DllImport("libc", SetLastError = true)] public static extern nint read(int fd, byte* buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern nint write(int fd, byte[] buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern int ioctl(int fd, nuint request, byte[] data);
        [DllImport("libc", SetLastError = true)] public static extern int poll([In, Out] PollFd[] fds, nuint count, int timeout);
        [DllImport("libc", SetLastError = true)] public static extern int pipe(int[] fds);
    }
}
