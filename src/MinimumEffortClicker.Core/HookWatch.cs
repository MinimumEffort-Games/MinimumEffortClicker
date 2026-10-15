namespace MinimumEffortClicker.Core;

/// <summary>
/// Decides when the input hooks look lost. Windows silently removes a low-level hook whose callback once answered too
/// slowly, and then Stop All on a hook key, held-input checks and recording go quiet without any error. The system's
/// last-input time keeps moving, so input the hooks never saw means they need installing again. Ticks are the
/// millisecond counter of the system clock and may wrap, so every comparison uses the difference.
/// </summary>
public static class HookWatch
{
    /// <summary>Input this old that no hook saw counts as missed; younger input may still be on its way to a hook.</summary>
    public const int SettleMilliseconds = 500;
    /// <summary>At most one reinstall this often: input to an administrator window never reaches the hooks either.</summary>
    public const int RetryMilliseconds = 5000;

    public static bool Missed(uint now, uint lastInput, uint lastKeyboardHook, uint lastMouseHook, uint lastReinstall)
    {
        bool keyboardSaw = unchecked((int)(lastKeyboardHook - lastInput)) >= -50;
        bool mouseSaw = unchecked((int)(lastMouseHook - lastInput)) >= -50;
        return !keyboardSaw && !mouseSaw
            && unchecked((int)(now - lastInput)) >= SettleMilliseconds
            && unchecked((int)(now - lastReinstall)) >= RetryMilliseconds;
    }
}
