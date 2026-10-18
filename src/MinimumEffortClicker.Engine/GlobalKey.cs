using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

/// <summary>
/// A global key beside Start, Stop All and Record: Screenshot and Pause. It holds its key, its trigger and what it does,
/// so each platform's hotkey layer registers, refuses and dispatches these keys the same way instead of once per control.
/// </summary>
public sealed class GlobalKey(int id, Action fire)
{
    /// <summary>The hotkey id Windows registers it under; the Linux layer does not use it.</summary>
    public int Id { get; } = id;
    public int? Key { get; set; }
    public TriggerLatch Trigger { get; } = new();
    public void Fire() => fire();

    /// <summary>These controls take a keyboard key, not a mouse button, Esc or a modifier.</summary>
    public static bool Allowed(int key) => ShortcutKeys.Supported(key) && !ShortcutKeys.IsMouse(key) && key != 27 && key is not (>= 0xA0 and <= 0xA5);
}
