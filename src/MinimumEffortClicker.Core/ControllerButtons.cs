namespace MinimumEffortClicker.Core;

/// <summary>Short names for controller buttons, as shown on buttons, in lists and on the overlay.</summary>
public static class ControllerButtons
{
    public static bool Supported(ControllerButton button) => Enum.IsDefined(button);

    public static string Label(ControllerButton button) => button switch
    {
        ControllerButton.LeftBumper => "LB", ControllerButton.RightBumper => "RB",
        ControllerButton.LeftTrigger => "LT", ControllerButton.RightTrigger => "RT",
        ControllerButton.LeftStickPress => "L3", ControllerButton.RightStickPress => "R3",
        ControllerButton.DPadUp => "D-pad Up", ControllerButton.DPadDown => "D-pad Down",
        ControllerButton.DPadLeft => "D-pad Left", ControllerButton.DPadRight => "D-pad Right",
        ControllerButton.LeftStickUp => "Left stick Up", ControllerButton.LeftStickDown => "Left stick Down",
        ControllerButton.LeftStickLeft => "Left stick Left", ControllerButton.LeftStickRight => "Left stick Right",
        ControllerButton.RightStickUp => "Right stick Up", ControllerButton.RightStickDown => "Right stick Down",
        ControllerButton.RightStickLeft => "Right stick Left", ControllerButton.RightStickRight => "Right stick Right",
        _ => button.ToString()
    };
}
