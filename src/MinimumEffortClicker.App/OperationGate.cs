namespace MinimumEffortClicker.App;

/// <summary>Everything the main window can be in the middle of. Flags combine.</summary>
[Flags]
internal enum Operation
{
    None = 0,
    /// <summary>Start, test, record, or pick a point or colour on the Actions page.</summary>
    Quick = 1,
    /// <summary>Saving or loading a .mecprofile file.</summary>
    MacroFile = 2,
    /// <summary>Writing the local toggle library.</summary>
    Library = 4,
    /// <summary>Registering hotkeys or toggle shortcuts on the control thread.</summary>
    Binding = 8,
    Screenshot = 16,
    /// <summary>Background preference save; never blocks the user, only a clean exit.</summary>
    Preferences = 32,
    /// <summary>Changing the record shortcut.</summary>
    RecordKey = 64,
    /// <summary>A modal window owned by the main window is open.</summary>
    Dialog = 128,
}

/// <summary>One place to ask whether the window is busy, instead of a different subset of booleans in every handler.
/// UI-thread only; nothing here is thread-safe or needs to be.</summary>
internal sealed class OperationGate
{
    private Operation active;
    public bool Has(Operation operation) => (active & operation) != 0;
    public bool Any(Operation mask) => (active & mask) != 0;
    /// <summary>No user-visible work is running: the precondition for starting anything new.</summary>
    public bool Idle => !Any(Operation.Quick | Operation.MacroFile | Operation.Library | Operation.Binding | Operation.Screenshot);
    public void Begin(Operation operation) => active |= operation;
    public void End(Operation operation) => active &= ~operation;
}
