using System.Diagnostics;
using System.Windows;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;
using static MinimumEffortClicker.WindowsProbe.Program;

namespace MinimumEffortClicker.WindowsProbe;

internal static class SpamPerformanceProbe
{
    internal static async Task MeasureAsync(Application app, DesktopRuntime runtime, Process receiverProcess,
        string pipe, TargetState receiver, Report report, CancellationToken stop)
    {
        report.Scope = "Opt-in VMware native Spam-toggle benchmark; isolated TestTarget only, F9/cutoff cleanup. Runtime/probe and receiver CPU reported separately; excludes complete editor.";
        ProbeNative.SetCursorPos(receiver.X, receiver.Y);
        using var process = Process.GetCurrentProcess();
        foreach (bool mouse in new[] { false, true })
        foreach (int interval in new[] { 100, 50, 20, 10 })
        {
            stop.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            var helper = new HelperPlan(mouse ? InputControl.Mouse(PointerButton.Left) : InputControl.Key(0x12),
                HelperMode.Repeat, TimeSpan.FromMilliseconds(interval), TimeSpan.FromMilliseconds(2), 0);
            Require(await runtime.ConfigureHelpersAsync([new(new(id, 0x75, false), helper, "Spam benchmark")]), "F6 unavailable.");
            var before = await Request(pipe, "state");
            process.Refresh(); receiverProcess.Refresh();
            var cpu = process.TotalProcessorTime; var receiverCpu = receiverProcess.TotalProcessorTime;
            var duration = Stopwatch.StartNew();
            report.Stage = $"Spam {(mouse ? "click" : "E")} at {interval}ms";
            Require(ProbeNative.SendProbeMacroKey(), "F6 start failed.");
            await WaitFor(() => runtime.Snapshot.Busy, stop);
            await Task.Delay(2000, stop);
            var stopping = Stopwatch.StartNew();
            Require(ProbeNative.SendProbeMacroKey(), "F6 stop failed.");
            await WaitFor(() => !runtime.Snapshot.Busy && !runtime.Snapshot.CleanupPending, stop);
            double stopMs = stopping.Elapsed.TotalMilliseconds;
            var after = await Request(pipe, "state");
            process.Refresh(); receiverProcess.Refresh();
            double seconds = duration.Elapsed.TotalSeconds;
            long presses = runtime.Snapshot.InputsSent / 2;
            Require(presses > 0 && runtime.Snapshot.InputsSent % 2 == 0 && after.ButtonsDown == "None observed" && after.KeysDown == "None observed", "Spam ownership cleanup failed.");
            if (mouse) Require(after.Count - before.Count == presses, "Click receipt mismatch.");
            else Require(after.KeyDownEvents - before.KeyDownEvents >= presses, "Key receipt missing.");
            report.ReceiverSamples.Add($"{report.Stage}: {presses} presses, {presses / seconds:0.###}/sec across {seconds:0.###}s including start/stop; runtime/probe CPU {(process.TotalProcessorTime-cpu).TotalSeconds/seconds*100:0.###}% one core; receiver CPU {(receiverProcess.TotalProcessorTime-receiverCpu).TotalSeconds/seconds*100:0.###}%; stop/cleanup {stopMs:0.###}ms; clean release.");
        }
        await runtime.ConfigureHelpersAsync([]);
    }
}
