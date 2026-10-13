namespace MinimumEffortClicker.Core;

/// <summary>Optional ranges on an ordinary action. Interval preserves older helper start-to-start timing.</summary>
public sealed record ActionTiming(TimeSpan? MaximumPressDuration = null, TimeSpan? MaximumPause = null,
    TimeSpan? Interval = null, TimeSpan? MaximumInterval = null);

internal sealed record ScheduledTiming(int Step, TimeSpan Offset, TimeSpan Duration, TimeSpan Press,
    TimeSpan Span, Guid? SecondClick, ActionTiming Settings)
{
    internal static ScheduledTiming Create(SequenceStep step, int index, TimeSpan offset)
    {
        var timing = step.Timing!;
        var children = step.Kind == StepKind.Input && step.Control is { } control
            ? new[] { new TimedAction(step.Id, TimeSpan.Zero, control, step.Duration) }
            : step.Children?.ToArray() ?? [];
        bool pause = step.Kind == StepKind.Wait;
        bool serial = children.Length == 2 && children.All(c => c.Control.Kind == InputKind.MouseButton)
            && children[0].Control == children[1].Control && children[1].Offset >= children[0].Duration;
        if (!pause && (step.Kind is not (StepKind.Input or StepKind.TimedGroup) || children.Length == 0
            || children[0].Offset != TimeSpan.Zero || children.Any(c => c.Duration != children[0].Duration || c.EndPosition is not null && c.EndPosition != c.Position)
            || !serial && children.Any(c => c.Offset != TimeSpan.Zero)))
            throw new ArgumentException("Random timing supports clicks, key presses, chords and pauses. Edit overlapping recordings with fixed timing.");
        var press = pause ? TimeSpan.Zero : children[0].Duration;
        var span = pause ? TimeSpan.Zero : children.Max(c => c.Offset + c.Duration);
        var after = step.Duration - span;
        if (timing.MaximumPressDuration is { } maxPress && (pause || press <= TimeSpan.Zero || maxPress < press || maxPress > SequenceCompiler.MaximumDuration)
            || timing.MaximumPause is { } maxPause && (maxPause < after || maxPause > SequenceCompiler.MaximumDuration)
            || timing.Interval is { } interval && (pause || interval < TimeSpan.FromMilliseconds(10) || interval < span || interval > SequenceCompiler.MaximumDuration || timing.MaximumPause is not null)
            || timing.MaximumInterval is { } maximum && (timing.Interval is not { } minimum || maximum < minimum || maximum > SequenceCompiler.MaximumDuration))
            throw new ArgumentException("Timing maximums must be at least their minimums and no longer than one day.");
        var maximumSpan = span + ((timing.MaximumPressDuration ?? press) - press) * (serial ? 2 : 1);
        if (timing.Interval is { } minInterval && maximumSpan > minInterval)
            throw new ArgumentException("The minimum interval must fit the longest press or double click.");
        var result = new ScheduledTiming(index, offset, step.Duration, press, span, serial ? children[1].Id : null, timing);
        if (result.MaximumDuration > SequenceCompiler.MaximumDuration) throw new ArgumentException("The longest action must fit within one day.");
        return result;
    }
    public TimeSpan MaximumDuration => Settings.Interval is { } interval ? Settings.MaximumInterval ?? interval
        : Span + ((Settings.MaximumPressDuration ?? Press) - Press) * (SecondClick is null ? 1 : 2) + (Settings.MaximumPause ?? Duration - Span);
    internal (TimeSpan PressDelta, TimeSpan DurationDelta) Sample(Func<double> random)
    {
        var pressDelta = TimingSample.Range(Press, Settings.MaximumPressDuration, random) - Press;
        var duration = Settings.Interval is { } interval ? TimingSample.Range(interval, Settings.MaximumInterval, random)
            : Span + pressDelta * (SecondClick is null ? 1 : 2) + TimingSample.Range(Duration - Span, Settings.MaximumPause, random);
        return (pressDelta, duration - Duration);
    }
}
