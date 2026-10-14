using System.Runtime.InteropServices;

namespace MinimumEffortClicker.Linux.Kde;

/// <summary>
/// KWin's fake-input Wayland protocol (org_kde_kwin_fake_input, plasma-wayland-protocols, LGPL-2.1-or-later): absolute
/// pointer moves, buttons, wheel and keys at desktop level. KWin offers it only to programs whose .desktop file lists it
/// under X-KDE-Wayland-Interfaces. Talks to libwayland-client directly; one instance per process, one thread.
/// </summary>
internal interface IInputDevice
{
    void MoveTo(double x, double y);
    void SetButton(uint code, bool pressed);
    void Scroll(uint axis, double value);
    void SetKey(int evdev, bool pressed);
    void Roundtrip();
}

internal sealed unsafe class FakeInput : IInputDevice, IDisposable
{
    private const string Library = "libwayland-client.so.0";
    private const string InterfaceName = "org_kde_kwin_fake_input";
    private const uint WantedVersion = 4; // keyboard_key arrived in version 4.
    private const uint Authenticate = 0, Button = 2, Axis = 3, PointerMotionAbsolute = 9, KeyboardKey = 10;

    private static readonly IntPtr FakeInputInterface = BuildInterface();
    private IntPtr display, registry, fakeInput, listener;
    private GCHandle self;
    private uint globalName, globalVersion;

    private FakeInput() { }

    /// <summary>Connects to the session's Wayland display; null when KWin does not offer fake input to this program.</summary>
    public static FakeInput? Connect(string reason)
    {
        var input = new FakeInput();
        try
        {
            input.display = wl_display_connect(IntPtr.Zero);
            if (input.display == IntPtr.Zero) throw new IOException("Cannot connect to the Wayland display.");
            input.self = GCHandle.Alloc(input);
            var args = stackalloc long[1];
            args[0] = 0;
            input.registry = wl_proxy_marshal_array_flags(input.display, 1, RegistryInterface, wl_proxy_get_version(input.display), 0, args);
            input.listener = Marshal.AllocHGlobal(2 * IntPtr.Size);
            ((IntPtr*)input.listener)[0] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, uint, byte*, uint, void>)&OnGlobal;
            ((IntPtr*)input.listener)[1] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, uint, void>)&OnGlobalRemove;
            wl_proxy_add_listener(input.registry, input.listener, GCHandle.ToIntPtr(input.self));
            input.Roundtrip();
            if (input.globalName == 0) { input.Dispose(); return null; }
            uint version = Math.Min(input.globalVersion, 6);
            if (version < WantedVersion) throw new IOException("KWin's fake input is too old for key presses.");
            var name = Marshal.StringToCoTaskMemUTF8(InterfaceName);
            try
            {
                var bind = stackalloc long[4];
                bind[0] = input.globalName; bind[1] = (long)name; bind[2] = version; bind[3] = 0;
                input.fakeInput = wl_proxy_marshal_array_flags(input.registry, 0, FakeInputInterface, version, 0, bind);
            }
            finally { Marshal.FreeCoTaskMem(name); }
            var application = Marshal.StringToCoTaskMemUTF8("Minimum Effort Clicker");
            var why = Marshal.StringToCoTaskMemUTF8(reason);
            try { input.Send(Authenticate, (long)application, (long)why); }
            finally { Marshal.FreeCoTaskMem(application); Marshal.FreeCoTaskMem(why); }
            input.Roundtrip();
            return input;
        }
        catch { input.Dispose(); throw; }
    }

    /// <summary>Moves the pointer to a logical desktop position.</summary>
    public void MoveTo(double x, double y) => Send(PointerMotionAbsolute, Fixed(x), Fixed(y));
    /// <summary>Linux button code (BTN_LEFT 0x110 ...).</summary>
    public void SetButton(uint code, bool pressed) => Send(Button, code, pressed ? 1 : 0);
    /// <summary>Axis 0 vertical, 1 horizontal, in Wayland axis units; positive scrolls down or right.</summary>
    public void Scroll(uint axis, double value) => Send(Axis, axis, Fixed(value));
    public void SetKey(int evdev, bool pressed) => Send(KeyboardKey, evdev, pressed ? 1 : 0);

    /// <summary>Waits until KWin has handled everything sent so far.</summary>
    public void Roundtrip()
    {
        if (wl_display_roundtrip(display) < 0) throw new IOException("The Wayland connection to KWin was lost.");
    }

    private static long Fixed(double value) => (uint)(int)Math.Round(value * 256);

    private void Send(uint opcode, long first, long second)
    {
        ObjectDisposedException.ThrowIf(fakeInput == IntPtr.Zero && opcode != Authenticate, this);
        var args = stackalloc long[2];
        args[0] = first; args[1] = second;
        wl_proxy_marshal_array_flags(fakeInput, opcode, IntPtr.Zero, wl_proxy_get_version(fakeInput), 0, args);
        if (wl_display_flush(display) < 0) throw new IOException("The Wayland connection to KWin was lost.");
    }

    [UnmanagedCallersOnly]
    private static void OnGlobal(IntPtr data, IntPtr registry, uint name, byte* interfaceName, uint version)
    {
        if (GCHandle.FromIntPtr(data).Target is FakeInput input && Marshal.PtrToStringUTF8((IntPtr)interfaceName) == InterfaceName)
        { input.globalName = name; input.globalVersion = version; }
    }

    [UnmanagedCallersOnly]
    private static void OnGlobalRemove(IntPtr data, IntPtr registry, uint name) { }

    public void Dispose()
    {
        if (display != IntPtr.Zero) wl_display_disconnect(display); // Frees every proxy with it.
        display = registry = fakeInput = IntPtr.Zero;
        if (listener != IntPtr.Zero) { Marshal.FreeHGlobal(listener); listener = IntPtr.Zero; }
        if (self.IsAllocated) self.Free();
    }

    /// <summary>
    /// The protocol's request table, laid out like libwayland's struct wl_interface and struct wl_message. Only the
    /// order and signatures matter to libwayland; the strings and arrays live for the whole process.
    /// </summary>
    private static IntPtr BuildInterface()
    {
        (string Name, string Signature)[] requests =
        [
            ("authenticate", "ss"), ("pointer_motion", "ff"), ("button", "uu"), ("axis", "uf"), ("touch_down", "2uff"),
            ("touch_motion", "2uff"), ("touch_up", "2u"), ("touch_cancel", "2"), ("touch_frame", "2"),
            ("pointer_motion_absolute", "3ff"), ("keyboard_key", "4uu"), ("destroy", "5"), ("keyboard_keysym", "6uu")
        ];
        var types = Marshal.AllocHGlobal(4 * IntPtr.Size);
        for (int i = 0; i < 4; i++) ((IntPtr*)types)[i] = IntPtr.Zero;
        var messages = (IntPtr*)Marshal.AllocHGlobal(requests.Length * 3 * IntPtr.Size);
        for (int i = 0; i < requests.Length; i++)
        {
            messages[i * 3] = Marshal.StringToCoTaskMemUTF8(requests[i].Name);
            messages[i * 3 + 1] = Marshal.StringToCoTaskMemUTF8(requests[i].Signature);
            messages[i * 3 + 2] = types;
        }
        var result = Marshal.AllocHGlobal(sizeof(WaylandInterface));
        *(WaylandInterface*)result = new()
        {
            Name = Marshal.StringToCoTaskMemUTF8(InterfaceName), Version = 6,
            MethodCount = requests.Length, Methods = (IntPtr)messages, EventCount = 0, Events = IntPtr.Zero
        };
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaylandInterface
    {
        public IntPtr Name; public int Version; public int MethodCount; public IntPtr Methods; public int EventCount; public IntPtr Events;
    }

    private static readonly IntPtr RegistryInterface = NativeLibrary.GetExport(NativeLibrary.Load(Library), "wl_registry_interface");

    [DllImport(Library)] private static extern IntPtr wl_display_connect(IntPtr name);
    [DllImport(Library)] private static extern void wl_display_disconnect(IntPtr display);
    [DllImport(Library)] private static extern int wl_display_roundtrip(IntPtr display);
    [DllImport(Library)] private static extern int wl_display_flush(IntPtr display);
    [DllImport(Library)] private static extern uint wl_proxy_get_version(IntPtr proxy);
    [DllImport(Library)] private static extern int wl_proxy_add_listener(IntPtr proxy, IntPtr implementation, IntPtr data);
    [DllImport(Library)] private static extern IntPtr wl_proxy_marshal_array_flags(IntPtr proxy, uint opcode, IntPtr @interface, uint version, uint flags, long* args);
}
