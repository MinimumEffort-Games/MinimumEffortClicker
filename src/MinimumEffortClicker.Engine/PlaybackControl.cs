namespace MinimumEffortClicker.Engine;

/// <summary>One run's explicit pause/resume route. No input, UI, or automatic resume.</summary>
public sealed class PlaybackControl : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource changed = new();
    private readonly ManualResetEventSlim resumed = new(false);
    private int state; // running, requested, acknowledged, resume requested, ended
    public bool IsPaused { get { lock (gate) return state is >= 1 and <= 3; } }
    public bool CanResume { get { lock (gate) return state == 2; } }
    internal CancellationToken Changed { get { lock (gate) return changed.Token; } }
    public bool RequestPause()
    {
        lock (gate)
        {
            if (state != 0) return false;
            state = 1; changed.Cancel(); return true;
        }
    }
    public bool RequestResume()
    {
        lock (gate) { if (state != 2) return false; state = 3; resumed.Set(); return true; }
    }
    internal void Acknowledge() { lock (gate) { if (state == 1) state = 2; } }
    internal void WaitForResume(CancellationToken stop) => resumed.Wait(stop);
    internal void CompleteResume()
    {
        // Called only by the scheduler, after its previous wait has returned.
        lock (gate) { changed.Dispose(); changed = new(); resumed.Reset(); state = 0; }
    }
    internal void End() { lock (gate) state = 4; }
    public void Dispose() { lock (gate) { state = 4; changed.Dispose(); resumed.Dispose(); } }
}

internal sealed class PlaybackClock(IClock source) : IClock
{
    private readonly object gate = new();
    private TimeSpan excluded;
    private TimeSpan? pausedAt;
    public TimeSpan Elapsed { get { lock (gate) return (pausedAt ?? source.Elapsed) - excluded; } }
    public void Pause() { lock (gate) pausedAt ??= source.Elapsed; }
    public void Resume()
    {
        lock (gate) { if (pausedAt is { } start) excluded += source.Elapsed - start; pausedAt = null; }
    }
}
