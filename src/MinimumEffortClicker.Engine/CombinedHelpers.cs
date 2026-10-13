using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

public sealed partial class ClickExecutor
{
    private sealed class HelperSlot(NamedHelper helper, TimeSpan origin)
    {
        public readonly NamedHelper Helper = helper;
        public readonly TimeSpan Origin = origin;
        public readonly TimeSpan End = helper.Plan.ActiveDuration is { } duration ? origin + duration : TimeSpan.MaxValue;
        public TimeSpan Due = origin;
        public TimeSpan NextStart = origin;
        public bool Releasing, Finished;
        public int Completed;
    }

    /// <summary>One worker, dispatch gate and ownership ledger for every compatible helper.</summary>
    public ClickRunResult Execute(CompatibleHelpers plan, Guid targetId, CancellationToken stop, HelperSetControl? control = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (control is not null && !ReferenceEquals(control.Plan, plan)) throw new ArgumentException("The stop controls belong to another helper set.");
        return ExecuteSession(targetId, stop, run =>
        {
            var origin = clock.Elapsed;
            var slots = plan.Helpers.Select(h => new HelperSlot(h, origin)).ToList();
            while (true)
            {
                if (run.Outcome is not null) return;
                foreach (var finished in slots.Where(s => s.Finished)) control?.Completed(finished.Helper.Id);
                slots.RemoveAll(s => s.Finished);
                var changes = control?.ReadChanges();
                if (changes is { } update)
                {
                    slots.AddRange(update.Joined.Select(h => new HelperSlot(h, clock.Elapsed)));
                    lock (gate)
                    {
                        foreach (var removed in slots.Where(s => !s.Finished && update.Stopped.Contains(s.Helper.Id)))
                        {
                            if (!Release(run, removed.Helper.Plan.Control)) { run.Outcome = ClickRunOutcome.InputRejected; return; }
                            removed.Finished = true;
                        }
                    }
                    // Release the member's reservation before waiting on another indefinite hold.
                    // Otherwise its next explicit toggle cannot rejoin until some other member wakes us.
                    foreach (var finished in slots.Where(s => s.Finished)) control?.Completed(finished.Helper.Id);
                    slots.RemoveAll(s => s.Finished);
                }
                HelperSlot? slot = null;
                foreach (var candidate in slots)
                {
                    if (!candidate.Releasing && candidate.Helper.Plan.Control.Kind == InputKind.MouseButton && lastClick is { } last)
                        candidate.Due = TimeSpan.FromTicks(Math.Min(candidate.End.Ticks, Math.Max(candidate.Due.Ticks, (last + clickRateLimit.MinimumGap).Ticks)));
                    if (!candidate.Finished && (slot is null || candidate.Due < slot.Due
                        || candidate.Due == slot.Due && candidate.Releasing && !slot.Releasing)) slot = candidate;
                }
                if (slot is null)
                {
                    foreach (var finished in slots.Where(s => s.Finished)) control?.Completed(finished.Helper.Id);
                    slots.RemoveAll(s => s.Finished);
                    if (control is null || control.TryCloseIfEmpty()) return;
                    continue;
                }
                var helper = slot.Helper.Plan;
                lock (gate) Publish(run, next: (slot.Releasing ? "Release: " : "Start: ") + slot.Helper.Name);
                if (changes is { } signal)
                {
                    if (!WaitDeadline(slot.Due, stop, signal.Changed)) continue;
                }
                else WaitDeadline(slot.Due, stop);
                lock (gate)
                {
                    if (changes is { } latest && latest.Changed.IsCancellationRequested) continue;
                    if (!CanContinue(run, targetId, stop)) return;
                    if (slot.Releasing)
                    {
                        if (!Release(run, helper.Control)) { run.Outcome = ClickRunOutcome.InputRejected; return; }
                        CompleteCycle(slot, run);
                    }
                    else if (clock.Elapsed >= slot.End) slot.Finished = true;
                    else
                    {
                        if (!sink.TrySend(new(helper.Control, InputEdge.Down, helper.Position))) { run.Outcome = ClickRunOutcome.InputRejected; return; }
                        AcceptedClick(helper.Control);
                        run.Sent++;
                        if (helper.Mode == HelperMode.Scroll) CompleteCycle(slot, run);
                        else
                        {
                            owned.Add(helper.Control); slot.Releasing = true;
                            slot.Due = helper.Mode == HelperMode.Hold ? slot.End : clock.Elapsed + SampleDuration(helper.PressDuration, helper.MaximumPressDuration);
                            if (slot.Due > slot.End) slot.Due = slot.End;
                        }
                    }
                    Publish(run, slot.Helper.Name);
                }
            }
        });
    }

    private void CompleteCycle(HelperSlot slot, Run run)
    {
        var helper = slot.Helper.Plan;
        slot.Completed++; CompleteLoop(run); slot.Releasing = false;
        if (helper.Mode == HelperMode.Hold || helper.Count > 0 && slot.Completed >= helper.Count)
        { slot.Finished = true; return; }
        // Skip every missed repeat slot rather than issuing a catch-up burst.
        var interval = SampleDuration(helper.Interval, helper.MaximumInterval);
        slot.NextStart += interval;
        if (slot.NextStart < clock.Elapsed)
        {
            if (helper.MaximumInterval is not null) slot.NextStart = clock.Elapsed + interval;
            else
            {
                long nextSlot = (clock.Elapsed - slot.Origin).Ticks / helper.Interval.Ticks + 1;
                slot.NextStart = slot.Origin + TimeSpan.FromTicks(checked(nextSlot * helper.Interval.Ticks));
            }
        }
        slot.Due = slot.NextStart;
        if (slot.Due >= slot.End) slot.Finished = true;
    }
}
