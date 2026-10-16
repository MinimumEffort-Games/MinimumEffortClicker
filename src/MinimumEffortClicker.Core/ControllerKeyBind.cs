namespace MinimumEffortClicker.Core;

/// <summary>
/// One Controller to Key bind (User, 2026-10-16): while the controller button is held, the key or mouse button is held,
/// and releasing the controller button releases it. It works in whichever window is in front, so it needs no target.
/// </summary>
public sealed record ControllerKeyBind(Guid Id, string Name, ControllerButton Button, InputControl Output, bool Active = true)
{
    public const int MaxNameLength = 100;

    public bool Valid => Id != Guid.Empty && !string.IsNullOrWhiteSpace(Name) && Name.Length <= MaxNameLength
        && ControllerButtons.Supported(Button) && OutputSupported(Output);

    public static bool OutputSupported(InputControl output) =>
        output.Kind == InputKind.ScanCode && SequenceKeys.Supported.Values.Contains(output.Code)
        || output.Kind == InputKind.MouseButton && Enum.IsDefined((PointerButton)output.Code);
}
