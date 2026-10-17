using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MinimumEffortClicker.App;

/// <summary>
/// Loads the pictures built into the app from assets/ui/generated (see <see cref="UiArt"/>), each once. A missing
/// picture is null, so the caller shows its text or a drawn glyph instead. Kept apart from UiArt so the status overlay,
/// which the Windows probe also builds, needs nothing else from the app.
/// </summary>
internal static class UiPictures
{
    private static readonly Dictionary<string, ImageSource?> Cache = [];

    /// <summary>An overlay button: show (the four corners), quit (the X), locked or unlocked (the padlock).</summary>
    public static ImageSource? Overlay(string name) => Load("overlay/" + name);
    public static ImageSource? Lock(bool locked) => Overlay(locked ? "locked" : "unlocked");

    public static ImageSource? Load(string path)
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
