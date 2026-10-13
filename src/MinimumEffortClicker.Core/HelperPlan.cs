namespace MinimumEffortClicker.Core;

public enum InputKind { MouseButton, ScanCode, Scroll, PointerMove, Unicode, LogicalKey, HorizontalScroll }
public readonly record struct InputControl(InputKind Kind, int Code)
{
    public static InputControl Mouse(PointerButton button) => new(InputKind.MouseButton, (int)button);
    public static InputControl Key(int scanCode) => new(InputKind.ScanCode, scanCode);
}
public enum HelperMode { Repeat, Hold, Scroll }

/// <summary>Immutable Quick helper shared by UI, profiles and execution. Count zero is explicit until-stop.</summary>
public sealed record HelperPlan(InputControl Control, HelperMode Mode, TimeSpan Interval,
    TimeSpan PressDuration, int Count, TimeSpan? ActiveDuration = null)
{
    public ClientPoint? Position { get; init; }
    public bool LeaveCursorFree { get; init; }
    public TimeSpan? MaximumInterval { get; init; }
    public TimeSpan? MaximumPressDuration { get; init; }
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Control.Kind) || Control.Kind is InputKind.PointerMove or InputKind.Unicode or InputKind.LogicalKey) errors.Add("Choose a supported action.");
        if (Control.Kind == InputKind.MouseButton && !Enum.IsDefined((PointerButton)Control.Code)) errors.Add("Choose a mouse button.");
        if (Control.Kind == InputKind.ScanCode && !SequenceKeys.Supported.Values.Contains(Control.Code)) errors.Add("Choose a supported key.");
        if ((Mode == HelperMode.Scroll) != (Control.Kind is InputKind.Scroll or InputKind.HorizontalScroll) ||
            Control.Kind is InputKind.Scroll or InputKind.HorizontalScroll && Control.Code is not (-120 or 120)) errors.Add("Choose a supported scroll direction.");
        if (Interval < TimeSpan.FromMilliseconds(10) || Interval > TimeSpan.FromDays(1)) errors.Add("Interval must be 10 ms to one day.");
        if (PressDuration <= TimeSpan.Zero || PressDuration > Interval) errors.Add("Press duration must be positive and no longer than the interval.");
        if (Count is < 0 or > ClickPlan.MaximumCount) errors.Add("Repeat count must be 1 to 1,000,000, or explicitly until stopped.");
        if (Mode == HelperMode.Hold && Count != (ActiveDuration is null ? 0 : 1)) errors.Add("A hold must have one bounded duration or explicitly last until stopped.");
        if (Mode != HelperMode.Hold && ActiveDuration is not null && Count != 0) errors.Add("Choose either a repeat count or an active duration.");
        if (ActiveDuration is { } duration && (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(1))) errors.Add("Duration must be positive and at most one day.");
        if (MaximumInterval is { } maxInterval && (Mode == HelperMode.Hold || maxInterval < Interval || maxInterval > TimeSpan.FromDays(1))) errors.Add("Maximum interval must be at least the minimum interval and at most one day.");
        if (MaximumPressDuration is { } maxPress && (Mode != HelperMode.Repeat || maxPress < PressDuration || maxPress > Interval)) errors.Add("Maximum press duration must be at least the minimum press duration and no longer than the minimum interval.");
        if (Position is not null && (Control.Kind != InputKind.MouseButton || Mode != HelperMode.Repeat || !Position.IsValid || Position.Space != CoordinateSpace.WindowClient)) errors.Add("A fixed click needs a valid point inside the selected window.");
        if (LeaveCursorFree && (Position is null || Control.Kind != InputKind.MouseButton || Control.Code > (int)PointerButton.Middle)) errors.Add("Cursor-free mode needs a fixed point and a left, middle or right click.");
        return errors;
    }

    public static IReadOnlyDictionary<string, int> SupportedKeys { get; } = new Dictionary<string, int>
    {
        ["A"] = 0x1E, ["B"] = 0x30, ["C"] = 0x2E, ["D"] = 0x20, ["E"] = 0x12,
        ["F"] = 0x21, ["G"] = 0x22, ["H"] = 0x23, ["I"] = 0x17, ["J"] = 0x24,
        ["K"] = 0x25, ["L"] = 0x26, ["M"] = 0x32, ["N"] = 0x31, ["O"] = 0x18,
        ["P"] = 0x19, ["Q"] = 0x10, ["R"] = 0x13, ["S"] = 0x1F, ["T"] = 0x14,
        ["U"] = 0x16, ["V"] = 0x2F, ["W"] = 0x11, ["X"] = 0x2D, ["Y"] = 0x15,
        ["Z"] = 0x2C, ["Space"] = 0x39, ["Enter"] = 0x1C, ["Tab"] = 0x0F,
        ["Up"] = 0x148, ["Down"] = 0x150, ["Left"] = 0x14B, ["Right"] = 0x14D,
        ["1"] = 0x02, ["2"] = 0x03, ["3"] = 0x04, ["4"] = 0x05, ["5"] = 0x06,
        ["6"] = 0x07, ["7"] = 0x08, ["8"] = 0x09, ["9"] = 0x0A, ["0"] = 0x0B
    }.AsReadOnly();
}
