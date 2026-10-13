using System.Runtime.InteropServices;

namespace MinimumEffortClicker.Windows;

/// <summary>Colours our native Windows 11 caption without replacing its controls.</summary>
public static class WindowChromeTheme
{
    public static bool Apply(nint window, bool dark, uint caption, uint text)
    {
        uint immersive = dark ? 1u : 0u;
        int modeResult = DwmSetWindowAttribute(window, 20, ref immersive, sizeof(uint));
        int captionResult = DwmSetWindowAttribute(window, 35, ref caption, sizeof(uint));
        int textResult = DwmSetWindowAttribute(window, 36, ref text, sizeof(uint));
        return modeResult >= 0 && captionResult >= 0 && textResult >= 0;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);
}
