using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private ScreenshotSettings screenshotSettings = new();
    private CancellationTokenSource? screenshotCancellation;
    private int screenshotRequestQueued, selectedScreenshotKey;
    private long screenshotFinishedAt = -10000;
    private string? lastScreenshot;

    private string DefaultScreenshotFolder => Path.Combine(store.DirectoryPath, "Screenshots");
    /// <summary>Where screenshots go: the folder the user chose, or the default.</summary>
    private string ScreenshotFolder => screenshotSettings.Folder ?? DefaultScreenshotFolder;

    private void LoadScreenshotSettings(ScreenshotSettings? settings)
    {
        screenshotSettings = settings ?? new();
        // F7 is the screenshot key out of the box. Older versions started without one (0), and Reset keybinds cleared
        // it; a missing key now gets F7 too. A key the user chose stays.
        if (screenshotSettings.Hotkey == 0) screenshotSettings = screenshotSettings with { Hotkey = ShortcutKeys.DefaultScreenshotKey, HotkeyEnabled = true };
        // The default folder is not saved, so it stays the Screenshots folder beside this copy's data, also when the
        // data is moved or copied. Settings that saved it in full are read back as the default when they point here.
        if (screenshotSettings.Folder is { } saved && string.Equals(Path.GetFullPath(saved), DefaultScreenshotFolder, StringComparison.OrdinalIgnoreCase))
            screenshotSettings = screenshotSettings with { Folder = null };
        ScreenshotModeBox.SelectedIndex = screenshotSettings.Mode;
        ScreenshotClipboardBox.IsChecked = screenshotSettings.CopyToClipboard;
        ScreenshotDailyFoldersBox.IsChecked = screenshotSettings.DailyFolders;
        ScreenshotFolderBox.Text = ScreenshotFolder;
        selectedScreenshotKey = screenshotSettings.Hotkey;
        ScreenshotHotkeyBox.IsChecked = screenshotSettings.HotkeyEnabled;
        RefreshScreenshotHotkeyButtons();
    }

    private async Task ConfigureScreenshotHotkey()
    {
        if (runtime is null) return;
        bool success = await runtime.Control.ConfigureScreenshotAsync(screenshotSettings.HotkeyEnabled ? screenshotSettings.Hotkey : null);
        if (!success)
        {
            screenshotSettings = screenshotSettings with { HotkeyEnabled = false };
            ScreenshotHotkeyBox.IsChecked = false;
            ScreenshotHotkeyStatus.Text = "That key is already used or unavailable. Choose another screenshot key.";
            PersistPreferences();
        }
        // Only a problem is worth a line here; a working key shows on its button.
        else ScreenshotHotkeyStatus.Text = "";
        RefreshScreenshotHotkeyButtons();
    }

    private async void CaptureScreenshotKey_Click(object sender, RoutedEventArgs e)
    {
        if (await CaptureShortcut("Screenshot hotkey — choose one keyboard key") is not { } key) return;
        if (!ScreenshotSettings.SupportedKey(key))
        { ScreenshotHotkeyStatus.Text = "Choose a keyboard key. Escape and modifier keys are reserved."; return; }
        selectedScreenshotKey = key;
        ScreenshotHotkeyBox.IsChecked = true;
        ApplyScreenshotKey_Click(sender, e);
    }

    /// <summary>The screenshot key on its Settings button (like the other keys) and on Take Screenshot.</summary>
    private void RefreshScreenshotHotkeyButtons()
    {
        bool enabled = screenshotSettings.HotkeyEnabled && screenshotSettings.Hotkey != 0;
        ScreenshotKeyBox.Content = enabled ? ShortcutKeys.Label(screenshotSettings.Hotkey) : "Choose a key…";
        TakeScreenshotButton.Content = enabled ? $"Take Screenshot {ShortcutKeys.Label(screenshotSettings.Hotkey)}" : "Take Screenshot";
    }

    private async void ApplyScreenshotKey_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || !operations.Idle) return;
        if (ScreenshotHotkeyBox.IsChecked == true && !ScreenshotSettings.SupportedKey(selectedScreenshotKey))
        { ScreenshotHotkeyStatus.Text = "Choose a screenshot key first."; return; }
        operations.Begin(Operation.Binding);
        try
        {
            screenshotSettings = screenshotSettings with { Hotkey = selectedScreenshotKey, HotkeyEnabled = ScreenshotHotkeyBox.IsChecked == true };
            await ConfigureScreenshotHotkey(); PersistPreferences();
        }
        catch (Exception ex) { ScreenshotHotkeyStatus.Text = "Could not apply screenshot key: " + ex.Message; }
        finally { operations.End(Operation.Binding); }
    }

    private void ScreenshotOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized || operations.Has(Operation.Screenshot)) return;
        screenshotSettings = screenshotSettings with { Mode = ScreenshotModeBox.SelectedIndex, CopyToClipboard = ScreenshotClipboardBox.IsChecked == true,
            DailyFolders = ScreenshotDailyFoldersBox.IsChecked == true };
        PersistPreferences();
    }

    private void ScreenshotFolder_Click(object sender, RoutedEventArgs e)
    {
        if (operations.Has(Operation.Screenshot)) return;
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Save screenshots in", InitialDirectory = ScreenshotFolder };
        if (picker.ShowDialog(this) != true) return;
        screenshotSettings = screenshotSettings with { Folder = picker.FolderName };
        ScreenshotFolderBox.Text = picker.FolderName; PersistPreferences();
    }


    private void ScreenshotOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = ScreenshotFiles.DestinationDirectory(ScreenshotFolder, screenshotSettings.DailyFolders, DateTime.Now);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) { ScreenshotStatus.Text = "Could not open folder: " + ex.Message; }
    }

    private void ScreenshotOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (lastScreenshot is null) return;
        try { Process.Start(new ProcessStartInfo(lastScreenshot) { UseShellExecute = true }); }
        catch (Exception ex) { ScreenshotStatus.Text = "Could not open screenshot: " + ex.Message; }
    }

    private void CancelScreenshot(string reason)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => screenshotCancellation?.Cancel());
    }

    private void ScreenshotHotkey()
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (Interlocked.Exchange(ref screenshotRequestQueued, 1) != 0) return;
        Dispatcher.BeginInvoke(async () =>
        {
            try { await TakeScreenshotAsync(); }
            finally { Volatile.Write(ref screenshotRequestQueued, 0); }
        });
    }
    private async void TakeScreenshot_Click(object sender, RoutedEventArgs e) => await TakeScreenshotAsync();

    private async Task TakeScreenshotAsync(bool fromAction = false, CancellationToken actionStop = default)
    {
        if (fromAction)
        {
            // A manual capture owns the same buffers/write slot. Macro actions wait, never drop.
            while (operations.Has(Operation.Screenshot) || Environment.TickCount64 - screenshotFinishedAt < 750)
                await Task.Delay(50, actionStop);
            actionStop.ThrowIfCancellationRequested();
            if (runtime is null || !initialized || closing || allowClose)
                throw new InvalidOperationException("Screenshot saving is unavailable while closing.");
        }
        if (runtime is null || !initialized || closing || allowClose || operations.Has(Operation.Screenshot) || Environment.TickCount64 - screenshotFinishedAt < 750) return;
        if ((!fromAction && operations.Has(Operation.Quick)) || operations.Has(Operation.Dialog) || operations.Any(Operation.Binding | Operation.Library) || operations.Has(Operation.MacroFile) || System.Windows.Interop.ComponentDispatcher.IsThreadModal
            || OwnedWindows.Cast<Window>().Any(w => w.IsVisible))
        {
            ScreenshotStatus.Text = "Finish the open dialog, recording, test, or overlay adjustment before taking a screenshot.";
            if (fromAction) throw new InvalidOperationException(ScreenshotStatus.Text);
            return;
        }
        var settings = fromAction ? screenshotSettings with { Mode = 0 } : screenshotSettings;
        string? targetTitle = runtime.SelectedTargetTitle;
        bool wasVisible = IsVisible, reactivate = IsVisible && ProbeNative.GetForegroundWindow() == new System.Windows.Interop.WindowInteropHelper(this).Handle;
        bool hidden = false;
        operations.Begin(Operation.Screenshot); TakeScreenshotButton.IsEnabled = false;
        ScreenshotStatus.Text = settings.Mode == 1 ? "Preparing area selection…" : "Taking screenshot…";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(actionStop); screenshotCancellation = cancellation;
        var token = cancellation.Token;
        try
        {
            if (settings.Mode == 1)
            {
                runtime.Stop("Stopped before selecting a screenshot area.");
                for (int i = 0; i < 80 && runtime.Snapshot.Busy; i++) await Task.Delay(25, token);
                if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) throw new InvalidOperationException("Finish input cleanup before selecting an area.");
            }
            token.ThrowIfCancellationRequested();
            if (wasVisible) { Hide(); hidden = true; }
            overlay?.Hide(); overlayFollowTimer.Stop();
            await Task.Delay(350, token); // Allow desktop hide animations to finish before copying pixels.
            var capturedAt = DateTime.Now;
            var frame = await Task.Run(() => DesktopScreenshot.Capture(token), token);
            token.ThrowIfCancellationRequested();
            BitmapSource image = BitmapSource.Create(frame.Bounds.Width, frame.Bounds.Height, 96, 96, PixelFormats.Bgr32, null, frame.Bgr32Pixels, frame.Bounds.Width * 4);
            image.Freeze();
            if (settings.Mode == 1)
            {
                var selection = new ScreenshotSelectionWindow(image, frame.Bounds);
                using var registration = token.Register(() => Dispatcher.BeginInvoke(selection.Cancel));
                if (selection.ShowDialog() != true || selection.Selection is not { } area) throw new OperationCanceledException();
                token.ThrowIfCancellationRequested();
                image = new CroppedBitmap(image, new Int32Rect(area.X, area.Y, area.Width, area.Height)); image.Freeze();
            }
            string path = await Task.Run(() => ScreenshotFiles.Save(settings.Folder ?? DefaultScreenshotFolder, stream =>
            {
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(stream);
            }, token, targetTitle, capturedAt, settings.DailyFolders), token);
            // A committed file remains valid even if the clipboard is busy or Stop arrives now.
            string clipboard = "";
            if (settings.CopyToClipboard && !token.IsCancellationRequested)
            {
                try { Clipboard.SetImage(image); clipboard = " · copied to clipboard"; }
                catch (ExternalException) { clipboard = " · clipboard busy; file saved"; }
            }
            lastScreenshot = path; ScreenshotLastFile.Text = path; ScreenshotOpenFileButton.IsEnabled = true;
            ScreenshotStatus.Text = $"Saved {image.PixelWidth} × {image.PixelHeight} PNG{clipboard}.";
            try
            {
                // Avoid repeatedly decoding previews during screenshot loops.
                ScreenshotPreview.Source = fromAction ? null : await Task.Run(() =>
                {
                    var preview = new BitmapImage(); preview.BeginInit(); preview.CacheOption = BitmapCacheOption.OnLoad;
                    preview.DecodePixelWidth = Math.Min(480, image.PixelWidth); preview.UriSource = new Uri(path); preview.EndInit(); preview.Freeze(); return preview;
                });
            }
            catch (Exception) { ScreenshotPreview.Source = null; ScreenshotStatus.Text += " Preview unavailable; the file was saved."; }
            if (!wasVisible && !fromAction) trayIcon?.Notify("Screenshot saved", Path.GetFileName(path) + clipboard);
        }
        catch (OperationCanceledException) { ScreenshotStatus.Text = "Screenshot cancelled. Any completed image is kept."; if (fromAction) throw; }
        catch (Exception ex)
        {
            ScreenshotStatus.Text = "Screenshot failed: " + ex.Message;
            AppLog.Failure("Screenshot", ex);
            if (!wasVisible) trayIcon?.Notify("Screenshot could not finish", ex.Message);
            if (fromAction) throw new IOException(ScreenshotStatus.Text, ex);
        }
        finally
        {
            screenshotCancellation = null; operations.End(Operation.Screenshot); screenshotFinishedAt = Environment.TickCount64;
            TakeScreenshotButton.IsEnabled = true;
            if (hidden && !closing && !allowClose && !exitRequested)
            {
                bool activate = ShowActivated;
                ShowActivated = reactivate; Show(); ShowActivated = activate;
                if (reactivate) Activate();
            }
            RefreshStatus();
        }
    }
}
