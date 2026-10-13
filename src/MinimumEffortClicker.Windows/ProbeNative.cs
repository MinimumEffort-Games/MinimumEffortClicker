using System.ComponentModel;
using System.Runtime.InteropServices;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Windows;

public static class ProbeNative
{
    public const nuint InputTag = 0x4D454350;
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort Key; public ushort Scan; public uint Flags; public uint Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }

    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint window, out Rect rectangle);
    [DllImport("user32.dll")] public static extern bool GetClientRect(nint window, out Rect rectangle);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] public static extern nint GetWindowDpiAwarenessContext(nint window);
    [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", SetLastError = true)] internal static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] public static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] public static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    internal static bool SendMouse(InputEdge edge) => SendInput(1, [new Input
    {
        Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = edge == InputEdge.Down ? 2u : 4u, Extra = InputTag } }
    }], Marshal.SizeOf<Input>()) == 1;

    /// <summary>Only the isolated probe calls this, to exercise its registered F9 route.</summary>
    public static bool SendProbeStopKey()
        => SendProbeKey(0x78, InputTag);
    /// <summary>Isolated fixture for the external stop hook while playback owns a modifier.</summary>
    public static bool SendProbeExternalStopKey() => SendProbeKey(0x78, 0x4D455454);

    /// <summary>Bounded M1 VM fixture only; distinct tag models an external trigger, never playback.</summary>
    public static bool SendProbeToggleKey() => SendProbeKey(0x77, 0x4D455454);
    public static bool SendProbeMacroKey() => SendProbeKey(0x75, 0x4D455454); // F6, bounded opt-in fixture.

    private static bool SendProbeKey(ushort key, nuint tag)
    {
        var down = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyInput { Key = key, Extra = tag } } };
        var up = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyInput { Key = key, Flags = 2, Extra = tag } } };
        var sent = SendInput(2, [down, up], Marshal.SizeOf<Input>());
        if (sent == 1) SendInput(1, [up], Marshal.SizeOf<Input>());
        return sent == 2;
    }
}

/// <summary>Probe adapter accepts left transitions only, and downs only over its bound foreground window.</summary>
public sealed class BoundProbeInput(nint target, uint processId) : IInputSink
{
    public bool TargetAvailable => ProbeNative.IsWindow(target) && !ProbeNative.IsIconic(target)
        && ProbeNative.GetForegroundWindow() == target && ProbeNative.GetWindowThreadProcessId(target, out var actual) != 0 && actual == processId;
    public bool TrySend(InputTransition transition)
    {
        if (transition.Button != PointerButton.Left) return false;
        if (transition.Edge == InputEdge.Down)
        {
            if (!TargetAvailable || (ProbeNative.GetAsyncKeyState(1) & 0x8000) != 0) return false;
            if (!ProbeNative.GetWindowRect(target, out var rect) || !ProbeNative.GetCursorPos(out var point)
                || point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom) return false;
        }
        return ProbeNative.SendMouse(transition.Edge);
    }
}

public sealed class ProbeStopHotkey : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new(false);
    private uint threadId;
    private Exception? failure;
    public ProbeStopHotkey(Action stop)
    {
        thread = new Thread(() =>
        {
            threadId = ProbeNative.GetCurrentThreadId();
            ProbeNative.PeekMessage(out _, 0, 0, 0, 0);
            bool registered = ProbeNative.RegisterHotKey(0, 1, 0x4000, 0x78);
            if (!registered) failure = new Win32Exception(Marshal.GetLastWin32Error(), "F9 stop hotkey is unavailable; probe cannot run.");
            ready.Set();
            if (!registered) return;
            try
            {
                int result;
                while ((result = ProbeNative.GetMessage(out var message, 0, 0, 0)) > 0)
                    if (message.Id == 0x0312) stop();
                if (result < 0) stop();
            }
            finally { ProbeNative.UnregisterHotKey(0, 1); }
        }) { IsBackground = true, Name = "MEC probe stop control" };
        thread.Start();
        ready.Wait();
        if (failure is not null) { ready.Dispose(); throw failure; }
    }
    public void Dispose()
    {
        ProbeNative.PostThreadMessage(threadId, 0x0012, 0, 0);
        thread.Join(TimeSpan.FromSeconds(3));
        ready.Dispose();
    }
}
