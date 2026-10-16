using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// The app's pictures: keycaps, controller buttons, action types and the mouse and keyboard. They are cut from the
/// art sheets by scripts/Build-UiArt.ps1 and built into the app. A picture that is missing returns null, so the caller
/// shows its text instead and the app keeps working.
/// </summary>
internal static class UiArt
{
    private static readonly Dictionary<string, ImageSource?> Cache = [];

    /// <summary>The keycap for a key name as the app lists it (SequenceKeys), or null for keys without one (F13–F24).</summary>
    public static ImageSource? Key(string name) => Load("keys/" + KeyFileName(name));

    /// <summary>A controller picture by its file name, for example "a", "lb" or "dpad-up".</summary>
    public static ImageSource? Controller(string name) => Load("controller/" + name);

    /// <summary>An action type: click, key-press, pause, wait-until, fullscreen-screenshot or scroll.</summary>
    public static ImageSource? Action(string name) => Load("actions/" + name);

    /// <summary>
    /// A controller button as a picture of the given height: its own icon, or for a stick direction the stick with an
    /// arrow keycap. Buttons without a picture (the Steam Deck back buttons and the touchpad) are their name as text.
    /// </summary>
    public static FrameworkElement ControllerGlyph(ControllerButton button, double height)
    {
        string? icon = button switch
        {
            ControllerButton.A => "a", ControllerButton.B => "b", ControllerButton.X => "x", ControllerButton.Y => "y",
            ControllerButton.LeftBumper => "lb", ControllerButton.RightBumper => "rb", ControllerButton.LeftTrigger => "lt", ControllerButton.RightTrigger => "rt",
            ControllerButton.View => "view-symbol", ControllerButton.Menu => "menu-symbol", ControllerButton.Share => "share-symbol",
            ControllerButton.LeftStickPress => "l3", ControllerButton.RightStickPress => "r3",
            ControllerButton.DPadUp => "dpad-up", ControllerButton.DPadDown => "dpad-down", ControllerButton.DPadLeft => "dpad-left", ControllerButton.DPadRight => "dpad-right",
            ControllerButton.LeftStickUp or ControllerButton.LeftStickDown or ControllerButton.LeftStickLeft or ControllerButton.LeftStickRight => "ls",
            ControllerButton.RightStickUp or ControllerButton.RightStickDown or ControllerButton.RightStickLeft or ControllerButton.RightStickRight => "rs",
            _ => null
        };
        string? arrow = button switch
        {
            ControllerButton.LeftStickUp or ControllerButton.RightStickUp => "Up",
            ControllerButton.LeftStickDown or ControllerButton.RightStickDown => "Down",
            ControllerButton.LeftStickLeft or ControllerButton.RightStickLeft => "Left",
            ControllerButton.LeftStickRight or ControllerButton.RightStickRight => "Right",
            _ => null
        };
        string label = ControllerButtons.Label(button);
        if (icon is null || Controller(icon) is not { } picture)
            return new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = label };
        System.Windows.Automation.AutomationProperties.SetName(row, label);
        RenderOptions.SetBitmapScalingMode(row, BitmapScalingMode.HighQuality);
        row.Children.Add(new Image { Source = picture, Height = height, Stretch = Stretch.Uniform });
        if (arrow is not null && Key(arrow) is { } keycap)
            row.Children.Add(new Image { Source = keycap, Height = height, Stretch = Stretch.Uniform, Margin = new Thickness(2, 0, 0, 0) });
        return row;
    }

    /// <summary>What a button that shows a chosen key says: the keycap next to its name, or only the name for mouse
    /// buttons and keys without a keycap.</summary>
    public static object InputContent(InputControl? input, string label)
    {
        string? name = input is { } i ? KeyPickerWindow.KeyName(i) : null;
        if (name is null || Key(name) is not { } art) return label;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var image = new Image { Source = art, Height = 30, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        content.Children.Add(image);
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    /// <summary>A key or mouse button as a small picture for a list: its keycap, the labelled mouse with that button
    /// outlined, or its name as text when there is no picture. The name is the tooltip.</summary>
    public static FrameworkElement InputPicture(InputControl? input, string label, double height)
    {
        FrameworkElement picture;
        if (input is { Kind: InputKind.MouseButton } mouse)
        {
            // The outline is too small to read at list size, so the short name (LMB, M4, ...) sits beside the mouse.
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new MouseButtonPicker { Selected = (PointerButton)mouse.Code, Height = height, IsHitTestVisible = false });
            row.Children.Add(new TextBlock { Text = ShortcutKeys.MouseLabel((PointerButton)mouse.Code), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            picture = row;
        }
        else if (input is { } key && KeyPickerWindow.KeyName(key) is { } name && Key(name) is { } art)
            picture = new Image { Source = art, Height = height, Stretch = Stretch.Uniform };
        else return new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        picture.ToolTip = label; picture.HorizontalAlignment = HorizontalAlignment.Center; picture.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(picture, label);
        return picture;
    }

    /// <summary>A picture to click buttons on, or one of its traced button outlines (white on transparent), from
    /// devices/: for example "mouse-buttons" or "controller-layout-a".</summary>
    public static ImageSource? Device(string name) => Load("devices/" + name);
    private static readonly Dictionary<string, (byte[] Pixels, int Width, int Height)?> Maps = [];
    /// <summary>A picture's button map as BGRA pixels (red channel = button value); null when it is missing.</summary>
    public static (byte[] Pixels, int Width, int Height)? DeviceMap(string name)
    {
        if (Maps.TryGetValue(name, out var cached)) return cached;
        (byte[], int, int)? map = null;
        if (Device(name) is BitmapSource source)
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
            bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
            map = (pixels, bgra.PixelWidth, bgra.PixelHeight);
        }
        return Maps[name] = map;
    }

    public static ImageSource? Mouse => Load("devices/mouse");
    public static ImageSource? Keyboard => Load("devices/keyboard");

    /// <summary>Same rule as Get-KeyFileName in scripts/Build-UiArt.ps1.</summary>
    public static string KeyFileName(string name) => name.ToLowerInvariant()
        .Replace("/", "slash").Replace("*", "star").Replace("+", "plus").Replace("-", "minus").Replace(".", "period").Replace(' ', '-');

    private static ImageSource? Load(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        var uri = new Uri($"pack://application:,,,/Assets/Ui/{path}.png", UriKind.Absolute);
        try
        {
            if (Application.GetResourceStream(uri) is { } resource)
            {
                using var stream = resource.Stream;
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
        }
        catch (IOException) { }
        return Cache[path] = image;
    }
}
