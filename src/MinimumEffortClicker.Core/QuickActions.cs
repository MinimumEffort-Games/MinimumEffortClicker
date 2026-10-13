namespace MinimumEffortClicker.Core;

public enum QuickClickKind { Single, Double, Hold }

/// <summary>Quick authoring produces ordinary sequence steps; playback uses the shared compiler.</summary>
public static class QuickActions
{
    public static SequenceDefinition WithClickMovement(SequenceDefinition source, bool enabled)
    {
        SequenceStep Apply(SequenceStep step) => !IsOrdinaryClick(step) ? step : step with
        { Motion = enabled || step.Motion?.OffsetPixels > 0 ? new(OffsetPixels: step.Motion?.OffsetPixels ?? 0, UsePreviousPause: enabled) : null };
        return source with { Steps = source.Steps.Select(Apply).ToArray(),
            Subsequences = source.Subsequences?.Select(body => body with { Steps = body.Steps.Select(Apply).ToArray() }).ToArray() };
    }
    /// <summary>List-wide authoring policy. Copies the document; native compatibility is still compiler-validated.</summary>
    public static SequenceDefinition WithCursorFree(SequenceDefinition source, bool enabled)
    {
        SequenceStep Apply(SequenceStep step) => step with
        { LeaveCursorFree = enabled && step.Kind is StepKind.Input or StepKind.TimedGroup };
        return source with { Steps = source.Steps.Select(Apply).ToArray(),
            Subsequences = source.Subsequences?.Select(body => body with { Steps = body.Steps.Select(Apply).ToArray() }).ToArray() };
    }
    public static SequenceDefinition RecordedActions(SequenceDefinition recording, bool includePauses, bool movement = false)
    {
        var inputs = recording.Steps.SkipWhile(s => s.Kind == StepKind.Wait).Reverse().SkipWhile(s => s.Kind == StepKind.Wait).Reverse();
        var result = recording with { Steps = inputs.Where(s => includePauses || s.Kind != StepKind.Wait)
            .Select(s => s.Kind == StepKind.Wait ? s with { Name = "Pause" } : movement && includePauses && IsOrdinaryClick(s) ? s with { Motion = new(UsePreviousPause: true) } : s).ToArray(), Repetition = new() };
        var plan = SequenceCompiler.Compile(result);
        // A mouse-only recording must not depend on the receiver's keyboard layout.
        return plan.AllInputs().Any(i => i.Control.Kind is InputKind.ScanCode or InputKind.LogicalKey)
            ? result : result with { KeyboardLayout = null };
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

    public static SequenceStep Pause(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentException("Pause must be longer than zero.");
        var step = SequenceStep.Wait(duration) with { Name = "Pause" };
        SequenceCompiler.Compile(new([step])); return step;
    }
}
