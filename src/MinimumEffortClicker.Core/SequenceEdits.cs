namespace MinimumEffortClicker.Core;

public static class SequenceEdits
{
    public static IReadOnlyList<SequenceStep> CopyWithNewIds(IEnumerable<SequenceStep> steps) => steps.Select(s =>
        s with { Id = Guid.NewGuid(), Children = s.Children?.Select(c => c with { Id = Guid.NewGuid() }).ToArray() }).ToArray();
    public static SequenceDefinition ChangeIdleGaps(SequenceDefinition source, Func<TimeSpan, TimeSpan> transform, IReadOnlyList<NamedPoint>? points = null)
    {
        var result = source with { Steps = source.Steps.Select(s => s.Kind == StepKind.Wait ? s with { Duration = transform(s.Duration) } : s).ToArray() };
        SequenceCompiler.Compile(result, points); return result;
    }
    public static SequenceDefinition Replace(SequenceDefinition source, int start, int count, IReadOnlyList<SequenceStep> replacement, IReadOnlyList<NamedPoint>? points = null)
    {
        if (start < 0 || count < 0 || start > source.Steps.Count - count) throw new ArgumentOutOfRangeException(nameof(start));
        var result = source with { Steps = source.Steps.Take(start).Concat(replacement).Concat(source.Steps.Skip(start + count)).ToArray() };
        SequenceCompiler.Compile(result, points); return result;
    }
    public static SequenceDefinition Snapshot(SequenceDefinition source) => source with { Steps = Array.AsReadOnly(source.Steps.Select(s =>
        s with { Children = s.Children is null ? null : Array.AsReadOnly(s.Children.ToArray()) }).ToArray()) };
}

/// <summary>Bounded draft history. Editing never mutates a compiled active run.</summary>
public sealed class SequenceHistory
{
    private readonly List<SequenceDefinition> undo = [], redo = [];
    private readonly IReadOnlyList<NamedPoint>? points;
    public SequenceHistory(SequenceDefinition initial, IReadOnlyList<NamedPoint>? points = null)
    { this.points = points?.ToArray(); SequenceCompiler.Compile(initial, this.points); Current = SequenceEdits.Snapshot(initial); }
    public SequenceDefinition Current { get; private set; }
    public bool CanUndo => undo.Count != 0;
    public bool CanRedo => redo.Count != 0;
    public void Commit(SequenceDefinition next)
    {
        SequenceCompiler.Compile(next, points);
        undo.Add(Current); redo.Clear(); Current = SequenceEdits.Snapshot(next);
        while (undo.Count > 50 || undo.Sum(Weight) > 50000) undo.RemoveAt(0);
    }
    public void Undo() { if (!CanUndo) return; redo.Add(Current); Current = undo[^1]; undo.RemoveAt(undo.Count - 1); }
    public void Redo() { if (!CanRedo) return; undo.Add(Current); Current = redo[^1]; redo.RemoveAt(redo.Count - 1); }
    private static int Weight(SequenceDefinition definition) => definition.Steps.Sum(s => 1 + (s.Children?.Count ?? 0) + (s.Condition?.Template?.Bgra.Length ?? 0) / 64);
}
