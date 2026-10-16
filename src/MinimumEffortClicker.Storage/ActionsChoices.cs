namespace MinimumEffortClicker.Storage;

/// <summary>
/// The Actions page's run and record checkboxes as the user last left them (User, 2026-10-16). Files without them
/// open with the old defaults. A loaded macro still brings its own Loop and mouse-movement choice.
/// </summary>
public sealed record ActionsChoices(bool Loop = false, bool MinimizeOnStart = true, bool RecordPauses = true,
    bool MinimizeOnRecord = true, bool SimulateMovement = false);
