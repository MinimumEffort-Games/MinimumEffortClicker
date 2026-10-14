namespace MinimumEffortClicker.Core;

/// <summary>Numeric values are stored in profile files; keep them stable when members change.</summary>
public enum StepKind { Input = 0, Wait = 1, TimedGroup = 2, Condition = 3, Screenshot = 7 }
public sealed record TimedAction(Guid Id, TimeSpan Offset, InputControl Control, TimeSpan Duration,
    ClientPoint? Position = null, ClientPoint? EndPosition = null, Guid? TargetId = null)
{ public int ClickCount { get; init; } = 1; }
public sealed record SequenceStep(Guid Id, string Name, bool Enabled, StepKind Kind, TimeSpan Duration,
    InputControl? Control, IReadOnlyList<TimedAction>? Children, ClientPoint? Position = null, ClientPoint? EndPosition = null, string Notes = "", Guid? TargetId = null, ConditionDefinition? Condition = null)
{
    public ActionTiming? Timing { get; init; }
    public bool LeaveCursorFree { get; init; }
    public ClickMotion? Motion { get; init; }
    /// <summary>
    /// With Loop on, a Once action runs in the first loop only; every later loop skips it like an unticked action.
    /// Optional in schema 17 files: a missing value means Repeating.
    /// </summary>
    public bool Once { get; init; }
    public static SequenceStep Input(string name, InputControl control, TimeSpan duration) =>
        new(Guid.NewGuid(), name, true, StepKind.Input, duration, control, null);
    public static SequenceStep Wait(TimeSpan duration) => new(Guid.NewGuid(), "Wait", true, StepKind.Wait, duration, null, null);
    public static readonly TimeSpan MinimumScreenshotPause = TimeSpan.FromSeconds(1);
    public static SequenceStep Screenshot(TimeSpan? pauseAfterSave = null) => new(Guid.NewGuid(), "Take fullscreen screenshot", true,
        StepKind.Screenshot, pauseAfterSave ?? MinimumScreenshotPause, null, null);
    public static SequenceStep WaitUntil(string name, ConditionDefinition condition) => new(Guid.NewGuid(), name, true, StepKind.Condition, TimeSpan.Zero, null, null, Condition: condition);
    public static SequenceStep Group(string name, TimeSpan duration, IReadOnlyList<TimedAction> children) =>
        new(Guid.NewGuid(), name, true, StepKind.TimedGroup, duration, null, children.ToArray());
    public override string ToString() => Name;
}
public sealed record SequenceDefinition(IReadOnlyList<SequenceStep> Steps, string? KeyboardLayout = null, SequenceRepetition? Repetition = null);
public sealed record SequenceRepetition(int Count = 1, TimeSpan DelayBetweenLoops = default, TimeSpan? ActiveDuration = null)
{
    public void Validate()
    {
        if (Count is < 0 or > 1000000 || DelayBetweenLoops < TimeSpan.Zero || DelayBetweenLoops > TimeSpan.FromDays(1)
            || ActiveDuration is { } duration && (Count != 0 || duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(1)))
            throw new ArgumentException("Choose 1–1,000,000 loops, until stopped, or up to one day of active time; loop delay must be zero to one day.");
    }
    public override string ToString() => ActiveDuration is { } duration ? $"{duration.TotalSeconds:0.###}s active time"
        : Count == 0 ? "until stopped" : Count == 1 ? "once" : $"{Count:N0} loops";
}
public sealed record ScheduledInput(TimeSpan Offset, Guid ActionId, InputControl Control, bool Down, ClientPoint? Position = null, string Name = "Input", int StepIndex = 0, bool LeaveCursorFree = false, int ClickCount = 1)
{ public ClickMotion? Motion { get; init; } public TimeSpan PauseBefore { get; init; } public IReadOnlyList<int> PauseSteps { get; init; } = []; public int PauseAfterStep { get; init; } = -1; }
public sealed record ScheduledScreenshot(TimeSpan Offset, int StepIndex, string Name);

/// <summary>Compiler-owned immutable snapshot. No native handles or UI state.</summary>
public sealed class SequencePlan
{
    internal SequencePlan(IEnumerable<ScheduledInput> inputs, TimeSpan duration, string? keyboardLayout, IEnumerable<ScheduledCondition>? conditions = null)
    { Inputs = Array.AsReadOnly(inputs.ToArray()); Duration = duration; KeyboardLayout = keyboardLayout; Conditions = Array.AsReadOnly((conditions ?? []).ToArray()); }
    public IReadOnlyList<ScheduledInput> Inputs { get; }
    public TimeSpan Duration { get; }
    public string? KeyboardLayout { get; }
    public IReadOnlyList<ScheduledCondition> Conditions { get; }
    public IReadOnlyList<ScheduledScreenshot> Screenshots { get; internal init; } = [];
    internal IReadOnlyList<ScheduledTiming> Timings { get; init; } = [];
    public bool LeaveCursorFree => Inputs.Any(i => i.LeaveCursorFree);
    public void ValidateDesktopMode(int width, int height, int dpi)
    {
        if (LeaveCursorFree) throw new ArgumentException("Leave cursor free requires a selected Click target.");
        foreach (var condition in Conditions)
        {
            if (condition.Definition.Region is not { } region)
                throw new ArgumentException("A \"window is active\" wait needs a selected Click target.");
            if (region.Origin.Space != CoordinateSpace.Desktop)
                throw new ArgumentException($"Wait \"{condition.Name}\" looks inside a window. Select that Click target, or pick its area again on a monitor.");
            if (!region.Origin.Matches(width, height, dpi))
                throw new ArgumentException($"The monitor layout changed since wait \"{condition.Name}\" was set up. Pick its area again.");
        }
        foreach (var input in Inputs)
        {
            if (input.Position is not { } point) continue;
            if (point.Space != CoordinateSpace.Desktop)
                throw new ArgumentException("This action uses a window location. Select its Click target or set a desktop location again.");
            if (!point.Matches(width, height, dpi))
                throw new ArgumentException("The desktop layout changed. Set the click location again.");
        }
    }
    /// <summary>At an explicit Actions start, choose fixed client offsets or proportional click positions.
    /// Drags, desktop coordinates and detection regions retain strict calibration.</summary>
    public SequencePlan AdaptClickPoints(int width, int height, int dpi, bool scaleWithWindow = false)
    {
        var starts = Inputs.Where(i => i.Down && i.Control.Kind == InputKind.MouseButton).ToDictionary(i => i.ActionId, i => i.Position);
        var drags = Inputs.Where(i => !i.Down && i.Position is not null)
            .Where(up => starts.GetValueOrDefault(up.ActionId) != up.Position).Select(i => i.ActionId).ToHashSet();
        var heldMouse = new HashSet<Guid>();
        foreach (var input in Inputs)
        {
            if (input.Control.Kind == InputKind.MouseButton)
            { if (input.Down) heldMouse.Add(input.ActionId); else heldMouse.Remove(input.ActionId); }
            else if (input.Control.Kind == InputKind.PointerMove) drags.UnionWith(heldMouse);
        }
        var inputs = Inputs.Select(i =>
        {
            if (i.Control.Kind != InputKind.MouseButton || i.Position is not { Space: CoordinateSpace.WindowClient } p || drags.Contains(i.ActionId)) return i;
            if (scaleWithWindow) return i with { Position = p with { ScaleWithWindow = true } };
            var point = new ClientPoint(checked((int)Math.Round(p.X * (double)dpi / p.Dpi)), checked((int)Math.Round(p.Y * (double)dpi / p.Dpi)), width, height, dpi);
            if (!point.IsValid) throw new ArgumentException($"Saved click at {p.X}, {p.Y} is outside this window. Use Set Location to choose a new point.");
            return i with { Position = point };
        });
        return new(inputs, Duration, KeyboardLayout, Conditions)
        { Screenshots = Screenshots, Timings = Timings, Repetition = Repetition, WorstCaseDuration = WorstCaseDuration, ExecutionActionCount = ExecutionActionCount,
          LaterLoops = LaterLoops?.AdaptClickPoints(width, height, dpi, scaleWithWindow) };
    }
    public SequencePlan SampleTiming(Func<double> random)
    {
        if (Timings.Count == 0) return this;
        var sampled = Timings.ToDictionary(t => t.Step, t => (Timing: t, Delta: t.Sample(random)));
        var shifts = new Dictionary<int, TimeSpan>(); TimeSpan shift = TimeSpan.Zero;
        foreach (int step in Inputs.Select(i => i.StepIndex).Concat(Conditions.Select(c => c.StepIndex)).Concat(Screenshots.Select(s => s.StepIndex)).Concat(sampled.Keys).Distinct().Order())
        { shifts[step] = shift; if (sampled.TryGetValue(step, out var timing)) shift += timing.Delta.DurationDelta; }
        var inputs = Inputs.Select(i =>
        {
            var offset = i.Offset + shifts[i.StepIndex];
            if (sampled.TryGetValue(i.StepIndex, out var timing))
                offset += timing.Delta.PressDelta * ((i.Down ? 0 : 1) + (i.ActionId == timing.Timing.SecondClick ? 1 : 0));
            var tailDelta = sampled.TryGetValue(i.PauseAfterStep, out var tail)
                ? tail.Delta.DurationDelta - tail.Delta.PressDelta * (tail.Timing.SecondClick is null ? 1 : 2) : TimeSpan.Zero;
            return i with { Offset = offset, PauseBefore = i.PauseBefore + tailDelta + TimeSpan.FromTicks(i.PauseSteps.Where(sampled.ContainsKey).Sum(step => sampled[step].Delta.DurationDelta.Ticks)) };
        }).ToArray();
        return new(inputs, Duration + shift, KeyboardLayout, Conditions.Select(c => c with { Offset = c.Offset + shifts[c.StepIndex] }))
        { Screenshots = Array.AsReadOnly(Screenshots.Select(s => s with { Offset = s.Offset + shifts[s.StepIndex] }).ToArray()), Repetition = Repetition, WorstCaseDuration = WorstCaseDuration, ExecutionActionCount = ExecutionActionCount };
    }
    public TimeSpan WorstCaseDuration { get; internal init; }
    public int ExecutionActionCount { get; internal init; }
    public SequenceRepetition Repetition { get; internal set; } = new();
    /// <summary>
    /// The list as it runs from the second loop on: the same steps with the Once actions skipped, so step numbers still
    /// match the Action List. Null when every action repeats.
    /// </summary>
    public SequencePlan? LaterLoops { get; internal set; }
    /// <summary>Nothing to run or wait for: a later loop made only of Once actions.</summary>
    public bool IsEmpty => Inputs.Count == 0 && Conditions.Count == 0 && Screenshots.Count == 0 && Duration == TimeSpan.Zero;
}

public static class SequenceCompiler
{
    public const int MaximumActions = 10000;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromDays(1);
    public static SequenceDefinition FromHelper(HelperPlan helper)
    {
        if (helper.Validate().Count != 0) throw new ArgumentException(string.Join(" ", helper.Validate()));
        if (helper.LeaveCursorFree || helper.MaximumInterval is not null || helper.MaximumPressDuration is not null)
        {
            var press = helper.Mode == HelperMode.Scroll ? TimeSpan.Zero : helper.PressDuration;
            var action = SequenceStep.Group(RecordingNormalizer.Label(helper.Control), helper.Interval,
                [new(Guid.NewGuid(), TimeSpan.Zero, helper.Control, press, helper.Position)]) with
            { LeaveCursorFree = helper.LeaveCursorFree, Timing = new(helper.MaximumPressDuration, Interval: helper.Interval, MaximumInterval: helper.MaximumInterval) };
            var converted = new SequenceDefinition([action], Repetition: new(helper.Count, ActiveDuration: helper.ActiveDuration));
            Compile(converted); return converted;
        }
        if (helper.Count == 0 && helper.ActiveDuration is null)
        {
            if (helper.Mode == HelperMode.Hold) throw new ArgumentException("Set a hold duration before editing a key hold as steps. Use Toggle for a hold that lasts until stopped.");
            var single = helper with { Count = 1 };
            var cycleSteps = new List<SequenceStep>(FromHelper(single).Steps);
            var gap = helper.Interval - (helper.Mode == HelperMode.Scroll ? TimeSpan.Zero : helper.PressDuration);
            if (gap > TimeSpan.Zero) cycleSteps.Add(SequenceStep.Wait(gap));
            return new(cycleSteps) { Repetition = new() { Count = 0 } };
        }
        if (helper.Mode == HelperMode.Hold)
            return new([SequenceStep.Input(RecordingNormalizer.Label(helper.Control), helper.Control, helper.ActiveDuration!.Value)]);
        var steps = new List<SequenceStep>(); TimeSpan cursor = TimeSpan.Zero;
        int count = helper.Count == 0 ? MaximumActions : helper.Count;
        for (int i = 0; i < count; i++)
        {
            if (steps.Count >= MaximumActions) throw new ArgumentException("Converted helper exceeds the editor's 10,000 action limit.");
            if (helper.ActiveDuration is { } end && cursor >= end) break;
            TimeSpan press = helper.Mode == HelperMode.Scroll ? TimeSpan.Zero : helper.PressDuration;
            if (helper.ActiveDuration is { } limit && cursor + press > limit) press = limit - cursor;
            steps.Add(SequenceStep.Input(RecordingNormalizer.Label(helper.Control), helper.Control, press) with { Position = helper.Position });
            cursor += press;
            if (i == count - 1) break;
            TimeSpan gap = helper.Interval - press;
            if (helper.ActiveDuration is { } until && cursor + gap > until) gap = until - cursor;
            if (gap > TimeSpan.Zero) { steps.Add(SequenceStep.Wait(gap)); cursor += gap; }
        }
        if (helper.ActiveDuration is { } expected && cursor < expected) throw new ArgumentException("Converted helper exceeds the editor's action limit.");
        var result = new SequenceDefinition(steps); Compile(result); return result;
    }
    public static SequencePlan Compile(SequenceDefinition definition, IReadOnlyList<NamedPoint>? points = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var repetition = definition.Repetition ?? new(); repetition.Validate();
        var plan = CompileSteps(definition, points);
        if (plan.LeaveCursorFree && plan.Inputs.Any(i => !i.LeaveCursorFree))
            throw new ArgumentException("A cursor-free list can contain cursor-free clicks and pauses. Enable cursor-free on every click, and keep keyboard actions in a separate macro.");
        plan.Repetition = repetition;
        if (definition.Steps.Any(s => s is { Enabled: true, Once: true }))
        {
            var later = CompileSteps(definition with { Steps = definition.Steps.Select(s => s.Once ? s with { Enabled = false } : s).ToArray() }, points);
            later.Repetition = repetition;
            plan.LaterLoops = later;
        }
        return plan;
    }
    private static SequencePlan CompileSteps(SequenceDefinition definition, IReadOnlyList<NamedPoint>? points)
    {
        var namedPoints = NamedPoint.Index(points);
        if (definition.KeyboardLayout is { } layout && (layout.Length != 8 || !layout.All(Uri.IsHexDigit))) throw new ArgumentException("Invalid keyboard-layout context.");
        if (definition.Steps is null || definition.Steps.Count > MaximumActions) throw new ArgumentException("Sequence exceeds 10,000 actions.");
        var events = new List<ScheduledInput>(); var ids = new HashSet<Guid>();
        var conditions = new List<ScheduledCondition>(); var timings = new List<ScheduledTiming>();
        var screenshots = new List<ScheduledScreenshot>();
        int stepIndex = -1; int imageBytes = 0;
        TimeSpan cursor = TimeSpan.Zero; int actions = 0; TimeSpan pauseBefore = TimeSpan.Zero; var pauseSteps = new List<int>(); int pauseAfterStep = -1;
        foreach (var step in definition.Steps)
        {
            stepIndex++;
            if (step is null || step.Id == Guid.Empty || !ids.Add(step.Id) || string.IsNullOrWhiteSpace(step.Name) || step.Name.Length > 100
                || step.Notes is null || step.Notes.Length > 4000 || !Enum.IsDefined(step.Kind) || step.Duration < TimeSpan.Zero || step.Duration > MaximumDuration)
                throw new ArgumentException("Invalid or duplicate sequence action.");
            if (++actions > MaximumActions) throw new ArgumentException("Sequence exceeds 10,000 actions.");
            if (step.Kind != StepKind.Condition && step.Condition is not null) throw new ArgumentException("Only a condition step may contain a condition.");
            if (step.Kind == StepKind.TimedGroup && step.Children is { } groupChildren && groupChildren.Any(c => c is null))
                throw new ArgumentException("Invalid or oversized timed group.");
            int eventStart = events.Count;
            var timing = step.Timing is null ? null : ScheduledTiming.Create(step, stepIndex, cursor);
            if (step.LeaveCursorFree && step.Kind is not (StepKind.Input or StepKind.TimedGroup)) throw new ArgumentException("Only a click can leave the cursor free.");
            if (step.Enabled && cursor + step.Duration > MaximumDuration) throw new ArgumentException("Sequence exceeds one day.");
            switch (step.Kind)
            {
                case StepKind.Screenshot:
                    if (step.Duration < SequenceStep.MinimumScreenshotPause || step.Control is not null || step.Children is not null
                        || step.Position is not null || step.EndPosition is not null || step.TargetId is not null)
                        throw new ArgumentException("A screenshot needs at least 1,000 ms pause after saving and cannot contain input.");
                    if (step.Enabled) screenshots.Add(new(cursor, stepIndex, step.Name));
                    break;
                case StepKind.Condition:
                    if (step.Condition is null || step.Duration != TimeSpan.Zero || step.Control is not null || step.Children is not null
                        || step.Position is not null || step.EndPosition is not null || step.TargetId is not null)
                        throw new ArgumentException("Invalid condition step.");
                    step.Condition.Validate();
                    imageBytes += step.Condition.Template?.Bgra.Length ?? 0;
                    if (imageBytes > 350000) throw new ArgumentException("Sequence templates exceed the bounded image budget.");
                    if (step.Enabled)
                        conditions.Add(new(cursor, stepIndex, step.Name, step.Condition));
                    break;
                case StepKind.Wait:
                    if (step.Control is not null || step.Children is not null || step.Position is not null || step.EndPosition is not null || step.TargetId is not null) throw new ArgumentException("A wait cannot contain input.");
                    break;
                case StepKind.Input:
                    if (step.Control is not { } control || step.Children is not null) throw new ArgumentException("Invalid input action.");
                    Add(step.Id, control, cursor, step.Duration, Resolve(step.Position, step.TargetId), step.EndPosition, step.Name);
                    break;
                case StepKind.TimedGroup:
                    if (step.Control is not null || step.Children is null || step.Children.Count == 0 || step.Position is not null || step.EndPosition is not null || step.TargetId is not null) throw new ArgumentException("Invalid timed group.");
                    foreach (var child in step.Children)
                    {
                        if (++actions > MaximumActions || child is null || child.Id == Guid.Empty || !ids.Add(child.Id)
                            || child.Offset < TimeSpan.Zero || child.Duration < TimeSpan.Zero
                            || child.Offset > step.Duration || child.Duration > step.Duration - child.Offset)
                            throw new ArgumentException("Invalid or oversized timed group.");
                        Add(child.Id, child.Control, cursor + child.Offset, child.Duration, Resolve(child.Position, child.TargetId), child.EndPosition, step.Name, child.ClickCount);
                    }
                    break;
            }
            if (step.Motion is { } motion)
            {
                motion.Validate();
                var clickInputs = events.Skip(eventStart).ToArray();
                if (step.Kind is not (StepKind.Input or StepKind.TimedGroup) || clickInputs.Length is < 2 or > 4
                    || clickInputs.Any(i => i.Control.Kind != InputKind.MouseButton || i.Down && i.Position is null)
                    || clickInputs.Select(i => i.Control).Distinct().Count() != 1
                    || clickInputs.Where(i => i.Down).Select(i => i.Position).Distinct().Count() != 1
                    || clickInputs.Where(i => !i.Down).Any(up => up.Position is not null && up.Position != clickInputs.First(i => i.ActionId == up.ActionId && i.Down).Position)
                    || step.LeaveCursorFree && (motion.MoveDuration != TimeSpan.Zero || motion.UsePreviousPause))
                    throw new ArgumentException("Movement and position variation require saved ordinary click locations. Smooth movement cannot leave the cursor free or edit a drag.");
                for (int i = eventStart; i < events.Count; i++) events[i] = events[i] with { Motion = motion,
                    PauseBefore = motion.UsePreviousPause && events[i].Down && events[i].Offset == cursor ? pauseBefore : TimeSpan.Zero,
                    PauseSteps = motion.UsePreviousPause && events[i].Down && events[i].Offset == cursor ? pauseSteps.ToArray() : [],
                    PauseAfterStep = motion.UsePreviousPause && events[i].Down && events[i].Offset == cursor ? pauseAfterStep : -1 };
            }
            if (step.LeaveCursorFree)
                for (int i = eventStart; i < events.Count; i++)
                {
                    var input = events[i];
                    if (input.Control.Kind != InputKind.MouseButton || input.Control.Code is < 0 or > 2
                        || input.Down && input.Position is not { Space: CoordinateSpace.WindowClient })
                        throw new ArgumentException("Cursor-free clicking needs a saved window location and a left, middle or right button.");
                    events[i] = input with { LeaveCursorFree = true };
                }
            if (step.Enabled)
            {
                if (timing is not null) timings.Add(timing);
                if (step.Kind == StepKind.Wait) { pauseBefore += step.Duration; pauseSteps.Add(stepIndex); }
                else
                {
                    pauseSteps.Clear(); pauseAfterStep = -1; pauseBefore = TimeSpan.Zero;
                    if (QuickActions.IsOrdinaryClick(step))
                    {
                        pauseBefore = step.Duration - (events.Skip(eventStart).Max(i => i.Offset) - cursor);
                        pauseAfterStep = stepIndex;
                    }
                }
                cursor += step.Duration;
            }
            else events.RemoveRange(eventStart, events.Count - eventStart);
        }
        // Release before re-press at a shared boundary; retain stable order otherwise.
        var ordered = events.OrderBy(e => e.Offset).ThenBy(e => e.Down ? 1 : 0)
            .ThenBy(e => e.Down ? (SequenceKeys.IsModifier(e.Control) ? 0 : 1) : (SequenceKeys.IsModifier(e.Control) ? 1 : 0)).ToArray();
        var owned = new HashSet<InputControl>();
        foreach (var item in ordered)
        {
            if (item.Control.Kind is InputKind.Scroll or InputKind.HorizontalScroll or InputKind.PointerMove) continue;
            if (item.Down && item.Motion is not null && owned.Count != 0)
                throw new ArgumentException("Movement and position variation cannot overlap another input action.");
            if (item.Down && item.LeaveCursorFree && owned.Count != 0)
                throw new ArgumentException("Cursor-free clicks must release before the next button is pressed.");
            if (!item.Down && item.LeaveCursorFree && item.Position is { } end
                && ordered.First(i => i.Down && i.ActionId == item.ActionId).Position != end)
                throw new ArgumentException("Cursor-free mode cannot drag. Use a fixed click location.");
            if (item.Down ? !owned.Add(item.Control) : !owned.Remove(item.Control))
                throw new ArgumentException("Overlapping actions conflict on the same input.");
        }
        if (owned.Count != 0) throw new ArgumentException("Sequence leaves held input.");
        // Waits have no time limit, so this is the longest the actions themselves can take.
        var worst = cursor + TimeSpan.FromTicks(timings.Sum(t => t.MaximumDuration.Ticks - t.Duration.Ticks))
            + TimeSpan.FromTicks(ordered.Where(i => i.Down).Sum(i => i.Motion?.MoveDuration.Ticks ?? 0));
        if (worst > MaximumDuration) throw new ArgumentException("Sequence including random timing exceeds one day.");
        return new(ordered, cursor, definition.KeyboardLayout, conditions) { Screenshots = screenshots.AsReadOnly(), Timings = timings.AsReadOnly(), WorstCaseDuration = worst, ExecutionActionCount = actions };

        ClientPoint? Resolve(ClientPoint? inline, Guid? reference)
        {
            if (reference is null) return inline;
            if (inline is not null) throw new ArgumentException("Choose a named point or an inline position, not both.");
            if (!namedPoints.TryGetValue(reference.Value, out var point)) throw new ArgumentException("A referenced named point is missing. Replace the reference before playback.");
            return point.Position;
        }

        void Add(Guid id, InputControl input, TimeSpan at, TimeSpan duration, ClientPoint? position, ClientPoint? endPosition, string name, int clickCount = 1)
        {
            if (clickCount is < 1 or > 2 || clickCount == 2 && input.Kind != InputKind.MouseButton) throw new ArgumentException("Invalid click count.");
            bool instant = input.Kind is InputKind.Scroll or InputKind.HorizontalScroll or InputKind.PointerMove;
            var validation = new HelperPlan(input, input.Kind == InputKind.Scroll ? HelperMode.Scroll : HelperMode.Repeat,
                TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 1).Validate();
            bool validControl = input.Kind switch
            {
                InputKind.PointerMove => input.Code == 0 && position is not null,
                InputKind.Scroll or InputKind.HorizontalScroll => input.Code is >= -12000 and <= 12000 && input.Code != 0,
                InputKind.ScanCode => SequenceKeys.Supported.Values.Contains(input.Code),
                _ => validation.Count == 0
            };
            if (!validControl || duration < TimeSpan.Zero || !instant && duration == TimeSpan.Zero || instant && duration != TimeSpan.Zero
                || position is { IsValid: false } || endPosition is { IsValid: false }
                || input.Kind is InputKind.ScanCode && (position is not null || endPosition is not null)
                || endPosition is not null && (input.Kind != InputKind.MouseButton || position is null
                    || position.Space != endPosition.Space || !position.Matches(endPosition.Width, endPosition.Height, endPosition.Dpi)))
                throw new ArgumentException("Input action has an invalid control or duration.");
            events.Add(new(at, id, input, true, position, name, stepIndex, ClickCount: clickCount));
            if (!instant) events.Add(new(at + duration, id, input, false, endPosition, name, stepIndex));
        }
    }
}
