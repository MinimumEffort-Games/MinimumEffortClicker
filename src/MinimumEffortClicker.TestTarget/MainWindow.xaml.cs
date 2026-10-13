using System.Windows;
using System.Windows.Input;

namespace MinimumEffortClicker.TestTarget;

public partial class MainWindow : Window
{
    private readonly InputMonitor monitor = new(deferViewUpdates: true);
    private readonly System.Windows.Threading.DispatcherTimer viewRefresh = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string receivedText = "";
    private void Area_TextInput(object sender, TextCompositionEventArgs e)
    {
        receivedText += e.Text;
        if (receivedText.Length > 256) receivedText = receivedText[^256..];
        ReceivedText.Text = "Text received here (latest 256 units, memory only): " + receivedText;
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = monitor;
        InputArea.MouseMove += (_, e) => { var p = e.GetPosition(InputArea); monitor.PointerMoved((int)p.X, (int)p.Y); };
        monitor.ViewInvalidated += () => viewRefresh.Start();
        viewRefresh.Tick += (_, _) => { viewRefresh.Stop(); monitor.RefreshView(); };
        Closed += (_, _) => viewRefresh.Stop();
        SourceInitialized += (_, _) => System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(ReceiveHorizontalWheel);
    }
    // Explicit opt-in harmless receiver covers recorded Desktop points, including the taskbar.
    // It receives input only; no synthetic input, commands, file writes or network activity.
    internal void EnableDesktopReplaySurface()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        if (Content is System.Windows.Controls.Grid root) root.Children.Remove(InputArea);
        var text = new System.Windows.Controls.StackPanel();
        text.Children.Add(new System.Windows.Controls.TextBlock { Text = "DESKTOP REPLAY RECEIVER — F9 stops playback · Alt+F4 closes receiver", FontSize = 22 });
        var count = new System.Windows.Controls.TextBlock { FontSize = 32, Margin = new Thickness(0,20,0,0) };
        count.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("ClickCount") { StringFormat = "Completed clicks: {0}" });
        text.Children.Add(count);
        foreach (var property in new[] { "KeysDown", "ButtonsDown", "PointerMoves", "DragMoves", "PointerPosition" })
        {
            var label = new System.Windows.Controls.TextBlock { FontSize = 18 };
            label.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding(property) { StringFormat = property + ": {0}" });
            text.Children.Add(label);
        }
        InputArea.Child = text; Content = InputArea;
        Activated += (_, _) => Topmost = true;
        Deactivated += (_, _) => Topmost = false;
    }
    private nint ReceiveHorizontalWheel(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x20E && InputArea.IsMouseOver) monitor.HorizontalWheel(unchecked((short)(wParam.ToInt64() >> 16)));
        return 0;
    }

    private void UpdateFocus() => monitor.SetFocus(IsActive, InputArea?.IsKeyboardFocused == true);
    private void Window_FocusChanged(object? sender, EventArgs e) => UpdateFocus();
    private void Area_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateFocus();
    private void Area_MouseDown(object sender, MouseButtonEventArgs e)
    {
        InputArea.Focus();
        UpdateFocus();
        monitor.ButtonDown(e.ChangedButton.ToString());
    }
    private void Window_MouseUp(object sender, MouseButtonEventArgs e) =>
        monitor.ButtonUp(e.ChangedButton.ToString(), InputArea.IsMouseOver);
    private void Area_MouseLeave(object sender, MouseEventArgs e) => monitor.PointerLeft();
    private void Area_KeyDown(object sender, KeyEventArgs e) => monitor.KeyDown(KeyName(e), e.IsRepeat);
    private void Area_KeyUp(object sender, KeyEventArgs e) => monitor.KeyUp(KeyName(e));
    private static string KeyName(KeyEventArgs e) => (e.Key == Key.System ? e.SystemKey : e.Key).ToString();
    private void Area_MouseWheel(object sender, MouseWheelEventArgs e) => monitor.Wheel(e.Delta);
    private void Clear_Click(object sender, RoutedEventArgs e)
    { monitor.Reset(); receivedText = ""; ReceivedText.Text = "Text received here (latest 256 units, memory only):"; }
    internal object ProbeSnapshot()
    {
        var point = InputArea.PointToScreen(new Point(InputArea.ActualWidth / 2, InputArea.ActualHeight / 2));
        var patch = CapturePatch.PointToScreen(new Point(16, 16));
        return new { Count = monitor.ClickCount, monitor.ButtonsDown, monitor.KeysDown, Text = receivedText,
            monitor.ButtonDownEvents, monitor.ButtonUpEvents, monitor.PointerLeaveEvents, PointerInsideArea = InputArea.IsMouseOver,
            monitor.KeyDownEvents, monitor.KeyUpEvents, monitor.WheelDelta, monitor.HorizontalWheelDelta,
            Focused = InputArea.IsKeyboardFocused, Window = new System.Windows.Interop.WindowInteropHelper(this).Handle.ToInt64(),
            X = (int)point.X, Y = (int)point.Y, CaptureX = (int)patch.X, CaptureY = (int)patch.Y,
            Dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX };
    }
    internal void FocusProbeArea() { WindowState = WindowState.Normal; Activate(); InputArea.Focus(); }
    internal void SetCapturePatch(bool red)
    {
        CapturePatch.Visibility = Visibility.Visible;
        CapturePatch.Background = new System.Windows.Media.SolidColorBrush(red
            ? System.Windows.Media.Color.FromRgb(0xD8, 0x4A, 0x31) : System.Windows.Media.Color.FromRgb(0x24, 0x68, 0xAC));
        UpdateLayout();
    }
}
