namespace MinimumEffortClicker.Core;

/// <summary>Sequence-local reusable bodies. All dependencies travel with the document.</summary>
public static class SequenceLibrary
{
    public static IEnumerable<SequenceStep> AllSteps(SequenceDefinition source) => source.Steps.Concat((source.Subsequences ?? []).SelectMany(s => s.Steps));
    public static SequencePlan Compile(SequenceDefinition source, IReadOnlyList<NamedPoint>? points = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Steps is null) throw new ArgumentException("Missing sequence actions.");
        var library = source.Subsequences ?? [];
        if (library.Count == 0 && !source.Steps.Any(s => s?.Kind is StepKind.Subsequence or StepKind.ConditionalFlow)) return SequenceCompiler.CompileFlat(source, points);
        if (library.Count > 32) throw new ArgumentException("A sequence supports at most 32 reusable subsequences.");
        var index = new Dictionary<Guid, ReusableSequence>();
        foreach (var body in library)
            if (body is null || body.Id == Guid.Empty || string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 100
                || body.Steps is null || body.Steps.Count == 0 || !index.TryAdd(body.Id, body))
                throw new ArgumentException("Invalid or duplicate reusable subsequence.");
        if (AllSteps(source).Sum(s => 1L + (s?.Children?.Count ?? 0)) > SequenceCompiler.MaximumActions)
            throw new ArgumentException("Stored subsequences exceed the action budget.");
        if (AllSteps(source).Sum(s => (long)(s?.Condition?.Template?.Bgra.Length ?? 0)) > 350000)
            throw new ArgumentException("Stored templates exceed the bounded image budget.");
        foreach (var body in library.Select(b => b.Steps).Prepend(source.Steps))
            SequenceCompiler.CompileFlat(new(body.Select(Placeholder).ToArray(), source.KeyboardLayout), points);
        int compileWork = 0;
        foreach (var body in library) Expand(body.Steps, [body.Id]);
        return Expand(source.Steps, []);

        SequenceStep Placeholder(SequenceStep step) => step?.Kind switch
        {
            StepKind.Subsequence => step with { Kind = StepKind.Wait, SubsequenceId = null },
            StepKind.ConditionalFlow => step with { Kind = StepKind.Condition, Flow = null },
            _ => step!
        };
        SequencePlan Expand(IReadOnlyList<SequenceStep> steps, HashSet<Guid> ancestors)
        {
            var output = new List<SequenceStep>(); var map = new List<int>(); var flows = new Dictionary<int, CompiledFlow>();
            Visit(steps, ancestors, null, true, false);
            var flat = SequenceCompiler.CompileFlat(new(output, source.KeyboardLayout), points);
            var conditions = flat.Conditions.Select(c => flows.TryGetValue(c.StepIndex, out var flow) ? c with { Flow = flow } : c).ToArray();
            TimeSpan worst = flat.WorstCaseDuration;
            long actions = flat.ExecutionActionCount;
            foreach (var condition in conditions.Where(c => c.Flow is not null))
            {
                var f = condition.Flow!;
                if (f.Kind == FlowKind.IfElse)
                {
                    var alternate = f.Otherwise?.WorstCaseDuration ?? TimeSpan.Zero;
                    worst += f.Body.WorstCaseDuration > alternate ? f.Body.WorstCaseDuration : alternate;
                    actions += Math.Max(Cost(f.Body), f.Otherwise is null ? 0 : Cost(f.Otherwise));
                }
                else
                {
                    worst += (f.Body.WorstCaseDuration + condition.Definition.Policy.Timeout * (condition.Definition.Policy.Retries + 1)) * f.MaximumIterations;
                    actions += (Cost(f.Body) + 1) * f.MaximumIterations;
                }
                if (worst > SequenceCompiler.MaximumDuration || actions > SequenceCompiler.MaximumActions)
                    throw new ArgumentException("Conditional expansion exceeds the one-day or 10,000-action execution budget.");
            }
            return new(flat.Inputs, flat.Duration, flat.KeyboardLayout, conditions) { Screenshots = flat.Screenshots, Timings = flat.Timings, SourceSteps = map.AsReadOnly(), WorstCaseDuration = worst, ExecutionActionCount = checked((int)actions) };

            SequencePlan Resolve(Guid id, HashSet<Guid> path)
            {
                if (!index.TryGetValue(id, out var nested)) throw new ArgumentException("Missing subsequence reference.");
                if (path.Count >= 4 || !path.Add(id)) throw new ArgumentException("Subsequence cycle or nesting deeper than four calls.");
                var result = Expand(nested.Steps, path); path.Remove(id); return result;
            }
            void Visit(IReadOnlyList<SequenceStep> body, HashSet<Guid> path, int? origin, bool enabled, bool clone)
            {
                for (int i = 0; i < body.Count; i++)
                {
                    if (++compileWork > 100000) throw new ArgumentException("Subsequence expansion exceeds the compilation work budget.");
                    var step = body[i]; int sourceIndex = origin ?? i;
                    if (step.Kind == StepKind.Subsequence)
                    {
                        if (step.SubsequenceId is not { } id || !index.TryGetValue(id, out var nested) || step.Duration != TimeSpan.Zero || step.Flow is not null)
                            throw new ArgumentException("Invalid or missing subsequence reference.");
                        if (path.Count >= 4 || !path.Add(id)) throw new ArgumentException("Subsequence cycle or nesting deeper than four calls.");
                        Visit(nested.Steps, path, sourceIndex, enabled && step.Enabled, true); path.Remove(id);
                    }
                    else
                    {
                        if (output.Count >= SequenceCompiler.MaximumActions) throw new ArgumentException("Expanded subsequence exceeds 10,000 actions.");
                        if (step.Kind == StepKind.ConditionalFlow)
                        {
                            var flow = step.Flow ?? throw new ArgumentException("Missing conditional flow.");
                            if (!Enum.IsDefined(flow.Kind) || flow.MaximumIterations is < 1 or > 1000
                                || flow.Kind == FlowKind.IfElse && flow.MaximumIterations != 1
                                || flow.Kind == FlowKind.RepeatUntil && flow.ElseId is not null
                                || step.Condition!.Kind == ConditionKind.RegionChanged)
                                throw new ArgumentException("Use a pixel, image or window-state check; repeat bounds are 1–1000, with no Else for repeats.");
                            var compiled = new CompiledFlow(flow.Kind, Resolve(flow.BodyId, path), flow.ElseId is { } other ? Resolve(other, path) : null, flow.MaximumIterations);
                            if (enabled && step.Enabled) flows.Add(output.Count, compiled);
                            step = Placeholder(step) with { Condition = step.Condition with { CheckOnly = true } };
                        }
                        output.Add(step with { Id = clone ? Guid.NewGuid() : step.Id, Enabled = enabled && step.Enabled,
                            Children = clone ? step.Children?.Select(c => c with { Id = Guid.NewGuid() }).ToArray() : step.Children });
                        map.Add(sourceIndex);
                    }
                }
            }
        }
    }
    private static long Cost(SequencePlan plan) => plan.ExecutionActionCount;
}
