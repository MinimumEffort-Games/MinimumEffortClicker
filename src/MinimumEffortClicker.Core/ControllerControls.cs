namespace MinimumEffortClicker.Core;

/// <summary>
/// Which controller button triggers each global control (User, 2026-10-16: A Start/Stop, B Stop All, Y Record,
/// X Screenshot by default, all rebindable). A control without a button is left out; no button serves two controls.
/// </summary>
public sealed record ControllerControls(ControllerButton? StartStop = ControllerButton.A, ControllerButton? StopAll = ControllerButton.B,
    ControllerButton? Record = ControllerButton.Y, ControllerButton? Screenshot = ControllerButton.X)
{
    public static ControllerControls Default => new();

    private IEnumerable<ControllerButton?> All => [StartStop, StopAll, Record, Screenshot];

    public bool Valid => All.All(b => b is null || ControllerButtons.Supported(b.Value))
        && All.Where(b => b is not null).Distinct().Count() == All.Count(b => b is not null);

    public ControllerButton? ButtonFor(ControllerCommand command) => command switch
    {
        ControllerCommand.StartStop => StartStop, ControllerCommand.StopAll => StopAll,
        ControllerCommand.Record => Record, ControllerCommand.Screenshot => Screenshot,
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    public ControllerCommand? CommandFor(ControllerButton button) =>
        StartStop == button ? ControllerCommand.StartStop : StopAll == button ? ControllerCommand.StopAll
        : Record == button ? ControllerCommand.Record : Screenshot == button ? ControllerCommand.Screenshot : null;

    /// <summary>Gives a control a new button. A control that had that button takes the old one, so none is lost.</summary>
    public ControllerControls With(ControllerCommand command, ControllerButton? button)
    {
        var previous = ButtonFor(command);
        var swapped = button is { } chosen && CommandFor(chosen) is { } other && other != command ? Set(this, other, previous) : this;
        return Set(swapped, command, button);
        static ControllerControls Set(ControllerControls controls, ControllerCommand command, ControllerButton? button) => command switch
        {
            ControllerCommand.StartStop => controls with { StartStop = button }, ControllerCommand.StopAll => controls with { StopAll = button },
            ControllerCommand.Record => controls with { Record = button }, _ => controls with { Screenshot = button }
        };
    }
}
