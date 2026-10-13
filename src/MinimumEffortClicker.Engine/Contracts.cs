using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

/// <summary>Elapsed monotonic time from an arbitrary origin; never wall-clock time.</summary>
public interface IClock
{
    TimeSpan Elapsed { get; }
}

/// <summary>Ephemeral instance identity, not a portable profile identifier or native handle.</summary>
public sealed record ForegroundTarget(Guid InstanceId, bool IsAvailable);

public interface IForegroundTargetProvider
{
    ForegroundTarget? Current { get; }
}

public enum InputEdge { Down, Up }
public readonly record struct InputTransition(InputControl Control, InputEdge Edge, ClientPoint? Position = null, int ClickCount = 1)
{
    public InputTransition(PointerButton button, InputEdge edge) : this(InputControl.Mouse(button), edge) { }
    public PointerButton Button => (PointerButton)Control.Code;
}

/// <summary>
/// Reports whether this single transition was accepted. False means it was not accepted;
/// adapters must reconcile native partial results here rather than reporting an ambiguous success.
/// Default tests supply memory-only fakes; the separate M0 probe supplies its narrow native adapter.
/// </summary>
public interface IInputSink
{
    bool TrySend(InputTransition transition);
}
