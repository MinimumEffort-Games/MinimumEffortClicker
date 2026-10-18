namespace MinimumEffortClicker.Core;

/// <summary>
/// Which controller button triggers each global control (User, 2026-10-16: A Start/Stop, B Stop All, Y Record,
/// X Screenshot by default, all rebindable; 2026-10-18: View, the Back / Select button, pauses). A control without a
/// button is left out; no button serves two controls.
/// On a Steam Deck the back buttons are the default (User, 2026-10-17): its desktop layout already uses A, B, X and Y
/// for Enter, Esc, the keyboard and Space, and almost no game uses the back buttons.
/// </summary>
public sealed record ControllerControls(ControllerButton? StartStop = ControllerButton.A, ControllerButton? StopAll = ControllerButton.B,
    ControllerButton? Record = ControllerButton.Y, ControllerButton? Screenshot = ControllerButton.X, ControllerButton? Pause = ControllerButton.View)
{
    public static ControllerControls Default => new();
    /// <summary>L4 Start/Stop, L5 Stop All, R4 Record, R5 Screenshot (User's layout on the Deck), View Pause.</summary>
    public static ControllerControls SteamDeck => new(ControllerButton.L4, ControllerButton.L5, ControllerButton.R4, ControllerButton.R5);
    /// <summary>The default for this device.</summary>
    public static ControllerControls DefaultFor(bool steamDeck) => steamDeck ? SteamDeck : Default;

    private IEnumerable<ControllerButton?> All => [StartStop, StopAll, Record, Screenshot, Pause];

    public bool Valid => All.All(b => b is null || ControllerButtons.Supported(b.Value))
        && All.Where(b => b is not null).Distinct().Count() == All.Count(b => b is not null);

    /// <summary>
    /// A file from before the Pause button gets View for it; when that layout already gave View to another control, the
    /// other control keeps it and Pause starts without a button.
    /// </summary>
    public ControllerControls Settled => Valid || !(this with { Pause = null }).Valid ? this : this with { Pause = null };

    public ControllerButton? ButtonFor(ControllerCommand command) => command switch
    {
        ControllerCommand.StartStop => StartStop, ControllerCommand.StopAll => StopAll,
        ControllerCommand.Record => Record, ControllerCommand.Screenshot => Screenshot, ControllerCommand.Pause => Pause,
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    public ControllerCommand? CommandFor(ControllerButton button) =>
        StartStop == button ? ControllerCommand.StartStop : StopAll == button ? ControllerCommand.StopAll
        : Record == button ? ControllerCommand.Record : Screenshot == button ? ControllerCommand.Screenshot
        : Pause == button ? ControllerCommand.Pause : null;

    /// <summary>Gives a control a new button. A control that had that button takes the old one, so none is lost.</summary>
    public ControllerControls With(ControllerCommand command, ControllerButton? button)
    {
        var previous = ButtonFor(command);
        var swapped = button is { } chosen && CommandFor(chosen) is { } other && other != command ? Set(this, other, previous) : this;
        return Set(swapped, command, button);
        static ControllerControls Set(ControllerControls controls, ControllerCommand command, ControllerButton? button) => command switch
        {
            ControllerCommand.StartStop => controls with { StartStop = button }, ControllerCommand.StopAll => controls with { StopAll = button },
            ControllerCommand.Record => controls with { Record = button }, ControllerCommand.Pause => controls with { Pause = button },
            _ => controls with { Screenshot = button }
        };
    }
}
