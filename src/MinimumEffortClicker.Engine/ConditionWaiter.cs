using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

public enum ConditionObservationState { NotMet, Met, Ambiguous, Unavailable }
public readonly record struct ConditionObservation(ConditionObservationState State, long ContextGeneration, long SampleId);
public interface IConditionProbe
{
    ConditionObservation Observe(CancellationToken stop);
}
public enum ConditionOutcome { Matched, Stopped, Paused, Ambiguous, Unavailable }
public sealed record ConditionResult(ConditionOutcome Outcome, int Samples, int Attempts);

/// <summary>Observation only. It has no input sink and cannot turn a match into a click.</summary>
public sealed class ConditionWaiter(IClock clock, IWaitStrategy waits)
{
    public ConditionResult Wait(ConditionPolicy policy, IConditionProbe probe, CancellationToken stop,
        bool untilCancelled = false, Action<ConditionObservation>? observed = null, Action? serviceInputs = null)
    {
        policy.Validate(); ArgumentNullException.ThrowIfNull(probe);
        int samples = 0;
        for (int attempt = 1; ; attempt++)
        {
            var until = clock.Elapsed + policy.Timeout;
            long? generation = null; long lastSample = -1; int stable = 0;
            while (untilCancelled || clock.Elapsed < until)
            {
                stop.ThrowIfCancellationRequested();
                serviceInputs?.Invoke();
                var observation = probe.Observe(stop); samples++;
                stop.ThrowIfCancellationRequested();
                if (!untilCancelled && clock.Elapsed >= until) break; // A slow capture cannot win after the deadline.
                observed?.Invoke(observation);
                if (observation.State == ConditionObservationState.Unavailable) return new(ConditionOutcome.Unavailable, samples, attempt);
                if (observation.State == ConditionObservationState.Ambiguous) return new(ConditionOutcome.Ambiguous, samples, attempt);
                if (!Enum.IsDefined(observation.State)) throw new InvalidOperationException("Unknown observation state.");
                if (observation.SampleId < 0) throw new InvalidOperationException("Observation needs a nonnegative sample identity.");
                if (generation != observation.ContextGeneration) { generation = observation.ContextGeneration; stable = 0; lastSample = -1; }
                if (observation.State == ConditionObservationState.NotMet) stable = 0;
                else if (observation.SampleId > lastSample) stable++;
                lastSample = Math.Max(lastSample, observation.SampleId);
                if (stable >= policy.StableSamples) return new(ConditionOutcome.Matched, samples, attempt);
                var next = clock.Elapsed + policy.SampleInterval;
                var due = untilCancelled || next < until ? next : until;
                if (serviceInputs is null) waits.WaitUntil(due, stop);
                else while (clock.Elapsed < due)
                {
                    serviceInputs(); stop.ThrowIfCancellationRequested();
                    var tick = clock.Elapsed + TimeSpan.FromMilliseconds(50);
                    waits.WaitUntil(tick < due ? tick : due, stop);
                }
            }
            if (policy.TimeoutAction == ConditionTimeoutAction.Retry && attempt <= policy.Retries) continue;
            return new(policy.TimeoutAction == ConditionTimeoutAction.Pause ? ConditionOutcome.Paused : ConditionOutcome.Stopped, samples, attempt);
        }
    }
}
