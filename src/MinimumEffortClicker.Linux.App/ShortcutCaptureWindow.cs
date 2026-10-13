using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

internal sealed class ShortcutCaptureWindow : DialogWindow
{
    public int? SelectedKey { get; private set; }
    public ShortcutCaptureWindow(Window owner, string purpose)
    {
        OwnerWindow = owner; Title = purpose; Width = 430; Height = 225; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        var hint = new TextBlock { Text = "Press a key, or click a mouse button inside this box.\nOne key or button is captured. Use Cancel to keep your current choice.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15) };
        panel.Children.Add(hint);
        var cancel = CancelButton(); cancel.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(cancel);
        void Select(int key)
        {
            if (!ShortcutKeys.Supported(key)) { hint.Text = "That key cannot be used here. Press another key or click Cancel."; return; }
            SelectedKey = key; DialogResult = true;
        }
        // Tunnel, so buttons and access keys never see the captured key first.
        AddHandler(KeyDownEvent, (_, e) => { e.Handled = true; Select(VirtualKey(e.PhysicalKey)); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            // The explicit cancel control is excluded from mouse binding capture.
            if (cancel.IsPointerOver) return;
            e.Handled = true;
            var properties = e.GetCurrentPoint(this).Properties;
            Select(properties.PointerUpdateKind switch
            {
                PointerUpdateKind.LeftButtonPressed => 1, PointerUpdateKind.RightButtonPressed => 2, PointerUpdateKind.MiddleButtonPressed => 4,
                PointerUpdateKind.XButton1Pressed => 5, PointerUpdateKind.XButton2Pressed => 6, _ => 0
            });
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += (_, _) => { Activate(); Focus(); };
    }

    /// <summary>Physical key position to the Windows virtual-key identity used by saved shortcuts.</summary>
    internal static int VirtualKey(PhysicalKey key) => key switch
    {
        >= PhysicalKey.A and <= PhysicalKey.Z => 'A' + (key - PhysicalKey.A),
        >= PhysicalKey.Digit0 and <= PhysicalKey.Digit9 => '0' + (key - PhysicalKey.Digit0),
        >= PhysicalKey.F1 and <= PhysicalKey.F24 => 0x70 + (key - PhysicalKey.F1),
        >= PhysicalKey.NumPad0 and <= PhysicalKey.NumPad9 => 0x60 + (key - PhysicalKey.NumPad0),
        PhysicalKey.NumPadMultiply => 0x6A, PhysicalKey.NumPadAdd => 0x6B, PhysicalKey.NumPadSubtract => 0x6D,
        PhysicalKey.NumPadDecimal => 0x6E, PhysicalKey.NumPadDivide => 0x6F, PhysicalKey.NumPadEnter => 13,
        PhysicalKey.Backspace => 8, PhysicalKey.Tab => 9, PhysicalKey.Enter => 13, PhysicalKey.Pause => 19,
        PhysicalKey.CapsLock => 20, PhysicalKey.Escape => 27, PhysicalKey.Space => 0x20,
        PhysicalKey.PageUp => 0x21, PhysicalKey.PageDown => 0x22, PhysicalKey.End => 0x23, PhysicalKey.Home => 0x24,
        PhysicalKey.ArrowLeft => 0x25, PhysicalKey.ArrowUp => 0x26, PhysicalKey.ArrowRight => 0x27, PhysicalKey.ArrowDown => 0x28,
        PhysicalKey.PrintScreen => 0x2C, PhysicalKey.Insert => 0x2D, PhysicalKey.Delete => 0x2E,
        PhysicalKey.NumLock => 0x90, PhysicalKey.ScrollLock => 0x91,
        PhysicalKey.ShiftLeft => 0xA0, PhysicalKey.ShiftRight => 0xA1, PhysicalKey.ControlLeft => 0xA2, PhysicalKey.ControlRight => 0xA3,
        PhysicalKey.AltLeft => 0xA4, PhysicalKey.AltRight => 0xA5,
        PhysicalKey.Semicolon => 0xBA, PhysicalKey.Equal => 0xBB, PhysicalKey.Comma => 0xBC, PhysicalKey.Minus => 0xBD,
        PhysicalKey.Period => 0xBE, PhysicalKey.Slash => 0xBF, PhysicalKey.Backquote => 0xC0, PhysicalKey.BracketLeft => 0xDB,
        PhysicalKey.Backslash => 0xDC, PhysicalKey.BracketRight => 0xDD, PhysicalKey.Quote => 0xDE, PhysicalKey.IntlBackslash => 0xE2,
        _ => 0
    };
}
