namespace MinimumEffortClicker.Core;

public sealed record TimelineEntry(int StepIndex, string Name, InputControl? Control, TimeSpan Start,
    TimeSpan Duration, ConditionDefinition? Condition = null, CompiledFlow? Flow = null);

/// <summary>Read-only nominal timing; observed conditions and runtime pacing may extend it.</summary>
public static class SequenceTimeline
{
    public static IReadOnlyList<TimelineEntry> Create(SequencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = new List<TimelineEntry>();
        var held = new Dictionary<Guid, ScheduledInput>();
        foreach (var input in plan.Inputs)
        {
            if (input.Control.Kind is InputKind.Scroll or InputKind.HorizontalScroll or InputKind.PointerMove)
                result.Add(new(input.StepIndex, input.Name, input.Control, input.Offset, TimeSpan.Zero));
            else if (input.Down) held.Add(input.ActionId, input);
            else
            {
                var start = held[input.ActionId]; held.Remove(input.ActionId);
                result.Add(new(start.StepIndex, start.Name, start.Control, start.Offset, input.Offset - start.Offset));
            }
        }
        result.AddRange(plan.Conditions.Select(c => new TimelineEntry(c.StepIndex, c.Name, null, c.Offset, TimeSpan.Zero, c.Definition, c.Flow)));
        result.AddRange(plan.Screenshots.Select(s => new TimelineEntry(s.StepIndex, s.Name, null, s.Offset, TimeSpan.Zero)));
        return Array.AsReadOnly(result.OrderBy(r => r.Start).ThenBy(r => r.StepIndex).Select(r => r with { StepIndex = plan.SourceStep(r.StepIndex) }).ToArray());
    }
}
