namespace MinimumEffortClicker.Engine;

public enum ShutdownTrigger { CompletedLoops, ActiveRunTime, SystemTime, CompletedClicks }
public sealed record ShutdownRequest(ShutdownTrigger Trigger, long Loops = 0, TimeSpan ActiveTime = default, DateTimeOffset At = default, long Clicks = 0)
{
    public void Validate(DateTimeOffset now)
    {
        if (!Enum.IsDefined(Trigger) || Trigger == ShutdownTrigger.CompletedLoops && (Loops < 1 || Loops > 1_000_000)
            || Trigger == ShutdownTrigger.CompletedClicks && (Clicks < 1 || Clicks > 1_000_000)
            || Trigger == ShutdownTrigger.ActiveRunTime && (ActiveTime < TimeSpan.FromSeconds(1) || ActiveTime > TimeSpan.FromDays(7))
            || Trigger == ShutdownTrigger.SystemTime && (At <= now || At > now.AddDays(7)))
            throw new ArgumentException("Choose 1–1,000,000 clicks/loops, 1 second–7 days of active run time, or a future time within 7 days.");
    }
}
public sealed record ShutdownStatus(bool Armed, bool CountingDown, TimeSpan Remaining, long LoopsRemaining, string Message);

/// <summary>In-memory, single-use plan. The caller supplies monotonic playback totals; idle time cannot count.</summary>
public sealed class ShutdownSchedule
{
    private readonly object gate = new();
    private ShutdownRequest? request;
    private long initialLoops;
    private long initialClicks;
    private TimeSpan initialActive, countdownAt;
    private bool countdown;
    private bool countdownStarted;
    private string message = "Automatic shutdown is off.";
    public bool Armed { get { lock (gate) return request is not null; } }
    public void Arm(ShutdownRequest next, DateTimeOffset now, long loops, TimeSpan active, long clicks = 0)
    {
        next.Validate(now);
        lock (gate) { request = next; initialLoops = loops; initialClicks = clicks; initialActive = active; countdown = countdownStarted = false; message = "Shutdown armed for this session."; }
    }
    public void Cancel(string reason = "Automatic shutdown cancelled.")
    { lock (gate) { request = null; countdown = false; message = reason; } }

    public ShutdownStatus Update(DateTimeOffset now, TimeSpan monotonic, long loops, TimeSpan active, long clicks = 0)
    {
        lock (gate)
        {
            if (request is not { } plan) return new(false, false, TimeSpan.Zero, 0, message);
            long remainingLoops = Math.Max(0, plan.Loops - Math.Max(0, loops - initialLoops));
            if (plan.Trigger == ShutdownTrigger.CompletedClicks) remainingLoops = Math.Max(0, plan.Clicks - Math.Max(0, clicks - initialClicks));
            var remaining = plan.Trigger == ShutdownTrigger.ActiveRunTime ? plan.ActiveTime - (active - initialActive) : plan.At - now;
            bool reached = plan.Trigger is ShutdownTrigger.CompletedLoops or ShutdownTrigger.CompletedClicks ? remainingLoops == 0 : remaining <= TimeSpan.Zero;
            if (reached && !countdown) { countdown = true; countdownAt = monotonic; }
            if (countdown)
            {
                remaining = TimeSpan.FromSeconds(30) - (countdownStarted ? monotonic - countdownAt : TimeSpan.Zero);
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                return new(true, true, remaining, 0, $"PC shuts down in {Math.Ceiling(remaining.TotalSeconds):0}s. Cancel or Stop All to keep working.");
            }
            message = plan.Trigger switch
            {
                ShutdownTrigger.CompletedLoops => $"Shutdown after {remainingLoops:N0} more completed loops.",
                ShutdownTrigger.CompletedClicks => $"Shutdown after {remainingLoops:N0} more clicks.",
                ShutdownTrigger.ActiveRunTime => $"Shutdown after {remaining.TotalSeconds:0.0}s more active run time. Idle time does not count.",
                _ => $"Shutdown at {plan.At.LocalDateTime:g}."
            };
            return new(true, false, remaining, remainingLoops, message);
        }
    }

    public void BeginCountdown(TimeSpan monotonic)
    { lock (gate) { if (request is not null && countdown && !countdownStarted) { countdownAt = monotonic; countdownStarted = true; } } }

    // The commit and cancellation share a tiny gate. No OS shutdown request can follow a completed cancellation.
    public bool TryCommit(TimeSpan monotonic, bool cleanupFinished, Action shutdown)
    {
        lock (gate)
        {
            if (request is null || !countdown || !countdownStarted || !cleanupFinished || monotonic - countdownAt < TimeSpan.FromSeconds(30)) return false;
            request = null; countdown = false; message = "Shutdown requested from Windows.";
            shutdown();
            return true;
        }
    }
}
