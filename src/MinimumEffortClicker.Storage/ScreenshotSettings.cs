using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Storage;

public sealed record ScreenshotSettings(int Mode = 1, bool CopyToClipboard = false, string? Folder = null,
    int Hotkey = 0, bool HotkeyEnabled = false, bool DailyFolders = true)
{
    public static bool SupportedKey(int key) => ShortcutKeys.Supported(key) && !ShortcutKeys.IsMouse(key)
        && key != 27 && key is not (>= 0xA0 and <= 0xA5);
    public bool Valid => Mode is 0 or 1 && (Folder is null || Folder.Length <= 1024 && Path.IsPathFullyQualified(Folder))
        && (Hotkey == 0 || SupportedKey(Hotkey)) && (!HotkeyEnabled || Hotkey != 0);
}
