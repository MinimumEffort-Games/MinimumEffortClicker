using System.IO;
using System.Runtime.CompilerServices;
using MinimumEffortClicker.Steam;
using MinimumEffortClicker.Storage;

namespace MinimumEffortClicker.App;

/// <summary>
/// The Steam build is the same app with steam.mode beside the exe (written by scripts/Build-Steam.ps1). Without it, as
/// in the GitHub package, nothing here touches Steam: the Steam types sit behind methods that are never called.
/// </summary>
internal static class SteamMode
{
    public const string MarkerFile = "steam.mode";
    public static bool Enabled { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, MarkerFile));

    /// <summary>True when Steam is starting the app itself and this copy must exit.</summary>
    public static bool RestartThroughSteam() => Enabled && Restart();

    /// <summary>The Workshop for the Steam build; null in every other build.</summary>
    public static IWorkshopClient? StartWorkshop() => Enabled ? Start() : null;

    // Kept out of line so the Steam assembly is only loaded when the marker is present.
    [MethodImpl(MethodImplOptions.NoInlining)] private static bool Restart() => SteamWorkshopClient.RestartThroughSteam();
    [MethodImpl(MethodImplOptions.NoInlining)] private static IWorkshopClient Start() => SteamWorkshopClient.Start();
}
