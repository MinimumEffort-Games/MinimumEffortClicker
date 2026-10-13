using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

/// <summary>Bounded stop requests for one reviewed helper set. It never dispatches input.</summary>
public sealed class HelperSetControl(CompatibleHelpers plan) : IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<Guid> stopped = [];
    private readonly Dictionary<Guid, NamedHelper> members = plan.Helpers.ToDictionary(h => h.Id);
    private readonly Queue<NamedHelper> joined = new();
    private CancellationTokenSource changed = new();
    private bool disposed;
    private bool accepting = true;
    public CompatibleHelpers Plan { get; } = plan;
    public bool RequestStop(Guid id)
    {
        lock (gate)
        {
            if (disposed || !members.ContainsKey(id) || !stopped.Add(id)) return false;
            changed.Cancel(); return true;
        }
    }
    // Called only by the scheduler after its previous wait has returned.
    public bool RequestJoin(NamedHelper helper)
    {
        lock (gate)
        {
            if (disposed || !accepting) return false;
            // Stopping members retain resources until the scheduler confirms their release.
            CompatibleHelpers.Compile(members.Values.Append(helper));
            members.Add(helper.Id, helper); stopped.Remove(helper.Id); joined.Enqueue(helper);
            changed.Cancel(); return true;
        }
    }
    internal void Completed(Guid id) { lock (gate) { members.Remove(id); stopped.Remove(id); } }
    public InputControl[] ActiveControls { get { lock (gate) return members.Values.Select(h => h.Plan.Control).ToArray(); } }
    public Guid[] ActiveIds { get { lock (gate) return members.Keys.ToArray(); } }
    internal bool TryCloseIfEmpty()
    {
        lock (gate) { if (members.Count != 0) return false; accepting = false; return true; }
    }
    internal (Guid[] Stopped, NamedHelper[] Joined, CancellationToken Changed) ReadChanges()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (changed.IsCancellationRequested) { changed.Dispose(); changed = new(); }
            var additions = joined.ToArray(); joined.Clear();
            return (stopped.ToArray(), additions, changed.Token);
        }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; accepting = false; members.Clear(); stopped.Clear(); joined.Clear(); changed.Dispose(); }
    }
}
