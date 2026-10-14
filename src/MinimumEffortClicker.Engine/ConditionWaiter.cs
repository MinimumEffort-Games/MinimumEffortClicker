using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

public enum ConditionObservationState { NotMet, Met, Ambiguous, Unavailable }
public readonly record struct ConditionObservation(ConditionObservationState State, long ContextGeneration, long SampleId);
public interface IConditionProbe
{
    ConditionObservation Observe(CancellationToken stop);
}
/// <summary>Stopped: the run's own time budget ran out while waiting. A wait never gives up by itself.</summary>
public enum ConditionOutcome { Matched, Stopped, Ambiguous, Unavailable }
public sealed record ConditionResult(ConditionOutcome Outcome, int Samples);

/// <summary>
/// Observation only. It has no input sink and cannot turn a match into a click. It keeps looking until a match,
/// an unreadable or ambiguous picture, or cancellation (Stop All, a pause, or the run's time budget).
/// </summary>
public sealed class ConditionWaiter(IClock clock, IWaitStrategy waits)
{
    public ConditionResult Wait(ConditionPolicy policy, IConditionProbe probe, CancellationToken stop,
        Action<ConditionObservation>? observed = null, Action? serviceInputs = null)
    {
        policy.Validate(); ArgumentNullException.ThrowIfNull(probe);
        int samples = 0;
        long? generation = null; long lastSample = -1; int stable = 0;
        while (true)
        {
            stop.ThrowIfCancellationRequested();
            serviceInputs?.Invoke();
            var observation = probe.Observe(stop); samples++;
            stop.ThrowIfCancellationRequested();
            observed?.Invoke(observation);
            if (observation.State == ConditionObservationState.Unavailable) return new(ConditionOutcome.Unavailable, samples);
            if (observation.State == ConditionObservationState.Ambiguous) return new(ConditionOutcome.Ambiguous, samples);
            if (!Enum.IsDefined(observation.State)) throw new InvalidOperationException("Unknown observation state.");
            if (observation.SampleId < 0) throw new InvalidOperationException("Observation needs a nonnegative sample identity.");
            if (generation != observation.ContextGeneration) { generation = observation.ContextGeneration; stable = 0; lastSample = -1; }
            if (observation.State == ConditionObservationState.NotMet) stable = 0;
            else if (observation.SampleId > lastSample) stable++;
            lastSample = Math.Max(lastSample, observation.SampleId);
            if (stable >= policy.StableSamples) return new(ConditionOutcome.Matched, samples);
            var due = clock.Elapsed + policy.SampleInterval;
            if (serviceInputs is null) waits.WaitUntil(due, stop);
            else while (clock.Elapsed < due)
            {
                serviceInputs(); stop.ThrowIfCancellationRequested();
                var tick = clock.Elapsed + TimeSpan.FromMilliseconds(50);
                waits.WaitUntil(tick < due ? tick : due, stop);
            }
        }
    }
}
