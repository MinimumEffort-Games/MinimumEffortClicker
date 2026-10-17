namespace MinimumEffortClicker.Core;

/// <summary>
/// The keyboard layout a recording was made with, so keys are only replayed into the same layout. Windows stores its
/// layout handle as 8 hex digits; Linux uses the same digits for common layouts and "linux:" plus the desktop's own
/// name (such as "linux:xkb:us+intl") for the rest, so a recording on any layout can be saved and played.
/// </summary>
public static class KeyboardLayoutId
{
    /// <summary>Longest "linux:" name kept; xkb names with a variant are far shorter.</summary>
    public const int MaxLinuxLength = 80;

    public static bool Valid(string layout) => layout.Length == 8 && layout.All(Uri.IsHexDigit)
        || layout.StartsWith("linux:", StringComparison.Ordinal) && layout.Length > "linux:".Length && layout.Length <= MaxLinuxLength
            && layout.All(c => c is > ' ' and < (char)127);
}
