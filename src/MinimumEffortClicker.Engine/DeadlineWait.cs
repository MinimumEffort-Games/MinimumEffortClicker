using System.Diagnostics;

namespace MinimumEffortClicker.Engine;

public interface IWaitStrategy
{
    /// <summary>Wait until a deadline on the injected clock, or throw when stopped.</summary>
    void WaitUntil(TimeSpan deadline, CancellationToken stop);
    bool WaitUntilOrChanged(TimeSpan deadline, CancellationToken stop, CancellationToken changed)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, changed);
        try { WaitUntil(deadline, linked.Token); return true; }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested && changed.IsCancellationRequested) { return false; }
    }
}

public sealed class StopwatchClock : IClock
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    public TimeSpan Elapsed => clock.Elapsed;
}

/// <summary>Blocking scheduler wait. No dispatcher timer, busy spin or per-event task.</summary>
public sealed class DeadlineWait(IClock clock) : IWaitStrategy
{
    public bool WaitUntilOrChanged(TimeSpan deadline, CancellationToken stop, CancellationToken changed)
    {
        var signals = new[] { stop.WaitHandle, changed.WaitHandle };
        while (true)
        {
            stop.ThrowIfCancellationRequested();
            if (changed.IsCancellationRequested) return false;
            var remaining = deadline - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) return true;
            int milliseconds = (int)Math.Min(int.MaxValue, Math.Ceiling(remaining.TotalMilliseconds));
            int result = WaitHandle.WaitAny(signals, milliseconds);
            if (result == 0) stop.ThrowIfCancellationRequested();
            if (result == 1) return false;
        }
    }
    public void WaitUntil(TimeSpan deadline, CancellationToken stop)
    {
        while (true)
        {
            stop.ThrowIfCancellationRequested();
            var remaining = deadline - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) return;
            // Round up rather than spinning for sub-millisecond remnants; cap WaitOne's Int32 timeout.
            int milliseconds = (int)Math.Min(int.MaxValue, Math.Ceiling(remaining.TotalMilliseconds));
            if (stop.WaitHandle.WaitOne(milliseconds)) stop.ThrowIfCancellationRequested();
        }
    }
}
