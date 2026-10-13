using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;
using static MinimumEffortClicker.WindowsProbe.Program;

namespace MinimumEffortClicker.WindowsProbe;

internal static class RecoveryProbe
{
    internal static async Task<Report> RunAsync(Application app, Window banner, bool performance = false, bool spamPerformance = false)
    {
        var report = new Report { Scope = "Isolated VM target minimize/exit/reselection and resume cancellation; receiver-only input" };
        using var runtime = new DesktopRuntime();
        using var cutoff = new CancellationTokenSource();
        using var stopOnCutoff = cutoff.Token.Register(() => runtime.Stop("Recovery probe cutoff."));
        runtime.Control.StopRequested += _ => cutoff.Cancel();
        Process? target = null;
        string pipe = "MEC-Probe-" + Guid.NewGuid().ToString("N");
        Process LaunchReceiver() => Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "MinimumEffortClicker.TestTarget.exe"))
            { UseShellExecute = false, ArgumentList = { "--probe-pipe", pipe } }) ?? throw new InvalidOperationException("Receiver launch failed.");
        try
        {
            Require(runtime.Control.Available, "Independent stop route unavailable.");
            var bannerHandle = await app.Dispatcher.InvokeAsync(() => new WindowInteropHelper(banner).Handle);
            await app.Dispatcher.InvokeAsync(() => banner.Activate());
            await WaitFor(() => ProbeNative.GetForegroundWindow() == bannerHandle, cutoff.Token, TimeSpan.FromSeconds(60));
            report.ProbeInitiallyForeground = true; cutoff.CancelAfter(TimeSpan.FromSeconds(30));
            target = LaunchReceiver();
            report.ReceiverForegroundGranted = ProbeNative.AllowSetForegroundWindow((uint)target.Id);
            var receiver = await Request(pipe, "focus");
            runtime.SelectTarget(new((nint)receiver.Window, target.Id, "Recovery receiver"));
            await WaitFor(() => runtime.Current?.IsAvailable == true, cutoff.Token);
            report.DpiScale = receiver.Dpi;
            if (spamPerformance)
            {
                await SpamPerformanceProbe.MeasureAsync(app, runtime, target, pipe, receiver, report, cutoff.Token);
                report.Stage = "Completed"; report.Passed = true; return report;
            }
            if (performance)
            {
                await PerformanceProbe.MeasureAsync(app, runtime, target, pipe, receiver, report, cutoff.Token);
                report.Stage = "Completed"; report.Passed = true; return report;
            }
            var hold = new HelperPlan(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 0);

            report.Stage = "Minimized target pauses and restores without output";
            Require(runtime.TryStart(hold), runtime.Snapshot.Reason);
            await WaitFor(() => runtime.Snapshot.InputsSent == 1, cutoff.Token);
            await Request(pipe, "minimize");
            await WaitFor(() => runtime.CanResume, cutoff.Token);
            Require(runtime.Snapshot.InputsSent == 2 && !runtime.Snapshot.CleanupPending, "Minimize did not release owned input.");
            Require(!runtime.TryResume(), "Minimized target accepted resume.");
            await Request(pipe, "focus");
            await WaitFor(() => runtime.Current?.IsAvailable == true, cutoff.Token);
            await Task.Delay(200, cutoff.Token);
            Require(runtime.CanResume && runtime.Snapshot.InputsSent == 2, "Restore resumed without authorization.");
            runtime.Stop("Cancel the resume handoff.");
            await WaitFor(() => !runtime.Snapshot.Busy, cutoff.Token);
            Require(runtime.Snapshot.InputsSent == 2 && !runtime.CanResume && !runtime.Snapshot.CleanupPending, "Cancelled countdown resumed input.");
            report.Checks.Add("Target minimize released W; minimized resume was refused; restore sent nothing; Stop while paused discarded the continuation.");

            report.Stage = "Target exit invalidates its identity and releases input";
            Require(runtime.TryStart(hold), runtime.Snapshot.Reason);
            await WaitFor(() => runtime.Snapshot.InputsSent == 1, cutoff.Token);
            await Request(pipe, "close");
            await target.WaitForExitAsync(cutoff.Token);
            await WaitFor(() => !runtime.Snapshot.Busy, cutoff.Token);
            Require(runtime.Snapshot.InputsSent == 2 && !runtime.Snapshot.CleanupPending && !runtime.CanResume, "Target exit left ownership or continuation.");
            Require(!runtime.TryStart(hold), "Closed target accepted playback.");
            target.Dispose(); target = null;
            pipe = "MEC-Probe-" + Guid.NewGuid().ToString("N");
            target = LaunchReceiver(); ProbeNative.AllowSetForegroundWindow((uint)target.Id);
            receiver = await Request(pipe, "focus");
            await Task.Delay(200, cutoff.Token);
            Require(!runtime.TryStart(hold), "A replacement receiver silently reused the old target binding.");
            Require(receiver.KeyDownEvents == 0 && !runtime.Snapshot.Busy, "Replacement received unrequested input.");
            runtime.SelectTarget(new((nint)receiver.Window, target.Id, "Reselected recovery receiver"));
            Require(runtime.TryStart(hold), runtime.Snapshot.Reason);
            await WaitFor(() => runtime.Snapshot.InputsSent == 1, cutoff.Token);
            runtime.Stop("Explicitly reselected target verified.");
            await WaitFor(() => !runtime.Snapshot.Busy, cutoff.Token);
            Require(runtime.Snapshot.InputsSent == 2 && !runtime.Snapshot.CleanupPending, "Reselected target failed cleanup.");
            report.Checks.Add("Closing the target while W was held cleaned up and invalidated its identity. A new receiver stayed input-free until explicitly reselected and started; final Stop released W.");
            report.Stage = "Completed"; report.Passed = true;
        }
        catch (Exception error) { report.Error = error.GetType().Name + ": " + error.Message + " Runtime: " + runtime.Snapshot; }
        finally
        {
            runtime.Stop("Recovery probe ended.");
            try { await WaitFor(() => !runtime.Snapshot.Busy, CancellationToken.None, TimeSpan.FromSeconds(2)); } catch (TimeoutException) { }
            if (target is not null)
            {
                if (!target.HasExited) { try { await Request(pipe, "close"); } catch (Exception) { } }
                if (!target.WaitForExit(2000)) target.Kill();
                target.Dispose();
            }
            report.FinishedAtUtc = DateTimeOffset.UtcNow;
        }
        return report;
    }
}
