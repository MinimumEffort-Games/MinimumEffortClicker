using System.Diagnostics;
using System.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private void BrandWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (AppDialog.Show(this, "Open www.minimumeffort.games in your browser?", "Minimum Effort website",
            MessageBoxButton.OKCancel, defaultResult: MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        try { Process.Start(new ProcessStartInfo("https://www.minimumeffort.games") { UseShellExecute = true }); }
        catch (Exception ex) { AppDialog.Show(this, "Could not open your browser: " + ex.Message); }
    }
}
