namespace MinimumEffortClicker.Core;

/// <summary>
/// Tells a Steam Deck from the firmware's maker and product name, which Linux reads from /sys/class/dmi/id and Windows
/// from the registry's BIOS key: Valve's "Jupiter" is the LCD Deck and "Galileo" the OLED one.
/// </summary>
public static class SteamDeckHardware
{
    public static bool Is(string? vendor, string? product) => vendor?.Trim() == "Valve" && product?.Trim() is "Jupiter" or "Galileo";
}
