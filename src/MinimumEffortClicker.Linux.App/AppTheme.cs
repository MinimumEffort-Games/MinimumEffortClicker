using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace MinimumEffortClicker.App;

public enum Appearance { System, Dark, Light }

/// <summary>The Windows build's palette, applied as application brushes plus the matching Fluent variant.</summary>
public static class AppTheme
{
    private static bool? applied;
    public static void Apply(Appearance appearance)
    {
        var application = Application.Current!;
        bool light = appearance == Appearance.Light
            || appearance == Appearance.System && application.PlatformSettings?.GetColorValues().ThemeVariant != PlatformThemeVariant.Dark;
        if (applied == light) return;
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
            application.Resources[name] = new SolidColorBrush(Color.Parse(light ? values.Light : values.Dark)).ToImmutable();
        application.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        applied = light;
    }
}
