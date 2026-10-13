using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.WindowsProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Independent preconditions stop an accidental launch on a real desktop from sending input:
        // the explicit flag, MEC_ISOLATED_VM=1 set inside the guest, a hypervisor in the BIOS strings, and a standard user.
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        string manufacturer = bios?.GetValue("SystemManufacturer") as string ?? "";
        string product = bios?.GetValue("SystemProductName") as string ?? "";
        bool virtualMachine = new[] { "VMware", "VirtualBox", "innotek", "QEMU", "Parallels", "Xen" }.Any(v => manufacturer.Contains(v, StringComparison.OrdinalIgnoreCase))
            || product.Contains("Virtual", StringComparison.OrdinalIgnoreCase);
        if (args.Length is not (2 or 3) || args.Length == 3 && args[2] is not ("--m1" or "--recovery" or "--performance" or "--spam-performance") || args[0] != "--isolated-vm"
            || Environment.GetEnvironmentVariable("MEC_ISOLATED_VM") != "1" || !virtualMachine) return 2;
        if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 2;
        // Two probes would share a cursor and F9 route. Refuse concurrent sessions before opening UI.
        using var session = new Mutex(false, @"Local\MinimumEffortClicker.M0Probe");
        try { if (!session.WaitOne(0)) return 2; }
        catch (AbandonedMutexException) { /* The previous process exited; this thread now owns the mutex. */ }
        try { return RunApplication(Path.GetFullPath(args[1]), args.Length == 3 ? args[2] : "--m0"); }
        finally { session.ReleaseMutex(); }
    }

    private static int RunApplication(string directory, string mode)
    {
        Directory.CreateDirectory(directory);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var banner = new Window { Title = "MEC isolated probe — click here to give focus", Width = 490, Height = 190,
            Content = new TextBlock { Text = $"Click this window once to give the test focus.\nF9 stops all probe input.\nInput cutoff: {(mode == "--m1" ? 90 : 30)} seconds after focus is ready.", Margin = new Thickness(24), FontSize = 18 } };
        // Begin after the initial window has rendered and completed its activation sequence.
        // Starting from Loaded can race a newly launched receiver against banner activation.
        bool started = false;
        banner.ContentRendered += async (_, _) =>
        {
            if (started) return;
            started = true;
            var report = await Task.Run(() => mode == "--m1" ? HelperProbe.RunAsync(application, banner)
                : mode is "--recovery" or "--performance" or "--spam-performance" ? RecoveryProbe.RunAsync(application, banner, mode == "--performance", mode == "--spam-performance") : RunAsync(application, banner, directory));
            File.WriteAllText(Path.Combine(directory, "probe-result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            application.Shutdown(report.Passed ? 0 : 1);
        };
        return application.Run(banner);
    }

    internal sealed record TargetState(int Count, string ButtonsDown, string KeysDown, bool Focused, long Window, int X, int Y, double Dpi,
        int ButtonDownEvents, int ButtonUpEvents, int PointerLeaveEvents, bool PointerInsideArea, int KeyDownEvents, int KeyUpEvents, int WheelDelta, int CaptureX, int CaptureY, string Text, int HorizontalWheelDelta);
    internal sealed class Report
    {
        public string Scope { get; set; } = "M0 isolated VMware feasibility, not product acceptance";
        public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset FinishedAtUtc { get; set; }
        public bool Passed { get; set; }
        public string Stage { get; set; } = "Startup";
        public bool ProbeInitiallyForeground { get; set; }
        public bool ReceiverForegroundGranted { get; set; }
        public int SessionId { get; set; } = Process.GetCurrentProcess().SessionId;
        public List<string> Checks { get; set; } = [];
        public List<string> ReceiverSamples { get; set; } = [];
        public string? Error { get; set; }
        public string OS { get; set; } = Environment.OSVersion.ToString();
        public int LogicalProcessors { get; set; } = Environment.ProcessorCount;
        public double DpiScale { get; set; }
        public double? IdleCpuPercentOneLogicalCore { get; set; }
        public double? AggregatePrivateMiB { get; set; }
        public double? SyntheticF9ToCleanupMs { get; set; }
        public int FinalReceiverCount { get; set; }
        public string Unmeasured { get; set; } = "Physical stop latency/p99, other DPI/monitors, physical input collisions, game compatibility, eight-hour soak.";
    }

    internal static async Task<TargetState> Request(string pipeName, string command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(timeout.Token); }
        catch (OperationCanceledException exception) { throw new TimeoutException($"TestTarget pipe connection timed out during '{command}'.", exception); }
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(command);
        return JsonSerializer.Deserialize<TargetState>(await reader.ReadLineAsync(timeout.Token) ?? "{}")!;
    }

    internal static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    internal static async Task<TargetState> WaitForCount(string pipeName, int expected, CancellationToken token)
    {
        // A pipe response can be dispatched before WPF drains its input queue on a busy VM.
        // Wait for bounded receiver evidence instead of assuming a fixed 100 ms delivery delay.
        var timer = Stopwatch.StartNew();
        TargetState state;
        do
        {
            token.ThrowIfCancellationRequested();
            state = await Request(pipeName, "state");
            if (state.Count >= expected) return state;
            await Task.Delay(50, token);
        } while (timer.Elapsed < TimeSpan.FromSeconds(3));
        return state;
    }

    internal static async Task WaitFor(Func<bool> predicate, CancellationToken token, TimeSpan? maximum = null)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        {
            token.ThrowIfCancellationRequested();
            if (timer.Elapsed > (maximum ?? TimeSpan.FromSeconds(3))) throw new TimeoutException("Probe precondition timed out.");
            await Task.Delay(10, token);
        }
    }

    private static async Task<Report> RunAsync(Application app, Window banner, string directory)
    {
        var report = new Report();
        string pipeName = "MEC-Probe-" + Guid.NewGuid().ToString("N");
        Process? target = null;
        Task<string>? targetError = null;
        Window? overlay = null;
        BoundedClickProbe? active = null;
        var runs = new List<BoundedClickProbe>();
        Task<bool>? pressWorker = null;
        // Waiting for the user to focus the probe emits no input. Start the independent
        // 30-second input watchdog only after foreground access is established.
        using var deadline = new CancellationTokenSource();
        using var stopSeen = new ManualResetEventSlim(false);
        ProbeStopHotkey? hotkey = null;
        using var timeout = deadline.Token.Register(() => active?.Stop());
        try
        {
            hotkey = new ProbeStopHotkey(() => { active?.Stop(); stopSeen.Set(); });
            report.Stage = "Probe window activation";
            nint bannerHandle = await app.Dispatcher.InvokeAsync(() => new WindowInteropHelper(banner).Handle);
            report.ProbeInitiallyForeground = ProbeNative.GetForegroundWindow() == bannerHandle;
            await app.Dispatcher.InvokeAsync(() => banner.Activate());
            await WaitFor(() => ProbeNative.GetForegroundWindow() == bannerHandle || stopSeen.IsSet,
                deadline.Token, TimeSpan.FromSeconds(60));
            Require(!stopSeen.IsSet, "User stopped before foreground access was established.");
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            report.Stage = "Receiver startup and handshake";
            string targetPath = Path.Combine(AppContext.BaseDirectory, "MinimumEffortClicker.TestTarget.exe");
            target = Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = false, RedirectStandardError = true,
                ArgumentList = { "--probe-pipe", pipeName } }) ?? throw new InvalidOperationException("Could not launch TestTarget.");
            targetError = target.StandardError.ReadToEndAsync();
            report.ReceiverForegroundGranted = ProbeNative.AllowSetForegroundWindow((uint)target.Id);
            var state = await Request(pipeName, "focus");
            var input = new BoundProbeInput((nint)state.Window, (uint)target.Id);
            report.Stage = "Receiver foreground activation";
            await WaitFor(() => input.TargetAvailable, deadline.Token);
            state = await Request(pipeName, "state");
            Require(state.Focused, "Test area did not acquire focus.");
            Require(ProbeNative.SetCursorPos(state.X, state.Y), "Cannot place probe pointer.");
            report.DpiScale = state.Dpi;
            report.Checks.Add("Standard-user target bound; F9 registered on dedicated control thread.");

            // Brief idle baseline, with no input. This includes both development-only processes.
            report.Stage = "Idle baseline";
            await Task.Delay(700, deadline.Token);
            using var self = Process.GetCurrentProcess();
            var before = self.TotalProcessorTime + target.TotalProcessorTime;
            var sample = Stopwatch.StartNew();
            await Task.Delay(2000, deadline.Token);
            self.Refresh(); target.Refresh();
            report.IdleCpuPercentOneLogicalCore = ((self.TotalProcessorTime + target.TotalProcessorTime - before).TotalMilliseconds / sample.Elapsed.TotalMilliseconds) * 100;
            report.AggregatePrivateMiB = (self.PrivateMemorySize64 + target.PrivateMemorySize64) / 1048576.0;

            report.Stage = "Foreground clicks";
            state = await Request(pipeName, "state");
            report.ReceiverSamples.Add($"Before clicks: count={state.Count}, down={state.ButtonDownEvents}, up={state.ButtonUpEvents}, leaves={state.PointerLeaveEvents}, overArea={state.PointerInsideArea}, focused={state.Focused}, point=({state.X},{state.Y})");
            active = new BoundedClickProbe(input); runs.Add(active);
            for (int i = 0; i < 5; i++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                Require(!stopSeen.IsSet && active.Press(TimeSpan.FromMilliseconds(20)), "Click probe was interrupted.");
                await Task.Delay(80, deadline.Token);
            }
            var afterClicks = await WaitForCount(pipeName, state.Count + 5, deadline.Token);
            ProbeNative.GetCursorPos(out var actualPointer);
            report.ReceiverSamples.Add($"After clicks: count={afterClicks.Count}, down={afterClicks.ButtonDownEvents}, up={afterClicks.ButtonUpEvents}, leaves={afterClicks.PointerLeaveEvents}, overArea={afterClicks.PointerInsideArea}, focused={afterClicks.Focused}, pointer=({actualPointer.X},{actualPointer.Y}), sent={active.InputsSent}");
            Require(afterClicks.Count == state.Count + 5, $"TestTarget received {afterClicks.Count - state.Count} completed clicks instead of five.");
            report.Checks.Add("Five foreground clicks received and counted by a separate process.");
            active.Stop(); active = null;

            report.Stage = "Overlay click-through";
            overlay = await app.Dispatcher.InvokeAsync(() =>
            {
                var window = new Window { Title = "MEC locked overlay probe", Width = 240, Height = 70,
                    AllowsTransparency = true, WindowStyle = WindowStyle.None, Background = Brushes.Transparent,
                    ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                    Content = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 17, 16, 16)), Padding = new Thickness(12),
                        Child = new TextBlock { Text = "PROBE RUNNING\nF9 · Stop all", Foreground = Brushes.White, FontSize = 15 } } };
                window.SourceInitialized += (_, _) =>
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    var styles = ProbeNative.GetWindowLongPtr(hwnd, -20).ToInt64();
                    ProbeNative.SetWindowLongPtr(hwnd, -20, (nint)(styles | 0x80000 | 0x20 | 0x08000000 | 0x80));
                    ProbeNative.SetWindowPos(hwnd, -1, state.X - 100, state.Y - 25, 240, 70, 0x0010 | 0x0040);
                };
                window.Show();
                var overlayHandle = new WindowInteropHelper(window).Handle;
                Require(ProbeNative.SetWindowPos(overlayHandle, -1, state.X - 100, state.Y - 25, 240, 70, 0x0010 | 0x0040), "Cannot place overlay over the test point.");
                Require(ProbeNative.GetWindowRect(overlayHandle, out var bounds)
                    && state.X >= bounds.Left && state.X < bounds.Right && state.Y >= bounds.Top && state.Y < bounds.Bottom,
                    "Overlay does not cover the test point; click-through result would be invalid.");
                long flags = ProbeNative.GetWindowLongPtr(overlayHandle, -20).ToInt64();
                Require((flags & (0x80000 | 0x20 | 0x08000000 | 0x8)) == (0x80000 | 0x20 | 0x08000000 | 0x8), "Required native overlay styles are missing.");
                return window;
            });
            await Task.Delay(150, deadline.Token);
            Require(input.TargetAvailable, "Overlay stole foreground focus.");
            active = new BoundedClickProbe(input); runs.Add(active);
            for (int i = 0; i < 5; i++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                Require(!stopSeen.IsSet && active.Press(TimeSpan.FromMilliseconds(20)), "Overlay click probe interrupted.");
                await Task.Delay(80, deadline.Token);
            }
            Require((await WaitForCount(pipeName, state.Count + 10, deadline.Token)).Count == state.Count + 10, "Clicks did not pass through the overlay into TestTarget.");
            Require(input.TargetAvailable, "Overlay click changed focus.");
            report.Checks.Add("Locked layered/topmost WPF overlay allowed five cross-process clicks without activation.");
            active.Stop(); active = null;

            // A foreground change must reject a new down; it must not silently resume on return.
            report.Stage = "Focus safety";
            await app.Dispatcher.InvokeAsync(() => { banner.Activate(); });
            await WaitFor(() => !input.TargetAvailable, deadline.Token);
            active = new BoundedClickProbe(input); runs.Add(active);
            Require(!active.Press(TimeSpan.FromMilliseconds(20)) && active.InputsSent == 0, "Input was sent after target focus loss.");
            await Request(pipeName, "focus");
            await WaitFor(() => input.TargetAvailable, deadline.Token);
            Require(!active.Press(TimeSpan.FromMilliseconds(20)), "Stopped probe resumed automatically.");
            report.Checks.Add("Focus mismatch refused a new down; focus return did not restart the stopped probe.");
            active = new BoundedClickProbe(input); runs.Add(active);
            Require(ProbeNative.SetCursorPos(state.X, state.Y), "Cannot reposition pointer.");

            // A single synthetic F9 exercises the OS registration path while this process's WPF UI is blocked.
            report.Stage = "Stop during blocked UI";
            Require(!stopSeen.IsSet, "User stopped the probe.");
            pressWorker = Task.Run(() => active.Press(TimeSpan.FromSeconds(10)));
            await WaitFor(() => active.HasOwnedInput, deadline.Token);
            using var uiBlocked = new ManualResetEventSlim(false);
            var block = app.Dispatcher.InvokeAsync(() => { uiBlocked.Set(); Thread.Sleep(1500); });
            Require(uiBlocked.Wait(2000), "Could not enter UI block test.");
            var latency = Stopwatch.StartNew();
            Require(ProbeNative.SendProbeStopKey(), "Synthetic F9 dispatch failed.");
            Require(stopSeen.Wait(1000), "Control-thread stop did not fire while UI was blocked.");
            Require(!await pressWorker.WaitAsync(TimeSpan.FromSeconds(1)), "Long hold was not cancelled.");
            report.SyntheticF9ToCleanupMs = latency.Elapsed.TotalMilliseconds;
            Require(!active.HasOwnedInput, "Input ownership remained after stop.");
            await block;
            var final = await Request(pipeName, "state");
            report.FinalReceiverCount = final.Count;
            Require(final.ButtonsDown == "None observed", "TestTarget still observes a held button.");
            report.Checks.Add("Registered synthetic F9 stopped a ten-second hold and released its owned button during a 1.5-second WPF UI block.");
            report.Passed = true;
            report.Stage = "Completed";
        }
        catch (Exception exception)
        {
            report.Error = exception.GetType().Name + ": " + exception.Message;
            if (target?.HasExited == true && targetError is not null)
            {
                string error = await targetError;
                report.Error += $" Receiver exit {target.ExitCode}: " + error[..Math.Min(error.Length, 4096)];
            }
        }
        finally
        {
            active?.Stop();
            hotkey?.Dispose();
            timeout.Dispose();
            if (pressWorker is not null) { try { await pressWorker.WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception) { } }
            if (pressWorker is null || pressWorker.IsCompleted) foreach (var run in runs) run.Dispose();
            if (overlay is not null) await app.Dispatcher.InvokeAsync(overlay.Close);
            if (target is not null)
            {
                try { await Request(pipeName, "close"); } catch (Exception) { }
                if (!target.WaitForExit(2000)) target.Kill();
                target.Dispose();
            }
        }
        report.FinishedAtUtc = DateTimeOffset.UtcNow;
        return report;
    }
}
