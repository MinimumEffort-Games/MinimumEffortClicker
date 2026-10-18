namespace MinimumEffortClicker.Core;

/// <summary>
/// The Click target chosen last, so the next start selects it again when its window is open (User, 2026-10-18).
/// Program is the executable's name without folder or extension; Title the window's title when it was chosen.
/// </summary>
public sealed record RememberedTarget(string Program, string Title)
{
    public bool Valid => Program is { Length: > 0 and <= 260 } && Title is { Length: <= 512 };

    /// <summary>
    /// Which open window is the remembered one: the same program with the same title, otherwise that program's only
    /// window. Several windows of the program and none with the title is no pick rather than a guess. -1 for none.
    /// </summary>
    public int Pick(IReadOnlyList<(string Program, string Title)> windows)
    {
        int only = -1, count = 0;
        for (int i = 0; i < windows.Count; i++)
        {
            if (!string.Equals(windows[i].Program, Program, StringComparison.OrdinalIgnoreCase)) continue;
            if (windows[i].Title == Title) return i;
            only = i; count++;
        }
        return count == 1 ? only : -1;
    }
}
