using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using System.Diagnostics;
using System.IO;
using Avalonia.Platform.Storage;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private ScreenshotSettings screenshotSettings = new();
    private bool screenshotBusy;
    private CancellationTokenSource? screenshotCancellation;
    private int screenshotRequestQueued, selectedScreenshotKey;
    private long screenshotFinishedAt = -10000;
    private string? lastScreenshot;

    private void LoadScreenshotSettings(ScreenshotSettings? settings)
    {
        screenshotSettings = settings ?? new();
        screenshotSettings = screenshotSettings with { Folder = screenshotSettings.Folder ?? Path.Combine(store.DirectoryPath, "Screenshots") };
        ScreenshotModeBox.SelectedIndex = screenshotSettings.Mode;
        ScreenshotClipboardBox.IsChecked = screenshotSettings.CopyToClipboard;
        ScreenshotDailyFoldersBox.IsChecked = screenshotSettings.DailyFolders;
        ScreenshotFolderBox.Text = screenshotSettings.Folder;
        selectedScreenshotKey = screenshotSettings.Hotkey;
        ScreenshotKeyBox.Content = selectedScreenshotKey == 0 ? "Choose a key…" : ShortcutKeys.Label(selectedScreenshotKey);
        ScreenshotHotkeyBox.IsChecked = screenshotSettings.HotkeyEnabled;
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
        else ScreenshotHotkeyStatus.Text = screenshotSettings.HotkeyEnabled
            ? $"Ready · {ShortcutKeys.Label(screenshotSettings.Hotkey)} · works while ME Clicker is hidden"
            : "Screenshot hotkey is off. You can still use Take Screenshot.";
        if (!screenshotSettings.HotkeyEnabled && success) ScreenshotHotkeyStatus.Text = "";
        RefreshScreenshotHotkeyButtons();
    }

    private async void CaptureScreenshotKey_Click(object sender, RoutedEventArgs e)
    {
        if (await CaptureShortcut("Screenshot hotkey — choose one keyboard key") is not { } key) return;
        if (!ScreenshotSettings.SupportedKey(key))
        { ScreenshotHotkeyStatus.Text = "Choose a keyboard key. Escape and modifier keys are reserved."; return; }
        selectedScreenshotKey = key;
        ScreenshotKeyBox.Content = ShortcutKeys.Label(key);
        ScreenshotHotkeyBox.IsChecked = true;
        ApplyScreenshotKey_Click(sender, e);
    }

    private void RefreshScreenshotHotkeyButtons()
    {
        bool enabled = screenshotSettings.HotkeyEnabled && screenshotSettings.Hotkey != 0;
        foreach (var button in new[] { ScreenshotKeyBox, ScreenshotHotkeyButton })
        {
            button.Content = enabled ? ShortcutKeys.Label(screenshotSettings.Hotkey) : "Set Hotkey";
            button.SetButtonStyle(enabled ? "BlueButton" : "RedButton");
        }
    }

    private async void ApplyScreenshotKey_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || screenshotBusy || bindingBusy || quickOperation) return;
        if (ScreenshotHotkeyBox.IsChecked == true && !ScreenshotSettings.SupportedKey(selectedScreenshotKey))
        { ScreenshotHotkeyStatus.Text = "Choose a screenshot key first."; return; }
        bindingBusy = true;
        try
        {
            screenshotSettings = screenshotSettings with { Hotkey = selectedScreenshotKey, HotkeyEnabled = ScreenshotHotkeyBox.IsChecked == true };
            await ConfigureScreenshotHotkey(); PersistPreferences();
        }
        catch (Exception ex) { ScreenshotHotkeyStatus.Text = "Could not apply screenshot key: " + ex.Message; }
        finally { bindingBusy = false; }
    }

    private void ScreenshotOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized || screenshotBusy) return;
        screenshotSettings = screenshotSettings with { Mode = ScreenshotModeBox.SelectedIndex, CopyToClipboard = ScreenshotClipboardBox.IsChecked == true,
            DailyFolders = ScreenshotDailyFoldersBox.IsChecked == true };
        PersistPreferences();
    }

    private async void ScreenshotFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (screenshotBusy) return;
        var start = await StorageProvider.TryGetFolderFromPathAsync(screenshotSettings.Folder ?? store.DirectoryPath);
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Save screenshots in", SuggestedStartLocation = start, AllowMultiple = false });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder) return;
        screenshotSettings = screenshotSettings with { Folder = folder };
        ScreenshotFolderBox.Text = folder; PersistPreferences();
    }

    private void ScreenshotOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = ScreenshotFiles.DestinationDirectory(screenshotSettings.Folder ?? Path.Combine(store.DirectoryPath, "Screenshots"), screenshotSettings.DailyFolders, DateTime.Now);
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
            while (screenshotBusy || Environment.TickCount64 - screenshotFinishedAt < 750)
                await Task.Delay(50, actionStop);
            actionStop.ThrowIfCancellationRequested();
            if (runtime is null || !initialized || closing || allowClose)
                throw new InvalidOperationException("Screenshot saving is unavailable while closing.");
        }
        if (runtime is null || !initialized || closing || allowClose || screenshotBusy || Environment.TickCount64 - screenshotFinishedAt < 750) return;
        if ((!fromAction && quickOperation) || quickEditorOpen || bindingBusy || ioBusy || macroFileBusy || DialogWindow.OpenCount > 0)
        {
            ScreenshotStatus.Text = "Finish the open dialog, recording or test before taking a screenshot.";
            if (fromAction) throw new InvalidOperationException(ScreenshotStatus.Text);
            return;
        }
        if (runtime.Control.Helper is not { } helper)
        {
            ScreenshotStatus.Text = "Screenshots need the ME Clicker GNOME helper. Install it in Settings.";
            if (fromAction) throw new InvalidOperationException(ScreenshotStatus.Text);
            return;
        }
        var settings = fromAction ? screenshotSettings with { Mode = 0 } : screenshotSettings;
        string? targetTitle = runtime.SelectedTargetTitle;
        bool wasVisible = IsVisible, reactivate = IsVisible && IsActive;
        bool hidden = false;
        screenshotBusy = true; TakeScreenshotButton.IsEnabled = false;
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
            var frame = await Task.Run(() => DesktopScreenshot.Capture(helper, token), token);
            token.ThrowIfCancellationRequested();
            byte[] pixels = frame.Bgr32Pixels;
            int width = frame.Bounds.Width, height = frame.Bounds.Height;
            if (settings.Mode == 1)
            {
                using var full = Imaging.FromBgra(pixels, width, height);
                var selection = new ScreenshotSelectionWindow(full, frame.Bounds);
                using var registration = token.Register(() => Dispatcher.Post(selection.Cancel));
                if (await selection.ShowAsync() != true || selection.Selection is not { } area) throw new OperationCanceledException();
                token.ThrowIfCancellationRequested();
                pixels = Imaging.Crop(pixels, width, area.X, area.Y, area.Width, area.Height);
                width = area.Width; height = area.Height;
            }
            using var image = Imaging.FromBgra(pixels, width, height);
            string path = await Task.Run(() => ScreenshotFiles.Save(settings.Folder!, stream => image.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default), token, targetTitle, capturedAt, settings.DailyFolders), token);
            // A committed file remains valid even if the clipboard is busy or Stop arrives now.
            string clipboard = "";
            if (settings.CopyToClipboard && !token.IsCancellationRequested && Clipboard is { } board)
            {
                try { await Avalonia.Input.Platform.ClipboardExtensions.SetBitmapAsync(board, image); clipboard = " · copied to clipboard"; }
                catch (Exception ex) when (ex is InvalidOperationException or IOException) { clipboard = " · clipboard busy; file saved"; }
            }
            lastScreenshot = path; ScreenshotLastFile.Text = path; ScreenshotOpenFileButton.IsEnabled = true;
            ScreenshotStatus.Text = $"Saved {width} × {height} PNG{clipboard}.";
            try
            {
                // Avoid repeatedly decoding previews during screenshot loops.
                var previous = ScreenshotPreview.Source as IDisposable;
                ScreenshotPreview.Source = fromAction ? null : await Task.Run(() =>
                {
                    using var file = File.OpenRead(path);
                    return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(file, Math.Min(480, width));
                });
                previous?.Dispose();
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
            screenshotCancellation = null; screenshotBusy = false; screenshotFinishedAt = Environment.TickCount64;
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
