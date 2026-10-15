namespace MinimumEffortClicker.Core;

/// <summary>
/// Key combinations that reach the operating system instead of the app in front: on Windows or Linux they open a
/// command line, a run box or Task Manager, close windows or shut the PC down. A macro from the Workshop that presses
/// one is refused. The Windows/Super key needs no entry: the importer refuses it in every macro. The user's own macros
/// may use these freely.
/// </summary>
public static class SystemShortcuts
{
    private static readonly int[] Ctrl = [0x1D, 0x11D], Alt = [0x38, 0x138], Shift = [0x2A, 0x36];
    private static readonly int[] FunctionKeys = [0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58];

    /// <summary>Each shortcut is a list of key groups; it fires when one key of every group is held at once.</summary>
    public static IReadOnlyList<(string Name, int[][] Keys)> All { get; } =
    [
        // The longer combination comes first so it is reported by its own name.
        ("Ctrl+Shift+Esc (Task Manager)", [Ctrl, Shift, [0x01]]),
        ("Ctrl+Esc (Start menu)", [Ctrl, [0x01]]),
        ("Alt+F4 (closes windows; on the desktop, shuts down)", [Alt, [0x3E]]),
        ("Ctrl+Alt+T (Linux terminal)", [Ctrl, Alt, [0x14]]),
        ("Alt+F2 (Linux run command)", [Alt, [0x3C]]),
        ("Alt+Space (window menu, KDE KRunner)", [Alt, [0x39]]),
        ("Ctrl+Alt+Delete (log out and shut down)", [Ctrl, Alt, [0x153, 0x53]]),
        ("Ctrl+Alt+F-key (Linux console switch)", [Ctrl, Alt, FunctionKeys]),
        ("Alt+Print Screen (Linux emergency keys)", [Alt, [0x137]]),
    ];

    /// <summary>The shortcuts a compiled sequence presses, in the order they first happen.</summary>
    public static IReadOnlyList<string> Find(SequencePlan plan)
    {
        var held = new HashSet<int>();
        var found = new List<string>();
        // OrderBy is stable, so presses at the same moment keep the order the compiler gave them.
        foreach (var input in plan.Inputs.Where(i => i.Control.Kind == InputKind.ScanCode).OrderBy(i => i.Offset))
        {
            if (!input.Down) { held.Remove(input.Control.Code); continue; }
            held.Add(input.Control.Code);
            var (name, _) = All.FirstOrDefault(shortcut => shortcut.Keys.All(group => group.Any(held.Contains)));
            if (name is not null && !found.Contains(name)) found.Add(name);
        }
        return found;
    }
}
