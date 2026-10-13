using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

public sealed record PreviewStep(TimeSpan Offset, InputTransition Transition);
public sealed record ClickPreview(TimeSpan CreatedAt, Guid TargetInstanceId, IReadOnlyList<PreviewStep> Steps);

/// <summary>
/// An immutable description of one click. Cannot send input, wait, register hotkeys or run loops.
/// A successful rehearsal is not authorization for future live dispatch.
/// </summary>
public sealed class ClickRehearsal(IClock clock, IForegroundTargetProvider targets)
{
    public ClickPreview Describe(ClickDefinition definition, Guid expectedTarget)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = definition.Validate();
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(definition));

        var target = targets.Current;
        if (expectedTarget == Guid.Empty || target is null || !target.IsAvailable || target.InstanceId != expectedTarget)
            throw new InvalidOperationException("The selected target is unavailable or is no longer in the foreground.");

        return new ClickPreview(clock.Elapsed, target.InstanceId, Array.AsReadOnly(new[]
        {
            new PreviewStep(TimeSpan.Zero, new InputTransition(definition.Button, InputEdge.Down)),
            new PreviewStep(definition.PressDuration, new InputTransition(definition.Button, InputEdge.Up))
        }));
    }
}
