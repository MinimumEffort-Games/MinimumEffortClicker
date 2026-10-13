using System.Windows;

namespace MinimumEffortClicker.App;

public partial class TrayNoticeWindow : Window
{
    public bool DisableRequested { get; private set; }
    public TrayNoticeWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => OkayButton.Focus();
    }

    private void Okay_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Disable_Click(object sender, RoutedEventArgs e)
    {
        DisableRequested = true;
        DialogResult = false;
    }
}
