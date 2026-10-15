namespace MinimumEffortClicker.Core;

/// <summary>
/// How your own input stops the clicker, besides its Stop All key (F9), which always works. Numeric values are saved in
/// the preferences file; keep them stable.
/// </summary>
public enum EmergencyStopMode
{
    /// <summary>Default: the left and right mouse buttons pressed together stop the Action List; nothing else you do stops anything.</summary>
    BothMouseButtons = 0,
    /// <summary>The original safety stop: pressing a key or button the clicker is using, or moving the mouse while it moves it, stops everything.</summary>
    AnyInput = 1,
}
