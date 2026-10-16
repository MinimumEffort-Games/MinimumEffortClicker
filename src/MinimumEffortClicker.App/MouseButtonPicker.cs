using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>Chooses a mouse button by clicking it on the labelled mouse; the chosen button's outline is green.</summary>
internal sealed class MouseButtonPicker : PictureButtons
{
    // Map values as in the mouse entry of scripts/Build-UiArt.ps1.
    private static readonly (PointerButton Button, Part Part)[] Buttons =
    [
        (PointerButton.Left, new(40, "left", "LMB (left)")), (PointerButton.Middle, new(80, "middle", "MMB (middle)")),
        (PointerButton.Right, new(120, "right", "RMB (right)")), (PointerButton.XButton1, new(160, "side1", "M4 (back)")),
        (PointerButton.XButton2, new(200, "side2", "M5 (forward)"))
    ];

    public event EventHandler? SelectedChanged;

    public MouseButtonPicker() : base("mouse-buttons", Buttons.Select(b => b.Part).ToArray())
    {
        System.Windows.Automation.AutomationProperties.SetName(this, "Mouse button");
        ButtonClicked += value => Selected = Buttons.First(b => b.Part.Value == value).Button;
    }

    public PointerButton? Selected
    {
        get => SelectedValue is { } v ? Buttons.First(b => b.Part.Value == v).Button : null;
        set
        {
            if (Selected == value) return;
            SelectedValue = value is { } button ? Buttons.First(b => b.Button == button).Part.Value : null;
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
