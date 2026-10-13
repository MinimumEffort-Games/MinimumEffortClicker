namespace MinimumEffortClicker.Engine;

/// <summary>
/// One run's pause/resume route. No input or UI. A pause is either the user's (stays paused until they resume) or
/// automatic (the target left the front; the runtime resumes it once the target is back).
/// </summary>
public sealed class PlaybackControl : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource changed = new();
    private readonly ManualResetEventSlim resumed = new(false);
    private int state; // running, requested, acknowledged, resume requested, ended
    private bool automatic;
    public bool IsPaused { get { lock (gate) return state is >= 1 and <= 3; } }
    public bool CanResume { get { lock (gate) return state == 2; } }
    /// <summary>True while the current pause was caused by the target leaving the front, not by the user.</summary>
    public bool Automatic { get { lock (gate) return automatic && state is >= 1 and <= 3; } }
    internal CancellationToken Changed { get { lock (gate) return changed.Token; } }
    public bool RequestPause(bool automatic = false)
    {
        lock (gate)
        {
            if (state != 0) return false;
            state = 1; this.automatic = automatic; changed.Cancel(); return true;
        }
    }
    /// <summary>The user paused while an automatic pause was in effect: keep it paused until they resume.</summary>
    public bool MakeManual()
    {
        lock (gate) { if (state is < 1 or > 2 || !automatic) return false; automatic = false; return true; }
    }
    public bool RequestResume()
    {
        lock (gate) { if (state != 2) return false; state = 3; resumed.Set(); return true; }
    }
    internal void Acknowledge() { lock (gate) { if (state == 1) state = 2; } }
    internal void WaitForResume(CancellationToken stop) => resumed.Wait(stop);
    /// <summary>A resume arrived but the target left the front again before input was re-sent: wait again.</summary>
    internal void Repause() { lock (gate) { if (state != 3) return; state = 2; automatic = true; resumed.Reset(); } }
    internal void CompleteResume()
    {
        // Called only by the scheduler, after its previous wait has returned.
        lock (gate) { changed.Dispose(); changed = new(); resumed.Reset(); state = 0; automatic = false; }
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
