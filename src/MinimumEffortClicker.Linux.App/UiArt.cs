using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// The app's pictures: keycaps, controller buttons, action types and the mouse and keyboard. They are cut from the
/// art sheets by scripts/Build-UiArt.ps1 and built into the app. A picture that is missing returns null, so the caller
/// shows its text instead and the app keeps working.
/// </summary>
internal static class UiArt
{
    private static readonly Dictionary<string, Bitmap?> Cache = [];

    /// <summary>The keycap for a key name as the app lists it (SequenceKeys), or null for keys without one (F13–F24).</summary>
    public static Bitmap? Key(string name) => Load("keys/" + KeyFileName(name));

    /// <summary>A controller picture by its file name, for example "a", "lb" or "dpad-up".</summary>
    public static Bitmap? Controller(string name) => Load("controller/" + name);

    /// <summary>An action type: click, key-press, pause, wait-until, fullscreen-screenshot or scroll.</summary>
    public static Bitmap? Action(string name) => Load("actions/" + name);

    /// <summary>
    /// A controller button as a picture of the given height: its own icon, or for a stick direction the stick with an
    /// arrow keycap. Buttons without a picture (the Steam Deck back buttons and the touchpad) are their name as text.
    /// </summary>
    public static Control ControllerGlyph(ControllerButton button, double height)
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
            return new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        ToolTip.SetTip(row, label);
        Avalonia.Automation.AutomationProperties.SetName(row, label);
        row.Children.Add(Picture(picture, height));
        if (arrow is not null && Key(arrow) is { } keycap)
        {
            var image = Picture(keycap, height); image.Margin = new Thickness(2, 0, 0, 0);
            row.Children.Add(image);
        }
        return row;
    }

    /// <summary>What a button that shows a chosen key says: the keycap next to its name, or only the name for mouse
    /// buttons and keys without a keycap.</summary>
    public static object InputContent(InputControl? input, string label)
    {
        string? name = input is { } i ? KeyPickerWindow.KeyName(i) : null;
        if (name is null || Key(name) is not { } art) return label;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var image = Picture(art, 30); image.Margin = new Thickness(0, 0, 8, 0);
        content.Children.Add(image);
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    /// <summary>A key or mouse button as a small picture for a list: its keycap, or the labelled mouse with that button
    /// outlined and its short name beside it, or its name as text when there is no picture. The name is the tooltip.</summary>
    public static Control InputPicture(InputControl? input, string label, double height)
    {
        Control picture;
        if (input is { Kind: InputKind.MouseButton } mouse)
        {
            // The outline is too small to read at list size, so the short name (LMB, M4, ...) sits beside the mouse.
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new MouseButtonPicker { Selected = (PointerButton)mouse.Code, Height = height, IsHitTestVisible = false });
            row.Children.Add(new TextBlock { Text = ShortcutKeys.MouseLabel((PointerButton)mouse.Code), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            picture = row;
        }
        else if (input is { } key && KeyPickerWindow.KeyName(key) is { } name && Key(name) is { } art)
            picture = Picture(art, height);
        else return new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(picture, label); picture.HorizontalAlignment = HorizontalAlignment.Center; picture.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(picture, label);
        return picture;
    }

    /// <summary>A picture to click buttons on, or one of its traced button outlines (white on transparent), from
    /// devices/: for example "mouse-buttons" or "controller-layout-a".</summary>
    public static Bitmap? Device(string name) => Load("devices/" + name);

    private static readonly Dictionary<string, (byte[] Pixels, int Width, int Height)?> Maps = [];
    /// <summary>A picture's button map with the red channel of each pixel in a byte per pixel; null when it is missing.</summary>
    public static (byte[] Pixels, int Width, int Height)? DeviceMap(string name)
    {
        if (Maps.TryGetValue(name, out var cached)) return cached;
        (byte[], int, int)? map = null;
        var uri = Uri("devices/" + name);
        if (AssetLoader.Exists(uri))
        {
            using var stream = AssetLoader.Open(uri);
            using var bitmap = WriteableBitmap.Decode(stream);
            using var buffer = bitmap.Lock();
            var row = new byte[buffer.RowBytes];
            var red = new byte[buffer.Size.Width * buffer.Size.Height];
            // The map's colours are opaque or fully clear, so premultiplied or not makes no difference here.
            int redOffset = buffer.Format == PixelFormat.Rgba8888 ? 0 : 2;
            for (int y = 0; y < buffer.Size.Height; y++)
            {
                Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, row.Length);
                for (int x = 0; x < buffer.Size.Width; x++)
                    red[y * buffer.Size.Width + x] = row[x * 4 + 3] < 128 ? (byte)0 : row[x * 4 + redOffset];
            }
            map = (red, buffer.Size.Width, buffer.Size.Height);
        }
        return Maps[name] = map;
    }

    public static Bitmap? Mouse => Load("devices/mouse");
    public static Bitmap? Keyboard => Load("devices/keyboard");

    /// <summary>Same rule as Get-KeyFileName in scripts/Build-UiArt.ps1.</summary>
    public static string KeyFileName(string name) => name.ToLowerInvariant()
        .Replace("/", "slash").Replace("*", "star").Replace("+", "plus").Replace("-", "minus").Replace(".", "period").Replace(' ', '-');

    /// <summary>A picture scaled smoothly to a height.</summary>
    public static Image Picture(IImage source, double height)
    {
        var image = new Image { Source = source, Height = height, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        return image;
    }

    private static Uri Uri(string path) => new($"avares://MinimumEffortClicker/Assets/Ui/{path}.png");

    private static Bitmap? Load(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        Bitmap? image = null;
        var uri = Uri(path);
        try { if (AssetLoader.Exists(uri)) { using var stream = AssetLoader.Open(uri); image = new Bitmap(stream); } }
        catch (Exception ex) when (ex is IOException or ArgumentException) { }
        return Cache[path] = image;
    }
}
