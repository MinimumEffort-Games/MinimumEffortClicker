using System.Runtime.InteropServices;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Windows;

public sealed partial class DesktopRuntime
{
    // Opt-in, exclusive cursor-free click lists. No cursor movement, focus activation, keys, hooks or injection.
    private volatile bool cursorFreeActive;
    private bool CursorFreeActive => cursorFreeActive;
    private nint messageRecipient;
    private int messageRecipientPid;
    private nint messagePosition;
    private bool BackgroundTargetAvailable
    {
        get
        {
            var selected = target;
            return selected is not null && !targetInvalidated && ProbeNative.IsWindow(selected.Handle) && !ProbeNative.IsIconic(selected.Handle)
                && ProbeNative.GetWindowThreadProcessId(selected.Handle, out var pid) != 0 && pid == selected.ProcessId;
        }
    }
    private bool SendBackgroundClick(InputTransition transition)
    {
        if (transition.Control.Kind != InputKind.MouseButton || transition.Control.Code is < 0 or > 2) return false;
        bool down = transition.Edge == InputEdge.Down;
        uint message = (transition.Control.Code, down) switch
        {
            (0, true) => 0x201, (0, false) => 0x202, (1, true) => 0x204, (1, false) => 0x205,
            (2, true) => 0x207, _ => 0x208
        };
        if (down && transition.ClickCount == 2) message += 2; // WM_*BUTTONDBLCLK is the second down.
        if (down)
        {
            if (!BackgroundTargetAvailable || cancellation?.IsCancellationRequested != false || !Control.Available || transition.Position is not { } position) return false;
            var selected = target!;
            var frame = GetTargetFrame();
            if (!position.Matches(frame.Width, frame.Height, frame.Dpi) || position.Space != CoordinateSpace.WindowClient) return false;
            position = position.InFrame(frame.Width, frame.Height, frame.Dpi);
            var recipient = selected.Handle;
            var client = new ProbeNative.Point { X = position.X, Y = position.Y };
            for (int depth = 0; depth < 16; depth++)
            {
                var child = MessageNative.ChildWindowFromPointEx(recipient, client, 7);
                if (child == 0 || child == recipient) break;
                // Never follow an embedded HWND owned by an unrelated process.
                if (ProbeNative.GetWindowThreadProcessId(child, out var childPid) == 0 || childPid != selected.ProcessId) return false;
                if (!DesktopNative.ClientToScreen(recipient, ref client) || !MessageNative.ScreenToClient(child, ref client)) return false;
                recipient = child;
            }
            if (client.X is < 0 or > short.MaxValue || client.Y is < 0 or > short.MaxValue) return false;
            messageRecipient = recipient; messageRecipientPid = selected.ProcessId;
            messagePosition = (nint)((client.Y << 16) | client.X);
        }
        if (messageRecipient == 0) return !down;
        if (!ProbeNative.IsWindow(messageRecipient) || ProbeNative.GetWindowThreadProcessId(messageRecipient, out var pid) == 0 || pid != messageRecipientPid)
        { messageRecipient = 0; return !down; } // No physical button was held, and the receiving window no longer exists.
        nuint buttons = down ? transition.Control.Code switch { 0 => 1u, 1 => 2u, _ => 16u } : 0;
        bool posted = MessageNative.PostMessageW(messageRecipient, message, buttons, messagePosition);
        if (posted && !down) messageRecipient = 0;
        return posted;
    }
    private static class MessageNative
    {
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
        [DllImport("user32.dll")] internal static extern nint ChildWindowFromPointEx(nint parent, ProbeNative.Point point, uint flags);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint window, ref ProbeNative.Point point);
    }
}
