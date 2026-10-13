namespace MinimumEffortClicker.Engine;

/// <summary>Coalesces release notifications without losing one that arrives during cleanup.</summary>
public sealed class DeferredCleanupRetry(Action<Action> schedule, Action retry)
{
    private readonly object gate = new();
    private bool queued, requested;

    public void Request()
    {
        lock (gate)
        {
            requested = true;
            if (queued) return;
            queued = true;
        }
        schedule(Drain);
    }

    private void Drain()
    {
        while (true)
        {
            lock (gate)
            {
                if (!requested) { queued = false; return; }
                requested = false;
            }
            retry();
        }
    }
}
