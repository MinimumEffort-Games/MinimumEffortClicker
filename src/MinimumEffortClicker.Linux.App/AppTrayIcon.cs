using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

/// <summary>Top-bar (StatusNotifierItem) icon. No scheduler, hooks, or background polling.</summary>
internal sealed class AppTrayIcon : IDisposable
{
    private readonly TrayIcon icon;

    public AppTrayIcon(Action open, Action stop, Action exit)
    {
        var menu = new NativeMenu();
        void Add(string header, Action action) { var item = new NativeMenuItem(header); item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Open ME Clicker", open);
        Add("Stop all", stop);
        menu.Items.Add(new NativeMenuItemSeparator());
        Add("Exit", exit);
        using var artwork = AssetLoader.Open(new Uri("avares://MinimumEffortClicker/Assets/App.png"));
        icon = new TrayIcon { Icon = new WindowIcon(artwork), ToolTipText = "Minimum Effort Clicker", Menu = menu, IsVisible = true };
        icon.Clicked += (_, _) => open();
        TrayIcon.SetIcons(Application.Current!, [icon]);
    }

    public void Notify(string title, string message) => ThreadPool.QueueUserWorkItem(_ => DesktopNotifications.Show(title, message));

    public void Dispose()
    {
        icon.IsVisible = false;
        TrayIcon.SetIcons(Application.Current!, []);
        icon.Dispose();
    }
}
