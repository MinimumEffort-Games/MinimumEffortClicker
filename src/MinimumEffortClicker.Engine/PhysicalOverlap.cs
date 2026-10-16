using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

/// <summary>What the clicker does with a press or release of a key or button the user is physically holding.</summary>
public enum OverlapAction
{
    /// <summary>The user is not holding it: send as usual.</summary>
    Send,
    /// <summary>Only Stop All: wait until the user lets go, then press. Stop All still stops the run while it waits.</summary>
    Wait,
    /// <summary>Only Stop All: the user took over a button the clicker held. Skip the clicker's release; the user's own
    /// release lets go of it, so nothing stays down and the run carries on.</summary>
    HandOver,
    /// <summary>Stop on my input: the user's input wins and the run ends, as it always did.</summary>
    Refuse,
}

/// <summary>
/// The rule for the user and the clicker using the same key or button at once, shared by the Windows and Linux runtimes.
/// With "Only Stop All" chosen, the user's own clicks never end a run; with "Stop on my input" they do.
/// </summary>
public static class PhysicalOverlap
{
    public static OverlapAction For(EmergencyStopMode mode, InputEdge edge, bool userHolds) =>
        !userHolds ? OverlapAction.Send
        : mode == EmergencyStopMode.AnyInput ? OverlapAction.Refuse
        : edge == InputEdge.Down ? OverlapAction.Wait : OverlapAction.HandOver;
}
