namespace MinimumEffortClicker.Core;

/// <summary>A bounded immutable repeat plan. Counts represent complete down/up cycles, not timer slots.</summary>
public sealed class ClickPlan
{
    public const int MaximumCount = 1_000_000;
    public PointerButton Button { get; }
    public TimeSpan Interval { get; }
    public TimeSpan PressDuration { get; }
    public int Count { get; }

    private ClickPlan(ClickDefinition definition, int count)
    {
        Button = definition.Button;
        Interval = definition.Interval;
        PressDuration = definition.PressDuration;
        Count = count;
    }

    public static ClickPlan Compile(ClickDefinition definition, int count)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = definition.Validate();
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(definition));
        if (count is < 1 or > MaximumCount) throw new ArgumentOutOfRangeException(nameof(count));
        return new ClickPlan(definition, count);
    }
}
