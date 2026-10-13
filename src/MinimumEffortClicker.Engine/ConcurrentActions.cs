using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

public sealed partial class ClickExecutor
{
    private ActionToggleSession? actionSession;
    private readonly Dictionary<Guid, InputControl> sessionHeld = [];
    private sealed class RepeatSlot(NamedHelper helper, TimeSpan due)
    {
        public NamedHelper Helper = helper;
        public TimeSpan Due = due, Started;
        public bool Down;
    }
    private readonly Dictionary<Guid, RepeatSlot> sessionRepeats = [];
    private TimeSpan nextSpamPress;
    // One scheduler; at most 100 aggregate spam presses/sec, never a catch-up burst.
    private TimeSpan NextConcurrentDue => sessionRepeats.Count == 0 ? TimeSpan.MaxValue
        : sessionRepeats.Values.Min(s => s.Down || s.Due >= nextSpamPress ? s.Due : nextSpamPress);
    public Action<SessionAction, bool>? SessionActionChanged { get; set; }
    public Action? SessionHoldsChanged { get; set; }
    public bool HasConcurrentSession => actionSession is not null;
    // Share deadlines with conditions: a long scan interval must not starve Spam.
    public void WaitWithConcurrentInputs(TimeSpan delay, CancellationToken stop)
        => WaitDeadline(clock.Elapsed + (delay > TimeSpan.Zero ? delay : TimeSpan.Zero), stop);

    // Scheduler-thread callback used by the observation adapter during long condition waits.
    public void ServiceConcurrentInputs(CancellationToken stop)
    {
        var session = actionSession; var run = activeRun;
        if (session is null || run is null) return;
        var changes = session.Read(); bool updated = false, membershipChanged = false;
        lock (gate)
        {
            if (!CanContinue(run, activeTarget, stop)) { stop.ThrowIfCancellationRequested(); throw new InterruptedPlaybackException(); }
            foreach (var id in changes.Stopping)
            {
                if (sessionHeld.TryGetValue(id, out var input) && !Release(run, input)) throw new InvalidOperationException("Toggle release was rejected.");
                sessionHeld.Remove(id); sessionRepeats.Remove(id); session.Released(id); updated = membershipChanged = true;
            }
            foreach (var helper in changes.Holds.Where(h => !changes.Stopping.Contains(h.Id) && !sessionHeld.ContainsKey(h.Id) && !sessionRepeats.ContainsKey(h.Id)))
            {
                if (helper.Plan.Mode == HelperMode.Repeat) { sessionRepeats.Add(helper.Id, new(helper, clock.Elapsed)); updated = membershipChanged = true; continue; }
                stop.ThrowIfCancellationRequested();
                if (!TargetMatches(activeTarget) || owned.Contains(helper.Plan.Control) || !sink.TrySend(new(helper.Plan.Control, InputEdge.Down)))
                    throw new InvalidOperationException("Toggle input was rejected; all input is being released.");
                owned.Add(helper.Plan.Control); sessionHeld.Add(helper.Id, helper.Plan.Control); run.Sent++; updated = membershipChanged = true;
                AcceptedClick(helper.Plan.Control);
            }
            foreach (var slot in sessionRepeats.Values.OrderBy(s => s.Due))
            {
                if (slot.Due > clock.Elapsed) continue;
                var input = slot.Helper.Plan.Control;
                if (slot.Down)
                {
                    if (!Release(run, input)) throw new InvalidOperationException("Spam release was rejected.");
                    sessionHeld.Remove(slot.Helper.Id); slot.Down = false;
                    slot.Due = slot.Started + slot.Helper.Plan.Interval;
                    if (slot.Due <= clock.Elapsed) slot.Due = clock.Elapsed + slot.Helper.Plan.Interval;
                }
                else
                {
                    if (clock.Elapsed < nextSpamPress) continue;
                    stop.ThrowIfCancellationRequested();
                    if (!TargetMatches(activeTarget) || owned.Contains(input) || !sink.TrySend(new(input, InputEdge.Down)))
                        throw new InvalidOperationException("Spam input was rejected; all input is being released.");
                    owned.Add(input); sessionHeld.Add(slot.Helper.Id, input); run.Sent++; AcceptedClick(input);
                    slot.Down = true; slot.Started = clock.Elapsed; slot.Due = clock.Elapsed + slot.Helper.Plan.PressDuration;
                    nextSpamPress = clock.Elapsed + TimeSpan.FromMilliseconds(10);
                }
                updated = true;
            }
            if (updated) Publish(run);
        }
        // The UI timer samples progress; do not enqueue a refresh for every spam edge.
        if (membershipChanged) SessionHoldsChanged?.Invoke();
    }
    public ClickRunResult Execute(ActionToggleSession session, Guid targetId, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(session);
        actionSession = session; nextSpamPress = clock.Elapsed;
        try
        {
            return ExecuteSession(targetId, stop, run =>
            {
                while (true)
                {
                    PauseCheckpoint(stop); ServiceConcurrentInputs(stop);
                    var action = session.TakeAction();
                    if (action is not null)
                    {
                        using var actionStop = CancellationTokenSource.CreateLinkedTokenSource(stop, action.Cancellation.Token);
                        using var releaseAction = actionStop.Token.Register(() =>
                        {
                            lock (gate)
                                foreach (var input in owned.Where(i => !sessionHeld.Values.Contains(i)).Reverse().ToArray())
                                    if (!Release(run, input)) run.Outcome = ClickRunOutcome.CleanupFailed;
                        });
                        try
                        {
                            SessionActionChanged?.Invoke(action, true);
                            WaitDeadline(clock.Elapsed + action.Countdown, actionStop.Token);
                            actionStop.Token.ThrowIfCancellationRequested();
                            ExecuteSequenceLoops(action.Plan, targetId, actionStop.Token, run);
                        }
                        catch (OperationCanceledException) when (action.Cancellation.IsCancellationRequested && !stop.IsCancellationRequested)
                        { lock (gate) if (run.Outcome == ClickRunOutcome.Cancelled) run.Outcome = null; }
                        finally
                        {
                            // Wait for the action-only release callback before relinquishing its reservation.
                            releaseAction.Dispose();
                            lock (gate)
                            {
                                foreach (var input in owned.Where(i => !sessionHeld.Values.Contains(i)).Reverse().ToArray())
                                    if (!Release(run, input)) run.Outcome = ClickRunOutcome.CleanupFailed;
                                finishSequence?.Dispose(); finishSequence = null; Interlocked.Exchange(ref sequenceDeadline, long.MaxValue);
                            }
                            session.CompletedAction(); SessionActionChanged?.Invoke(action, false);
                        }
                        if (run.Outcome is not null) return;
                    }
                    if (session.CloseIfEmpty()) { if (session.LastActionCancelled) run.Outcome = ClickRunOutcome.Cancelled; return; }
                    WaitDeadline(TimeSpan.MaxValue, stop, session.Changed);
                    lock (gate) { if (!CanContinue(run, targetId, stop)) return; }
                }
            });
        }
        finally { actionSession = null; sessionHeld.Clear(); sessionRepeats.Clear(); }
    }
}
