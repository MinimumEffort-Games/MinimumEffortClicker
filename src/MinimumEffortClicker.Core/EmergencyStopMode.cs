namespace MinimumEffortClicker.Core;

/// <summary>
/// How your own input stops the clicker, besides its Stop All key (F9), which always works. Numeric values are saved in
/// the preferences file; keep them stable.
/// </summary>
public enum EmergencyStopMode
{
    /// <summary>Default: only Stop All stops the clicker; nothing else you press or move stops a run, so you can click into
    /// your game. (Value 0 once meant left + right mouse button together; User dropped that stop on 2026-10-15.)</summary>
    OnlyStopAll = 0,
    /// <summary>The original safety stop: pressing a key or button the clicker is using, or moving the mouse while it moves it, stops everything.</summary>
    AnyInput = 1,
}
