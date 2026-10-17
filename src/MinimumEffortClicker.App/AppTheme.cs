using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using MinimumEffortClicker.Windows;
using Microsoft.Win32;

namespace MinimumEffortClicker.App;

public enum Appearance { System, Dark, Light }

public static class AppTheme
{
    private static (bool Light, bool HighContrast, Color Window, Color Text, Color Highlight)? applied;
    /// <summary>Whether the light colours are showing now (also when Appearance follows Windows).</summary>
    public static bool IsLight => applied?.Light == true;
    public static void Apply(Appearance appearance)
    {
        bool light = appearance == Appearance.Light;
        if (appearance == Appearance.System)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        bool highContrast = SystemParameters.HighContrast;
        var resolved = (light, highContrast, highContrast ? SystemColors.WindowColor : default,
            highContrast ? SystemColors.WindowTextColor : default, highContrast ? SystemColors.HighlightColor : default);
        if (applied == resolved) return;
        var pairs = new Dictionary<string, (string Dark, string Light)>
        {
            ["BrandPrimary"] = ("#ECE7DC", "#111010"), ["BrandSecondary"] = ("#A39E93", "#5D594F"),
            ["AppBackground"] = ("#111010", "#ECE7DC"), ["Surface"] = ("#1A1918", "#F8F5EE"),
            ["RaisedSurface"] = ("#2C2A28", "#FFFFFF"), ["Border"] = ("#A39E93", "#777268"),
            ["TextPrimary"] = ("#ECE7DC", "#111010"), ["TextSecondary"] = ("#A39E93", "#5D594F"),
            ["FocusRing"] = ("#ECE7DC", "#111010"), ["Running"] = ("#8FBC8F", "#2B6132"),
            ["Paused"] = ("#CDB97A", "#765411"), ["Recording"] = ("#D7ABE8", "#763D88"),
            ["Error"] = ("#FFA8A8", "#A52323"), ["Disabled"] = ("#A39E93", "#5D594F"),
            // Functional UI accents requested by the owner; not brand palette claims.
            ["SuccessFill"] = ("#246B39", "#246B39"), ["SuccessBorder"] = ("#73BE83", "#28713B"),
            ["ActionFill"] = ("#245FA5", "#245FA5"), ["ActionBorder"] = ("#6CA9F2", "#2566B3"),
            ["StopFill"] = ("#AD3039", "#AD3039"), ["StopBorder"] = ("#EF8080", "#B2313B"),
            ["ListBorder"] = ("#D8B94E", "#94700B"), ["ColourButtonText"] = ("#FFFFFF", "#FFFFFF")
        };
        foreach (var (name, values) in pairs)
        {
            Brush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? values.Light : values.Dark));
            if (highContrast)
                brush = name is "AppBackground" or "Surface" or "RaisedSurface" or "SuccessFill" or "ActionFill" or "StopFill" ? SystemColors.WindowBrush
                    : name == "FocusRing" ? SystemColors.HighlightBrush : SystemColors.WindowTextBrush;
            brush.Freeze();
            Application.Current.Resources[name] = brush;
        }
        applied = resolved;
        foreach (Window window in Application.Current.Windows) ApplyWindowChrome(window);
    }

    public static void ApplyWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || window.WindowStyle == WindowStyle.None || applied is not { } theme) return;
        uint caption = uint.MaxValue, text = uint.MaxValue;
        if (!theme.HighContrast)
        {
            caption = ColourRef(((SolidColorBrush)Application.Current.Resources["AppBackground"]).Color);
            text = ColourRef(((SolidColorBrush)Application.Current.Resources["TextPrimary"]).Color);
        }
        // Unsupported DWM attributes leave Windows' ordinary caption available.
        WindowChromeTheme.Apply(handle, !theme.Light && !theme.HighContrast, caption, text);
    }

    private static uint ColourRef(Color colour) => (uint)(colour.R | colour.G << 8 | colour.B << 16);
}
