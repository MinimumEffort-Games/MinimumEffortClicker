namespace MinimumEffortClicker.Core;

public enum QuickClickKind { Single, Double, Hold }
/// <summary>Numeric values are the order of the Scroll direction choice in the editor.</summary>
public enum ScrollDirection { Up, Down, Left, Right }
/// <summary>A scroll action as the editor shows it. Gap is the time between notches.</summary>
public sealed record QuickScroll(ScrollDirection Direction, int Notches, TimeSpan Gap, ClientPoint? Position, TimeSpan After);

/// <summary>Quick authoring produces ordinary sequence steps; playback uses the shared compiler.</summary>
public static class QuickActions
{
    public static SequenceDefinition WithClickMovement(SequenceDefinition source, bool enabled)
    {
        SequenceStep Apply(SequenceStep step) => !IsOrdinaryClick(step) ? step : step with
        { Motion = enabled || step.Motion?.OffsetPixels > 0 ? new(OffsetPixels: step.Motion?.OffsetPixels ?? 0, UsePreviousPause: enabled) : null };
        return source with { Steps = source.Steps.Select(Apply).ToArray() };
    }
    /// <summary>List-wide authoring policy. Copies the document; native compatibility is still compiler-validated.</summary>
    public static SequenceDefinition WithCursorFree(SequenceDefinition source, bool enabled)
    {
        SequenceStep Apply(SequenceStep step) => step with
        { LeaveCursorFree = enabled && step.Kind is StepKind.Input or StepKind.TimedGroup };
        return source with { Steps = source.Steps.Select(Apply).ToArray() };
    }
    public static SequenceDefinition RecordedActions(SequenceDefinition recording, bool includePauses, bool movement = false)
    {
        var inputs = MergeScrollBursts(recording.Steps.SkipWhile(s => s.Kind == StepKind.Wait).Reverse().SkipWhile(s => s.Kind == StepKind.Wait).Reverse().ToList());
        var result = recording with { Steps = inputs.Where(s => includePauses || s.Kind != StepKind.Wait)
            .Select(s => s.Kind == StepKind.Wait ? s with { Name = "Pause" } : movement && includePauses && IsOrdinaryClick(s) ? s with { Motion = new(UsePreviousPause: true) } : s).ToArray(), Repetition = new() };
        var plan = SequenceCompiler.Compile(result);
        // A mouse-only recording must not depend on the receiver's keyboard layout.
        return plan.Inputs.Any(i => i.Control.Kind == InputKind.ScanCode) ? result : result with { KeyboardLayout = null };
    }
    public static bool IsOrdinaryClick(SequenceStep step)
    {
        if (step.Kind == StepKind.Input) return step.Control?.Kind == InputKind.MouseButton && step.Position is not null
            && (step.EndPosition is null || step.EndPosition == step.Position);
        return step.Kind == StepKind.TimedGroup && step.Children is { Count: > 0 and <= 2 } children
            && children.All(c => c.Control.Kind == InputKind.MouseButton && c.Position is not null && (c.EndPosition is null || c.EndPosition == c.Position))
            && children.Select(c => c.Control).Distinct().Count() == 1 && children.Select(c => c.Position).Distinct().Count() == 1;
    }
    public static SequenceStep Click(PointerButton button, QuickClickKind kind, TimeSpan press, TimeSpan doubleGap,
        TimeSpan after, ClientPoint? point = null)
    {
        if (!Enum.IsDefined(kind) || doubleGap < TimeSpan.Zero) throw new ArgumentException("Choose a click type and non-negative double-click gap.");
        var control = InputControl.Mouse(button);
        var children = new List<TimedAction> { new(Guid.NewGuid(), TimeSpan.Zero, control, press, point) };
        if (kind == QuickClickKind.Double) children.Add(new(Guid.NewGuid(), press + doubleGap, control, press, point) { ClickCount = 2 });
        string name = kind == QuickClickKind.Hold ? "Hold " + button : kind == QuickClickKind.Double ? "Double " + button + " click" : button + " click";
        return Group(name, children, after);
    }

    public static SequenceStep Keys(IReadOnlyList<int> scanCodes, TimeSpan press, TimeSpan after, bool hold)
    {
        if (scanCodes.Count is < 1 or > 8 || scanCodes.Distinct().Count() != scanCodes.Count)
            throw new ArgumentException("Choose one key and distinct optional modifiers.");
        string name = string.Join(" + ", scanCodes.Select(c => SequenceKeys.Supported.FirstOrDefault(k => k.Value == c).Key
            ?? throw new ArgumentException("Choose a supported key.")));
        return Group((hold ? "Hold " : "Press ") + name,
            scanCodes.Select(c => new TimedAction(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(c), press)).ToArray(), after);
    }

    private static SequenceStep Group(string name, IReadOnlyList<TimedAction> children, TimeSpan after)
    {
        if (after < TimeSpan.Zero || after > TimeSpan.FromDays(1)) throw new ArgumentException("Delay after an action must be zero to one day.");
        var result = SequenceStep.Group(name, children.Max(c => c.Offset + c.Duration) + after, children);
        SequenceCompiler.Compile(new([result]));
        return result;
    }

    /// <summary>One wheel notch, the size a mouse wheel reports for a single click of the wheel.</summary>
    public const int Notch = 120;
    /// <summary>Recorded notches closer together than this, in one direction at one spot, become one scroll action.</summary>
    public static readonly TimeSpan ScrollBurstGap = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// A scroll of whole notches. Every notch is its own input at its own moment, so apps and games that count wheel
    /// events see each one; one big jump would read as a single notch to many of them.
    /// </summary>
    public static SequenceStep Scroll(ScrollDirection direction, int notches, TimeSpan gap, TimeSpan after, ClientPoint? point = null)
    {
        if (!Enum.IsDefined(direction) || notches is < 1 or > 100) throw new ArgumentException("Choose a direction and 1–100 notches.");
        if (gap < TimeSpan.Zero || gap > TimeSpan.FromSeconds(5)) throw new ArgumentException("Time between notches must be 0–5,000 ms.");
        var control = new InputControl(direction is ScrollDirection.Up or ScrollDirection.Down ? InputKind.Scroll : InputKind.HorizontalScroll,
            direction is ScrollDirection.Up or ScrollDirection.Right ? Notch : -Notch);
        var children = Enumerable.Range(0, notches).Select(i => new TimedAction(Guid.NewGuid(), gap * i, control, TimeSpan.Zero, point)).ToArray();
        return Group(ScrollName(direction, notches), children, after);
    }
    public static string ScrollName(ScrollDirection direction, int notches) => $"Scroll {direction.ToString().ToLowerInvariant()}" + (notches > 1 ? $" ×{notches}" : "");

    /// <summary>
    /// Reads a step back as a scroll action when it is one: wheel input in one direction at one spot, nothing else.
    /// Recorded wheels can report less than a whole notch (touchpads); those add up to the nearest whole notch.
    /// </summary>
    public static QuickScroll? ReadScroll(SequenceStep step)
    {
        IReadOnlyList<TimedAction> parts = step.Kind == StepKind.Input && step.Control is { } control
            ? [new TimedAction(step.Id, TimeSpan.Zero, control, step.Duration, step.Position)]
            : step.Kind == StepKind.TimedGroup ? step.Children ?? [] : [];
        if (parts.Count == 0 || parts.Any(p => p.Control.Kind is not (InputKind.Scroll or InputKind.HorizontalScroll) || p.Control.Kind != parts[0].Control.Kind
            || Math.Sign(p.Control.Code) != Math.Sign(parts[0].Control.Code) || p.Position != parts[0].Position || p.Duration != TimeSpan.Zero || p.TargetId is not null)) return null;
        var first = parts[0].Control;
        var direction = first.Kind == InputKind.Scroll ? (first.Code > 0 ? ScrollDirection.Up : ScrollDirection.Down) : (first.Code > 0 ? ScrollDirection.Right : ScrollDirection.Left);
        int notches = Math.Clamp((int)Math.Round(parts.Sum(p => Math.Abs((double)p.Control.Code)) / Notch), 1, 100);
        var offsets = parts.Select(p => p.Offset).Order().ToArray();
        var gap = offsets.Length > 1 ? (offsets[^1] - offsets[0]) / (offsets.Length - 1) : TimeSpan.FromMilliseconds(30);
        var end = offsets[^1];
        return new(direction, notches, TimeSpan.FromMilliseconds(Math.Round(gap.TotalMilliseconds)), parts[0].Position, step.Duration - end);
    }

    /// <summary>
    /// A recording makes one step per wheel notch, with the pauses between them. A quick run of notches in one direction
    /// at one spot becomes one scroll action that keeps their timing, so the list shows "Scroll down ×5", not ten rows.
    /// </summary>
    private static List<SequenceStep> MergeScrollBursts(List<SequenceStep> steps)
    {
        static bool IsNotch(SequenceStep s) => s.Kind == StepKind.Input && s.Control?.Kind is InputKind.Scroll or InputKind.HorizontalScroll;
        static bool Same(SequenceStep a, SequenceStep b) => a.Control!.Value.Kind == b.Control!.Value.Kind
            && Math.Sign(a.Control.Value.Code) == Math.Sign(b.Control.Value.Code) && a.Position == b.Position;
        var result = new List<SequenceStep>();
        for (int i = 0; i < steps.Count;)
        {
            if (!IsNotch(steps[i])) { result.Add(steps[i++]); continue; }
            var first = steps[i];
            var children = new List<TimedAction> { new(Guid.NewGuid(), TimeSpan.Zero, first.Control!.Value, TimeSpan.Zero, first.Position) };
            TimeSpan at = TimeSpan.Zero; int next = i + 1;
            while (next + 1 < steps.Count && steps[next].Kind == StepKind.Wait && steps[next].Duration <= ScrollBurstGap && IsNotch(steps[next + 1]) && Same(first, steps[next + 1]))
            {
                at += steps[next].Duration;
                children.Add(new(Guid.NewGuid(), at, steps[next + 1].Control!.Value, TimeSpan.Zero, steps[next + 1].Position));
                next += 2;
            }
            var merged = SequenceStep.Group("Scroll", at, children);
            var read = ReadScroll(merged)!;
            result.Add(merged with { Name = ScrollName(read.Direction, read.Notches) });
            i = next;
        }
        return result;
    }

    public static SequenceStep Pause(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentException("Pause must be longer than zero.");
        var step = SequenceStep.Wait(duration) with { Name = "Pause" };
        SequenceCompiler.Compile(new([step])); return step;
    }
}
