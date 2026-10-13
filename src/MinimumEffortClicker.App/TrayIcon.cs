namespace MinimumEffortClicker.App;

/// <summary>Notification-area UI only. No scheduler, hooks, or background polling.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly System.Drawing.Icon artwork;
    private readonly System.Windows.Forms.ContextMenuStrip menu = new();
    private readonly System.Windows.Forms.NotifyIcon icon = new();

    public TrayIcon(Action open, Action stop, Action exit)
    {
        using var stream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/App.ico")).Stream;
        artwork = new System.Drawing.Icon(stream, 32, 32);
        try
        {
            menu.Items.Add("Open ME Clicker", null, (_, _) => open());
            menu.Items.Add("Stop all", null, (_, _) => stop());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => exit());
            icon.Icon = artwork;
            icon.Text = "Minimum Effort Clicker";
            icon.ContextMenuStrip = menu;
            icon.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) open(); };
            icon.Visible = true;
        }
        catch { Dispose(); throw; }
    }

    public void Notify(string title, string message) => icon.ShowBalloonTip(4000, title,
        message.Length > 250 ? message[..250] + "…" : message, System.Windows.Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        artwork.Dispose();
    }
}
