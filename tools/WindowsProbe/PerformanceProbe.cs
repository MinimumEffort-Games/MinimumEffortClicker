using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using MinimumEffortClicker.App;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;
using MinimumEffortClicker.Vision;
using static MinimumEffortClicker.WindowsProbe.Program;

namespace MinimumEffortClicker.WindowsProbe;

internal static class PerformanceProbe
{
    internal static async Task MeasureAsync(Application app, DesktopRuntime runtime, Process receiverProcess,
        string pipe, TargetState receiver, Report report, CancellationToken stop)
    {
        report.Scope = "Bounded native runtime/overlay performance matrix. Probe shell is not the complete editor; receiver cost reported separately.";
        ProbeNative.SetCursorPos(receiver.X, receiver.Y);
        var overlay = await app.Dispatcher.InvokeAsync(() => { var value = new StatusOverlay(); value.Show(); value.Hide(); return value; });
        using var product = Process.GetCurrentProcess();
        try
        {
            async Task Sample(string scenario, Func<Task> action)
            {
                product.Refresh(); receiverProcess.Refresh();
                var productCpu = product.TotalProcessorTime; var receiverCpu = receiverProcess.TotalProcessorTime;
                var clock = Stopwatch.StartNew();
                await action();
                double seconds = clock.Elapsed.TotalSeconds;
                product.Refresh(); receiverProcess.Refresh();
                report.ReceiverSamples.Add($"{scenario}: {seconds:0.###} s; runtime/probe CPU {(product.TotalProcessorTime - productCpu).TotalSeconds / seconds * 100:0.###}% of one core, private {product.PrivateMemorySize64 / 1048576d:0.###} MiB; receiver CPU {(receiverProcess.TotalProcessorTime - receiverCpu).TotalSeconds / seconds * 100:0.###}%, private {receiverProcess.PrivateMemorySize64 / 1048576d:0.###} MiB; aggregate private {(product.PrivateMemorySize64 + receiverProcess.PrivateMemorySize64) / 1048576d:0.###} MiB.");
            }
            report.Stage = "Idle runtime sample";
            await Sample("Idle, no capture, overlay hidden", () => Task.Delay(2000, stop));
            foreach (var scenario in new[] { (Rate: 1, Overlay: false), (Rate: 10, Overlay: false), (Rate: 20, Overlay: false), (Rate: 100, Overlay: false), (Rate: 20, Overlay: true) })
            {
                report.Stage = $"{scenario.Rate} CPS, overlay {scenario.Overlay}";
                runtime.SetClickRateLimit(scenario.Rate);
                await app.Dispatcher.InvokeAsync(() =>
                {
                    if (scenario.Overlay) { overlay.Update(runtime.Snapshot, "F9"); overlay.Show(); }
                    else overlay.Hide();
                    runtime.OverlayHandle = scenario.Overlay ? new WindowInteropHelper(overlay).Handle : 0;
                });
                var before = await Request(pipe, "state");
                var plan = new HelperPlan(InputControl.Mouse(PointerButton.Left), HelperMode.Repeat,
                    TimeSpan.FromSeconds(1d / scenario.Rate), TimeSpan.FromMilliseconds(2), 0, TimeSpan.FromSeconds(3));
                var delivered = Stopwatch.StartNew();
                await Sample(report.Stage, async () =>
                {
                    Require(runtime.TryStart(plan), runtime.Snapshot.Reason);
                    while (runtime.Snapshot.Busy)
                    {
                        await Task.Delay(100, stop);
                        if (scenario.Overlay) await app.Dispatcher.InvokeAsync(() => overlay.Update(runtime.Snapshot, "F9"));
                    }
                });
                int expected = before.Count + (int)runtime.Snapshot.InputsSent / 2;
                var after = await WaitForCount(pipe, expected, stop);
                Require(after.Count == expected && !runtime.Snapshot.CleanupPending && after.ButtonsDown == "None observed", "Performance phase receipt/cleanup mismatch.");
                report.ReceiverSamples.Add($"Requested {scenario.Rate} CPS, overlay {scenario.Overlay}: {after.Count - before.Count} received clicks in {delivered.Elapsed.TotalSeconds:0.###} s, {runtime.Snapshot.InputsSent} transitions, cleanup complete.");
            }
            await app.Dispatcher.InvokeAsync(overlay.Hide); runtime.OverlayHandle = 0;
            report.Stage = "Capture-only resource sample";
            var frame = runtime.GetTargetFrame();
            var point = new ClientPoint(receiver.CaptureX - frame.X, receiver.CaptureY - frame.Y, frame.Width, frame.Height, frame.Dpi);
            long inputs = runtime.Snapshot.InputsSent;
            await Sample("8x8 WGC capture at up to 10 samples/sec; zero new input", async () =>
            {
                using var capture = new WindowRegionCapture((nint)receiver.Window, new(point, 8, 8), () => runtime.Current?.IsAvailable == true);
                var clock = Stopwatch.StartNew(); int samples = 0;
                while (clock.Elapsed < TimeSpan.FromSeconds(2)) { _ = capture.Capture(stop); samples++; await Task.Delay(100, stop); }
                report.ReceiverSamples.Add($"Capture observations: {samples}; session disposed after sample.");
            });
            Require(inputs == runtime.Snapshot.InputsSent, "Capture resource sample emitted input.");
            report.Checks.Add("Separate idle, 1/10/20/100 CPS, compact overlay and WGC samples completed with exact receiver counts and cleanup. CPU denominator is one logical core; receiver cost is separate. Short samples are not steady-state or whole-app acceptance.");
        }
        finally { await app.Dispatcher.InvokeAsync(() => { overlay.Close(); }); }
    }
}
