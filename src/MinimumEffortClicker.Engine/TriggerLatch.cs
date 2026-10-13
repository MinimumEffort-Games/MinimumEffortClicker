namespace MinimumEffortClicker.Engine;

public enum TriggerCommand { None, Toggle, Stop }

/// <summary>Control-thread state for either ordering of registered-hotkey and key-release delivery.</summary>
public sealed class TriggerLatch
{
    private bool down, pending, eligible;
    public void Reset(bool physicallyDown = false) { down = physicallyDown; pending = eligible = false; }
    public void CancelPending() => pending = eligible = false;
    public TriggerCommand KeyEdge(bool isDown, bool holdToRun)
    {
        if (isDown && !down) eligible = true;
        down = isDown;
        if (isDown) return TriggerCommand.None;
        if (holdToRun) return TriggerCommand.Stop;
        if (!pending) return TriggerCommand.None;
        pending = false; return TriggerCommand.Toggle;
    }
    public TriggerCommand RegisteredHotkey(bool holdToRun)
    {
        if (!eligible) return TriggerCommand.None;
        eligible = false;
        if (holdToRun) return down ? TriggerCommand.Toggle : TriggerCommand.None;
        if (!down) return TriggerCommand.Toggle;
        pending = true; return TriggerCommand.None;
    }
}
