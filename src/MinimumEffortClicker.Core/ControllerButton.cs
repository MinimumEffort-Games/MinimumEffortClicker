namespace MinimumEffortClicker.Core;

/// <summary>
/// One button of a game controller, named the Xbox / Steam Deck way. Triggers and stick directions count as buttons
/// once pushed far enough. The numbers are saved in settings and binds, so they never change and are never reused.
/// </summary>
public enum ControllerButton
{
    A = 1, B = 2, X = 3, Y = 4,
    LeftBumper = 5, RightBumper = 6, LeftTrigger = 7, RightTrigger = 8,
    View = 9, Menu = 10, LeftStickPress = 11, RightStickPress = 12,
    DPadUp = 13, DPadDown = 14, DPadLeft = 15, DPadRight = 16,
    // Steam Deck back buttons (SDL's paddles): L4 and R4 on top, L5 and R5 below.
    L4 = 17, R4 = 18, L5 = 19, R5 = 20,
    Share = 21, Touchpad = 22,
    LeftStickUp = 23, LeftStickDown = 24, LeftStickLeft = 25, LeftStickRight = 26,
    RightStickUp = 27, RightStickDown = 28, RightStickLeft = 29, RightStickRight = 30
}
