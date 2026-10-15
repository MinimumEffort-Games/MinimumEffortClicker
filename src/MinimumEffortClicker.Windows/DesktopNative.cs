using System.Runtime.InteropServices;
using System.Text;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Windows;

internal static class DesktopNative
{
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);

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
