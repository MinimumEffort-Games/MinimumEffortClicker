using System.IO;
using System.Windows;
using Microsoft.Win32;
using MinimumEffortClicker.Storage;

namespace MinimumEffortClicker.App;

/// <summary>Asks what to share a macro as. It only collects the text; the upload happens after it closes.</summary>
public partial class WorkshopShareWindow : Window
{
    // Steam refuses preview images over 1 MB.
    private const long PreviewLimitBytes = 1024 * 1024;
    public string ShareTitle => TitleBox.Text.Trim();
    public string ShareDescription => DescriptionBox.Text.Trim();
    public WorkshopVisibility ShareVisibility => (WorkshopVisibility)Math.Max(0, VisibilityBox.SelectedIndex);
    /// <summary>The image the user chose, or null to keep the item's current preview (the app logo for a new item).</summary>
    public string? PreviewImage { get; private set; }

    public WorkshopShareWindow(string macroName, WorkshopShare? previous, bool holdsScreenImages)
    {
        InitializeComponent();
        TitleBox.Text = previous?.Title ?? macroName;
        DescriptionBox.Text = previous?.Description ?? "";
        VisibilityBox.SelectedIndex = (int)(previous?.Visibility ?? WorkshopVisibility.Public);
        ImageWarning.Visibility = holdsScreenImages ? Visibility.Visible : Visibility.Collapsed;
        if (previous is not null) { HeadingText.Text = "Update your Workshop item"; ShareButton.Content = "Update"; }
        PreviewText.Text = previous is null ? "Default: the Minimum Effort logo" : "Keeps the current preview";
        Loaded += (_, _) => { TitleBox.Focus(); TitleBox.SelectAll(); };
    }

    private void ChoosePreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Preview image", Filter = "Images (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif" };
        if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > PreviewLimitBytes) { PreviewText.Text = "That image is over 1 MB; Steam refuses it. Choose a smaller one."; return; }
        PreviewImage = dialog.FileName; PreviewText.Text = Path.GetFileName(dialog.FileName);
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
        if (ShareTitle.Length == 0) { TitleBox.Focus(); return; }
        DialogResult = true;
    }
}
