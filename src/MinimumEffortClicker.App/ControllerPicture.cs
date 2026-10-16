using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// The controller picture: buttons that are taken are outlined red (with what they do on hover), the chosen one green,
/// and a click on a button reports it. The Steam Deck back buttons and the touchpad are not on the picture.
/// </summary>
internal sealed class ControllerPicture : PictureButtons
{
    // Map value = the ControllerButton number times 8; the art name is the button's name in lower case (Trace-Picture).
    private static readonly ControllerButton[] OnPicture = Enum.GetValues<ControllerButton>()
        .Where(b => b is not (ControllerButton.L4 or ControllerButton.R4 or ControllerButton.L5 or ControllerButton.R5 or ControllerButton.Touchpad)).ToArray();

    public static bool Shows(ControllerButton button) => OnPicture.Contains(button);

    public event Action<ControllerButton>? Clicked;

    public ControllerPicture() : base("controller-layout",
        OnPicture.Select(b => new Part((int)b * 8, b.ToString().ToLowerInvariant(), ControllerButtons.Label(b))).ToArray())
    {
        System.Windows.Automation.AutomationProperties.SetName(this, "Controller");
        ButtonClicked += value => Clicked?.Invoke((ControllerButton)(value / 8));
    }

    public ControllerButton? Selected
    {
        get => SelectedValue is { } v ? (ControllerButton)(v / 8) : null;
        set => SelectedValue = value is { } b && Shows(b) ? (int)b * 8 : null;
    }

    /// <summary>Outlines a button red and says on hover what it does now; null frees it.</summary>
    public void Taken(ControllerButton button, string? what) { if (Shows(button)) SetTaken((int)button * 8, what); }
}
