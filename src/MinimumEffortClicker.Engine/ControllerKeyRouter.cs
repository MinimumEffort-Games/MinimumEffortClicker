using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

/// <summary>
/// Turns controller button presses into key and mouse-button presses for Controller to Key. A key that two binds share
/// stays down until both of their buttons are up. The platform sends what it returns; nothing here touches input.
/// </summary>
public sealed class ControllerKeyRouter
{
    private readonly object gate = new();
    private ControllerKeyBind[] binds = [];
    private readonly HashSet<ControllerButton> pressed = [];
    private readonly Dictionary<InputControl, int> holders = [];

    /// <summary>Uses these binds from now on (only the active ones press anything). Returns what must be let go first.</summary>
    public IReadOnlyList<InputControl> Configure(IEnumerable<ControllerKeyBind> active)
    {
        lock (gate)
        {
            var release = ReleaseAllLocked();
            binds = active.Where(b => b.Active && b.Valid).ToArray();
            return release;
        }
    }

    /// <summary>A controller button went down or up. Returns the outputs to press (true) or release (false), in order.</summary>
    public IReadOnlyList<(InputControl Output, bool Down)> Edge(ControllerButton button, bool down)
    {
        lock (gate)
        {
            if (down ? !pressed.Add(button) : !pressed.Remove(button)) return [];
            var edges = new List<(InputControl, bool)>();
            foreach (var bind in binds.Where(b => b.Button == button))
            {
                int count = holders.GetValueOrDefault(bind.Output) + (down ? 1 : -1);
                if (count <= 0) holders.Remove(bind.Output); else holders[bind.Output] = count;
                if (down && count == 1 || !down && count == 0) edges.Add((bind.Output, down));
            }
            return edges;
        }
    }

    /// <summary>Everything currently held, to release on Stop All, a locked screen or closing. The binds stay armed.</summary>
    public IReadOnlyList<InputControl> ReleaseAll() { lock (gate) return ReleaseAllLocked(); }

    private InputControl[] ReleaseAllLocked()
    {
        var held = holders.Keys.ToArray();
        holders.Clear(); pressed.Clear();
        return held;
    }
}
