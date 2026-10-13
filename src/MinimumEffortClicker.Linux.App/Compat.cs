using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace MinimumEffortClicker.App;

// The Linux window code is ported from the WPF app. These small equivalents keep the ported
// code close to the Windows original, so fixes can be carried between the two by eye.
public enum Visibility { Visible, Hidden, Collapsed }
public enum MessageBoxButton { OK, OKCancel, YesNoCancel, YesNo }
public enum MessageBoxResult { None, OK, Cancel, Yes, No }
public enum MessageBoxImage { None, Information, Question, Warning, Error }

internal static class Compat
{
    private static bool shutdownStarted;
    public static void TrackShutdown() => Dispatcher.UIThread.ShutdownStarted += (_, _) => shutdownStarted = true;

    extension(Visual visual)
    {
        public Visibility Visibility
        {
            get => visual.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            set => visual.IsVisible = value == Visibility.Visible;
        }
        public double ActualWidth => visual.Bounds.Width;
        public double ActualHeight => visual.Bounds.Height;
    }

    extension(Control control)
    {
        public object? ToolTip
        {
            get => Avalonia.Controls.ToolTip.GetTip(control);
            set => Avalonia.Controls.ToolTip.SetTip(control, value);
        }
        /// <summary>Follows a theme brush, like WPF's SetResourceReference.</summary>
        public void SetResourceReference(AvaloniaProperty property, string key)
            => control.Bind(property, control.GetResourceObservable(key), Avalonia.Data.BindingPriority.LocalValue);
        /// <summary>The WPF build swaps named button styles; here the same names map to style classes.</summary>
        public void SetButtonStyle(string key)
        {
            control.Classes.Set("green", key == "GreenButton");
            control.Classes.Set("blue", key == "BlueButton");
            control.Classes.Set("red", key == "RedButton");
        }
    }

    extension(Dispatcher dispatcher)
    {
        public bool HasShutdownStarted => shutdownStarted;
        public void BeginInvoke(Action action) => dispatcher.Post(action);
        public void BeginInvoke(Func<Task> action) => dispatcher.Post(() => _ = action());
    }
}

/// <summary>
/// A modal window with WPF-style DialogResult. ShowDialog runs a nested dispatcher frame so the
/// ported code can keep reading the result synchronously, exactly like the Windows build.
/// </summary>
public class DialogWindow : Window
{
    private bool? result;
    /// <summary>Modal dialogs currently open; the WPF build asks ComponentDispatcher.IsThreadModal.</summary>
    public static int OpenCount { get; private set; }
    public Window? OwnerWindow { get; set; }
    public bool? DialogResult
    {
        get => result;
        set { result = value; Close(value); }
    }

    public DialogWindow()
    {
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; DialogResult = false; } };
    }

    public bool? ShowDialog()
    {
        OpenCount++;
        try { return ShowModal(); }
        finally { OpenCount--; }
    }

    private bool? ShowModal()
    {
        var owner = OwnerWindow ?? (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null || !owner.IsVisible) return ShowStandalone();
        var task = ShowDialog<bool?>(owner);
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.UIThread.PushFrame(frame);
        return task.IsCompletedSuccessfully ? task.Result ?? result : result;
    }

    // A hidden owner (tray mode) cannot parent a dialog; show it on its own and wait the same way.
    private bool? ShowStandalone()
    {
        var frame = new DispatcherFrame();
        Closed += (_, _) => frame.Continue = false;
        Show();
        Activate();
        Dispatcher.UIThread.PushFrame(frame);
        return result;
    }

    /// <summary>
    /// Shows the window without a nested dispatcher frame and completes when it closes. Use this for
    /// pointer-driven windows: Avalonia's X11 backend can hold back pointer motion during a nested frame.
    /// </summary>
    public Task<bool?> ShowAsync()
    {
        var closed = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => closed.TrySetResult(result);
        OpenCount++;
        closed.Task.ContinueWith(_ => Dispatcher.UIThread.Post(() => OpenCount--), TaskScheduler.Default);
        Show();
        Activate();
        return closed.Task;
    }

    /// <summary>A Cancel button that closes this dialog with a false result, like WPF's IsCancel.</summary>
    public Button CancelButton(string label = "Cancel")
    {
        var button = new Button { Content = label, IsCancel = true };
        button.Click += (_, _) => DialogResult = false;
        return button;
    }
}
