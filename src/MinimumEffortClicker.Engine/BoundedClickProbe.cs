using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

/// <summary>M0 feasibility harness, not the production macro scheduler.</summary>
public sealed class BoundedClickProbe(IInputSink sink) : IDisposable
{
    private readonly object gate = new();
    private readonly ManualResetEventSlim stopped = new(false);
    private bool ownsDown;
    public bool IsStopped => stopped.IsSet;
    public bool HasOwnedInput { get { lock (gate) return ownsDown; } }
    public int InputsSent { get; private set; }

    public bool Press(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(duration));
        lock (gate)
        {
            if (stopped.IsSet || ownsDown) return false;
            if (!sink.TrySend(new(PointerButton.Left, InputEdge.Down))) { stopped.Set(); return false; }
            ownsDown = true;
            InputsSent++;
        }
        stopped.Wait(duration);
        lock (gate)
        {
            if (ownsDown && !Release()) { stopped.Set(); return false; }
            return !stopped.IsSet;
        }
    }

    public void Stop()
    {
        // Signal first, so the active wait wakes before contending on the dispatch gate.
        stopped.Set();
        lock (gate) Release();
    }

    private bool Release()
    {
        if (!ownsDown) return true;
        if (!sink.TrySend(new(PointerButton.Left, InputEdge.Up))) return false;
        ownsDown = false;
        InputsSent++;
        return true;
    }

    // A caller must join its worker before disposing. Failed cleanup remains visible to the caller.
    public void Dispose() { Stop(); stopped.Dispose(); }
}
