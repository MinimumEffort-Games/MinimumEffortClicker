using System.Runtime.InteropServices;
using System.Text;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Windows;

internal static class DesktopNative
{
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassNameW(nint window, StringBuilder name, int size);

    internal delegate bool EnumWindow(nint hwnd, nint parameter);
    internal delegate nint Hook(int code, nuint message, nint data);
    internal delegate void WinEvent(nint hook, uint evt, nint hwnd, int obj, int child, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardEvent { public uint Key, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseEvent { public ProbeNative.Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Union Data; }
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out ProbeNative.Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref ProbeNative.Point point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(ProbeNative.Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookExW(int id, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [StructLayout(LayoutKind.Sequential)] internal struct LastInputInfo { public uint Size, Time; }
    [DllImport("user32.dll")] internal static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("user32.dll")] internal static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] internal static extern bool KillTimer(nint window, nuint id);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKeyExW(uint code, uint mapType, nint layout);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
    // Raw mouse movement for Look recording: a message-only window receives WM_INPUT while a recording runs.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowExW(uint exStyle, string className, string? title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] internal static extern nint DispatchMessageW(ref ProbeNative.Message message);
    [StructLayout(LayoutKind.Sequential)] internal struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] internal static extern uint GetRawInputData(nint rawInput, uint command, [Out] byte[]? data, ref uint size, uint headerSize);
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public uint Size, Flags; public nint Cursor; public ProbeNative.Point Position; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetClipCursor(out ProbeNative.Rect rect);

    /// <summary>
    /// The cursor is held by the app in front, the way first- and third-person games hold it: hidden, or kept inside a
    /// smaller area than the desktop. Mouse movement then turns the camera instead of moving a pointer.
    /// </summary>
    internal static bool CursorHeld()
    {
        var info = new CursorInfo { Size = (uint)Marshal.SizeOf<CursorInfo>() };
        if (GetCursorInfo(ref info) && (info.Flags & 1) == 0) return true;
        if (!GetClipCursor(out var clip)) return false;
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
        return clip.Left > left || clip.Top > top || clip.Right < left + GetSystemMetrics(78) || clip.Bottom < top + GetSystemMetrics(79);
    }

    internal static int VirtualKey(InputControl control) => control.Kind switch
    {
        InputKind.ScanCode => (int)MapVirtualKey((uint)((control.Code & 0x100) != 0 ? 0xE000 | (control.Code & 0xFF) : control.Code), 3),
        InputKind.MouseButton => (PointerButton)control.Code switch { PointerButton.Left => 1, PointerButton.Right => 2, PointerButton.Middle => 4, PointerButton.XButton1 => 5, _ => 6 },
        _ => 0
    };

    internal static bool Send(InputTransition transition, ProbeNative.Point? position = null)
    {
        var control = transition.Control;
        if (!Enum.IsDefined(control.Kind)) return false;
        bool up = transition.Edge == InputEdge.Up;
        Input input;
        Input? move = null;
        if (position is { } point)
        {
            try
            {
                move = new Input { Type = 0, Data = new() { Mouse = new() {
                    X = ClientPoint.NormalizeAbsolute(point.X, GetSystemMetrics(76), GetSystemMetrics(78)),
                    Y = ClientPoint.NormalizeAbsolute(point.Y, GetSystemMetrics(77), GetSystemMetrics(79)),
                    Flags = 0xC001, Extra = ProbeNative.InputTag } } };
            }
            catch (ArgumentOutOfRangeException) { return false; }
        }
        if (control.Kind == InputKind.PointerMove)
            return move is { } movement && SendInput(1, [movement], Marshal.SizeOf<Input>()) == 1;
        if (control.Kind == InputKind.Look)
        {
            // Relative movement (MOUSEEVENTF_MOVE without ABSOLUTE), the counts a game reads as camera movement.
            var (dx, dy) = control.LookStep;
            var look = new Input { Type = 0, Data = new() { Mouse = new() { X = dx, Y = dy, Flags = 0x0001, Extra = ProbeNative.InputTag } } };
            return SendInput(1, [look], Marshal.SizeOf<Input>()) == 1;
        }
        if (control.Kind == InputKind.ScanCode)
            input = new() { Type = 1, Data = new() { Key = new() { Scan = (ushort)(control.Code & 0xFF),
                Flags = 8u | (up ? 2u : 0u) | ((control.Code & 0x100) != 0 ? 1u : 0u), Extra = ProbeNative.InputTag } } };
        else
        {
            uint flags = control.Kind == InputKind.Scroll ? 0x800u : control.Kind == InputKind.HorizontalScroll ? 0x1000u : (PointerButton)control.Code switch
            {
                PointerButton.Left => up ? 4u : 2u, PointerButton.Right => up ? 0x10u : 8u,
                PointerButton.Middle => up ? 0x40u : 0x20u, _ => up ? 0x100u : 0x80u
            };
            uint data = control.Kind is InputKind.Scroll or InputKind.HorizontalScroll ? unchecked((uint)control.Code)
                : control.Code == (int)PointerButton.XButton1 ? 1u : control.Code == (int)PointerButton.XButton2 ? 2u : 0;
            input = new() { Type = 0, Data = new() { Mouse = new() { Flags = flags, Data = data, Extra = ProbeNative.InputTag } } };
        }
        // Movement precedes the edge. A partial count of one accepted only the move and owns no input.
        return move is { } first ? SendInput(2, [first, input], Marshal.SizeOf<Input>()) == 2
            : SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }
}
