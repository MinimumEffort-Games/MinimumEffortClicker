namespace MinimumEffortClicker.Core;

public enum PointerButton { Left, Right, Middle, XButton1, XButton2 }

/// <summary>First domain example; interval includes the press duration. No repetition yet.</summary>
public sealed record ClickDefinition(PointerButton Button, TimeSpan Interval, TimeSpan PressDuration)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(Button))
            errors.Add("Choose a supported mouse button.");
        if (Interval <= TimeSpan.Zero || Interval > TimeSpan.FromDays(1))
            errors.Add("Interval must be greater than zero and at most one day.");
        if (PressDuration <= TimeSpan.Zero || PressDuration > Interval)
            errors.Add("Press duration must be greater than zero and no longer than the interval.");
        return errors.AsReadOnly();
    }
}
