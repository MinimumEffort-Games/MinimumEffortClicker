namespace MinimumEffortClicker.Core;

public sealed record RecordedEdge(TimeSpan At, InputControl Control, bool Down, ClientPoint? Position = null);
public sealed record RecordingDraft(SequenceDefinition Sequence, IReadOnlyList<string> Warnings);

/// <summary>Transforms explicit capture data into editable holds and overlapping groups, never a key log.</summary>
public static class RecordingNormalizer
{
    public static RecordingDraft Normalize(IReadOnlyList<RecordedEdge> edges, TimeSpan endedAt)
    {
        if (edges.Count > SequenceCompiler.MaximumActions || endedAt < TimeSpan.Zero || endedAt > SequenceCompiler.MaximumDuration)
            throw new ArgumentException("Recording exceeds its bounds.");
        var holds = new Dictionary<InputControl, RecordedEdge>();
        var spans = new List<TimedAction>(); var warnings = new List<string>();
        var interrupted = new HashSet<Guid>();
        TimeSpan last = TimeSpan.Zero;
        foreach (var edge in edges)
        {
            if (edge is null || edge.At < last || edge.At > endedAt) throw new ArgumentException("Recording timestamps are not monotonic.");
            last = edge.At;
            if (edge.Control.Kind is InputKind.Scroll or InputKind.HorizontalScroll or InputKind.PointerMove or InputKind.Look)
            {
                if (!edge.Down) throw new ArgumentException("Invalid scroll edge.");
                spans.Add(new(Guid.NewGuid(), edge.At, edge.Control, TimeSpan.Zero, edge.Position));
            }
            else if (edge.Down)
            {
                if (!holds.TryAdd(edge.Control, edge)) throw new ArgumentException("Recording contains duplicate down transitions.");
            }
            else
            {
                if (!holds.Remove(edge.Control, out var start)) throw new ArgumentException("Recording contains an unmatched release.");
                if (edge.At <= start.At) throw new ArgumentException("Recorded hold has no measurable duration.");
                spans.Add(new(Guid.NewGuid(), start.At, edge.Control, edge.At - start.At, start.Position, edge.Position));
            }
        }
        foreach (var hold in holds)
        {
            if (endedAt > hold.Value.At)
            {
                var span = new TimedAction(Guid.NewGuid(), hold.Value.At, hold.Key, endedAt - hold.Value.At, hold.Value.Position);
                spans.Add(span); interrupted.Add(span.Id);
            }
            warnings.Add("Capture ended during a hold. Its draft duration ends at capture stop; review before keeping it.");
        }
        var ordered = spans.OrderBy(s => s.Offset).ToArray();
        var steps = new List<SequenceStep>(); TimeSpan cursor = TimeSpan.Zero;
        for (int i = 0; i < ordered.Length;)
        {
            var first = ordered[i++]; var group = new List<TimedAction> { first };
            TimeSpan end = first.Offset + first.Duration;
            TimeSpan? lastLook = first.Control.Kind == InputKind.Look ? first.Offset : null;
            // Overlapping inputs share a step; so does one continuous look with the clicks made while turning, until the mouse
            // rests for LookRest.
            while (i < ordered.Length && (ordered[i].Offset < end
                || ordered[i].Control.Kind is InputKind.Look or InputKind.MouseButton && lastLook is { } look && ordered[i].Offset - look <= LookRest))
            {
                var next = ordered[i++]; group.Add(next);
                if (next.Offset + next.Duration > end) end = next.Offset + next.Duration;
                if (next.Control.Kind == InputKind.Look) lastLook = next.Offset;
            }
            if (first.Offset > cursor) steps.Add(SequenceStep.Wait(first.Offset - cursor));
            if (group.Count == 1) steps.Add(SequenceStep.Input(RecordedLabel(first, interrupted.Contains(first.Id)), first.Control, first.Duration) with { Position = first.Position, EndPosition = first.EndPosition });
            else steps.Add(SequenceStep.Group(GroupName(group), end - first.Offset,
                group.Select(s => s with { Offset = s.Offset - first.Offset }).ToArray()));
            cursor = end;
        }
        if (endedAt > cursor) steps.Add(SequenceStep.Wait(endedAt - cursor));
        var result = new SequenceDefinition(steps);
        SequenceCompiler.Compile(result);
        return new(result, warnings.Distinct().ToArray());
    }
    /// <summary>A look continues across rests shorter than this; a longer rest starts a new Look action.</summary>
    public static readonly TimeSpan LookRest = TimeSpan.FromMilliseconds(250);
    private static string GroupName(IReadOnlyList<TimedAction> group)
    {
        var buttons = group.Where(s => s.Control.Kind == InputKind.MouseButton).Select(s => (PointerButton)s.Control.Code).Distinct().ToArray();
        if (group.All(s => s.Control.Kind == InputKind.Look)) return "Look";
        if (group.Any(s => s.Control.Kind == InputKind.Look)) return "Look" + string.Concat(buttons.Select(b => " + " + b));
        return group.Any(s => s.Control.Kind == InputKind.PointerMove) && buttons.Length > 0 ? "Drag " + string.Join(" + ", buttons) : "Overlapping inputs";
    }
    private static string RecordedLabel(TimedAction action, bool interrupted)
    {
        if (action.Control.Kind == InputKind.MouseButton && action.EndPosition is not null && action.EndPosition != action.Position) return "Drag " + (PointerButton)action.Control.Code;
        if (!interrupted && action.Duration < TimeSpan.FromMilliseconds(500))
        {
            if (action.Control.Kind == InputKind.MouseButton) return (PointerButton)action.Control.Code + " click";
            if (action.Control.Kind == InputKind.ScanCode) return "Press " + SequenceKeys.Supported.FirstOrDefault(x => x.Value == action.Control.Code).Key;
        }
        return Label(action.Control);
    }
    public static string Label(InputControl input) => input.Kind switch
    {
        InputKind.ScanCode => "Hold " + SequenceKeys.Supported.FirstOrDefault(x => x.Value == input.Code).Key,
        InputKind.MouseButton => "Hold " + (PointerButton)input.Code,
        InputKind.PointerMove => "Move pointer",
        InputKind.Look => "Look",
        InputKind.HorizontalScroll => input.Code > 0 ? "Scroll right" : "Scroll left",
        _ => input.Code > 0 ? "Scroll up" : "Scroll down"
    };
}
