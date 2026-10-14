using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using MinimumEffortClicker.App;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Windows;
using MinimumEffortClicker.Vision;
using static MinimumEffortClicker.WindowsProbe.Program;

namespace MinimumEffortClicker.WindowsProbe;

/// <summary>Explicit VMware-only integration of the production runtime against its own receiver.</summary>
internal static class HelperProbe
{
    internal static async Task<Report> RunAsync(Application app, Window banner)
    {
        var report = new Report { Scope = "M1 production helper runtime in isolated VMware; bounded receiver-only integration" };
        using var runtime = new DesktopRuntime();
        using var deadline = new CancellationTokenSource();
        using var cutoff = deadline.Token.Register(() => runtime.Stop("Probe cutoff."));
        bool expectedStop = false;
        runtime.Control.StopRequested += _ => { if (!expectedStop) deadline.Cancel(); };
        Process? target = null;
        StatusOverlay? overlay = null;
        string pipe = "MEC-Probe-" + Guid.NewGuid().ToString("N");
        try
        {
            Require(runtime.Control.Available, runtime.Control.Error ?? "Stop controls unavailable.");
            report.Stage = "Probe window activation";
            nint bannerHandle = await app.Dispatcher.InvokeAsync(() => new WindowInteropHelper(banner).Handle);
            await app.Dispatcher.InvokeAsync(() => banner.Activate());
            await WaitFor(() => ProbeNative.GetForegroundWindow() == bannerHandle, deadline.Token, TimeSpan.FromSeconds(60));
            report.ProbeInitiallyForeground = true;
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            report.Stage = "Input-free native timer precision";
            var timingClock = new StopwatchClock();
            using (var timingWait = new WindowsDeadlineWait(timingClock))
            {
                var timingStart = timingClock.Elapsed;
                double worstLate = 0;
                for (int i = 0; i < 100; i++)
                {
                    var due = timingClock.Elapsed + TimeSpan.FromMilliseconds(2);
                    timingWait.WaitUntil(due, deadline.Token);
                    Require(timingClock.Elapsed >= due, "Native timer returned before its deadline.");
                    worstLate = Math.Max(worstLate, (timingClock.Elapsed - due).TotalMilliseconds);
                }
                report.ReceiverSamples.Add($"Input-free timer: 100 × 2 ms waits took {(timingClock.Elapsed - timingStart).TotalMilliseconds:0.###} ms; worst observed lateness {worstLate:0.###} ms. No OS timer-resolution setting changed.");
            }
            target = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "MinimumEffortClicker.TestTarget.exe"))
                { UseShellExecute = false, ArgumentList = { "--probe-pipe", pipe } }) ?? throw new InvalidOperationException("Receiver launch failed.");
            report.ReceiverForegroundGranted = ProbeNative.AllowSetForegroundWindow((uint)target.Id);
            var receiver = await Request(pipe, "focus");
            runtime.SelectTarget(new((nint)receiver.Window, target.Id, "Probe TestTarget"));
            await WaitFor(() => runtime.Current?.IsAvailable == true, deadline.Token);
            receiver = await Request(pipe, "state");
            ProbeNative.SetCursorPos(receiver.X, receiver.Y);
            await Task.Delay(100, deadline.Token);
            report.DpiScale = receiver.Dpi;

            report.Stage = "Explicit small-region capture";
            receiver = await Request(pipe, "capture-blue");
            var captureFrame = runtime.GetTargetFrame();
            var capturePoint = new ClientPoint(receiver.CaptureX - captureFrame.X, receiver.CaptureY - captureFrame.Y,
                captureFrame.Width, captureFrame.Height, captureFrame.Dpi);
            using (var pixels = new WindowRegionCapture((nint)receiver.Window, new(capturePoint, 8, 8), () => runtime.Current?.IsAvailable == true))
            {
                CapturedRegion blue = pixels.Capture(deadline.Token);
                Require(blue.Image.Pixels[0] == 0xAC && blue.Image.Pixels[1] == 0x68 && blue.Image.Pixels[2] == 0x24,
                    $"Capture mapping failed: BGR={blue.Image.Pixels[0]},{blue.Image.Pixels[1]},{blue.Image.Pixels[2]}.");
                await Request(pipe, "capture-red");
                CapturedRegion red = blue;
                for (int frame = 0; frame < 10 && red.Image.Pixels[2] != 0xD8; frame++) red = pixels.Capture(deadline.Token);
                Require(red.FrameId > blue.FrameId && RegionComparison.Changed(blue.Image, red.Image, 2, 1), "Changed patch was not captured on a fresh frame.");
                Require(red.Image.Pixels[0] == 0x31 && red.Image.Pixels[1] == 0x4A && red.Image.Pixels[2] == 0xD8, "Red patch colour mismatch.");
            }
            Require(runtime.Snapshot.InputsSent == 0, "Capture emitted input.");
            report.Checks.Add("WGC captured only an 8×8 client patch from the owned receiver; fresh blue/red frames matched exact pixels, comparison detected change, and capture disposed with zero inputs.");

            report.Stage = "Production repeated clicks and overlay";
            overlay = await app.Dispatcher.InvokeAsync(() =>
            {
                var window = new StatusOverlay();
                window.Update(runtime.Snapshot, "F9"); window.Show();
                runtime.OverlayHandle = new WindowInteropHelper(window).Handle;
                Require(ProbeNative.SetWindowPos(runtime.OverlayHandle, -1, receiver.X - 100, receiver.Y - 20, 280, 86, 0x0010 | 0x0040), "Overlay placement failed.");
                return window;
            });
            var clicks = new HelperPlan(InputControl.Mouse(PointerButton.Left), HelperMode.Repeat, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 5);
            Require(runtime.TryStart(clicks), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            var clicked = await WaitForCount(pipe, receiver.Count + 5, deadline.Token);
            Require(clicked.Count == receiver.Count + 5 && runtime.Snapshot.InputsSent == 10, $"Click mismatch: receiver {clicked.Count}, runtime {runtime.Snapshot}.");
            report.Checks.Add("Production executor and locked overlay: five clicks received, ten transitions sent, no activation.");

            report.Stage = "Bounded 100 CPS receiver measurement";
            runtime.SetClickRateLimit(100);
            var dispatchBefore = runtime.NativeDispatchTiming;
            var fastStart = Stopwatch.StartNew();
            Require(runtime.TryStart(clicks with { Interval = TimeSpan.FromMilliseconds(10), PressDuration = TimeSpan.FromMilliseconds(2), Count = 100 }), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token, TimeSpan.FromSeconds(5));
            var fastElapsed = fastStart.Elapsed;
            var fastReceiver = await WaitForCount(pipe, clicked.Count + 100, deadline.Token);
            Require(fastReceiver.Count == clicked.Count + 100 && runtime.Snapshot.InputsSent == 200 && !runtime.Snapshot.CleanupPending, "High-rate receiver count or cleanup mismatch.");
            runtime.SetClickRateLimit(20);
            var dispatchAfter = runtime.NativeDispatchTiming;
            report.ReceiverSamples.Add($"Native dispatch: {dispatchAfter.Count - dispatchBefore.Count} calls, {dispatchAfter.TotalMilliseconds - dispatchBefore.TotalMilliseconds:0.###} ms total; lifetime maximum {dispatchAfter.MaximumMilliseconds:0.###} ms.");
            report.ReceiverSamples.Add($"100 requested CPS: 100 received clicks in {fastElapsed.TotalMilliseconds:0.###} ms including final release and completion polling; no catch-up bursts. Overlay shown; target renderer active.");

            report.Stage = "Bounded 20 CPS resource sample";
            using (var productProcess = Process.GetCurrentProcess())
            {
                var cpuBefore = productProcess.TotalProcessorTime;
                var sampleStart = Stopwatch.StartNew();
                Require(runtime.TryStart(clicks with { Interval = TimeSpan.FromMilliseconds(50), Count = 60 }), runtime.Snapshot.Reason);
                await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token, TimeSpan.FromSeconds(6));
                var sampleReceiver = await WaitForCount(pipe, fastReceiver.Count + 60, deadline.Token);
                productProcess.Refresh();
                Require(sampleReceiver.Count == fastReceiver.Count + 60 && !runtime.Snapshot.CleanupPending, "20 CPS sample failed receipt or cleanup.");
                report.ReceiverSamples.Add($"Short 20 CPS probe/runtime/overlay sample: {sampleStart.Elapsed.TotalSeconds:0.###} s; {(productProcess.TotalProcessorTime - cpuBefore).TotalSeconds / sampleStart.Elapsed.TotalSeconds * 100:0.###}% of one logical core; private {productProcess.PrivateMemorySize64 / 1048576d:0.###} MiB, {productProcess.HandleCount} handles. Excludes the receiver process; not whole-app acceptance or a steady-state benchmark.");
            }

            await Task.Delay(50, deadline.Token);
            receiver = await Request(pipe, "state");

            report.Stage = "Window-relative positioned click and drag";
            nint targetHandle = (nint)receiver.Window;
            Require(ProbeNative.GetClientRect(targetHandle, out var client), "Client calibration failed.");
            var clientOrigin = new ProbeNative.Point();
            Require(ProbeNative.ClientToScreen(targetHandle, ref clientOrigin), "Client origin failed.");
            var anchor = new ClientPoint(receiver.X - clientOrigin.X, receiver.Y - clientOrigin.Y, client.Right, client.Bottom, (int)ProbeNative.GetDpiForWindow(targetHandle));
            var endAnchor = anchor with { X = anchor.X + 40 };
            var positioned = SequenceCompiler.Compile(new([SequenceStep.Input("Positioned drag", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(100))
                with { Position = anchor, EndPosition = endAnchor }]));
            Require(ProbeNative.GetWindowRect(targetHandle, out var windowRect), "Window geometry unavailable.");
            Require(ProbeNative.SetWindowPos(targetHandle, 0, windowRect.Left + 30, windowRect.Top + 20, 0, 0, 0x15), "Target move failed.");
            await Task.Delay(100, deadline.Token);
            Require(runtime.TryStart(positioned), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.State == "Completed" && runtime.Snapshot.InputsSent == 2, "Positioned drag did not complete.");
            Require(ProbeNative.GetCursorPos(out var finalPointer), "Pointer query failed.");
            Require(finalPointer.X == receiver.X + 70 && finalPointer.Y == receiver.Y + 20, $"Absolute pointer mismatch: {finalPointer.X},{finalPointer.Y}.");
            var dragged = await Request(pipe, "state");
            var dragReceiptWait = Stopwatch.StartNew();
            while ((dragged.ButtonDownEvents < receiver.ButtonDownEvents + 1 || dragged.ButtonUpEvents < receiver.ButtonUpEvents + 1)
                && dragReceiptWait.Elapsed < TimeSpan.FromSeconds(3))
            { await Task.Delay(50, deadline.Token); dragged = await Request(pipe, "state"); }
            Require(dragged.ButtonDownEvents == receiver.ButtonDownEvents + 1 && dragged.ButtonUpEvents == receiver.ButtonUpEvents + 1
                && dragged.ButtonsDown == "None observed", "Positioned drag receipt mismatch.");
            Require(ProbeNative.SetWindowPos(targetHandle, 0, 0, 0, windowRect.Right - windowRect.Left + 20, windowRect.Bottom - windowRect.Top, 0x16), "Target resize failed.");
            await Task.Delay(100, deadline.Token);
            var staleCalibration = SequenceCompiler.Compile(new([
                SequenceStep.Input("Must not reach this key", InputControl.Key(0x11), TimeSpan.FromMilliseconds(20)),
                SequenceStep.Input("Stale point", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(20)) with { Position = anchor }]));
            Require(runtime.TryStart(staleCalibration), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 0 && !runtime.Snapshot.CleanupPending
                && runtime.Snapshot.Reason.Contains("size or DPI changed", StringComparison.Ordinal), "Resized calibration sent input or did not report recalibration.");
            Require(ProbeNative.SetWindowPos(targetHandle, 0, windowRect.Left, windowRect.Top, windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top, 0x14), "Target restore failed.");
            await Task.Delay(100, deadline.Token);
            ProbeNative.SetCursorPos(receiver.X, receiver.Y);
            report.Checks.Add("Window-relative drag followed a moved target, reached its exact endpoint and released; resizing rejected playback with zero transitions.");

            report.Stage = "Fixed desktop point and foreground bounds";
            var fixedPoint = new ClientPoint(receiver.X, receiver.Y, anchor.Width, anchor.Height, anchor.Dpi, CoordinateSpace.Desktop);
            var fixedStep = SequenceStep.Input("Fixed desktop", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(20)) with { Position = fixedPoint };
            var beforeFixed = await Request(pipe, "state");
            Require(ProbeNative.SetWindowPos(targetHandle, 0, windowRect.Left + 30, windowRect.Top + 20, 0, 0, 0x15), "Fixed-point target move failed.");
            await Task.Delay(50, deadline.Token);
            Require(runtime.TryStart(SequenceCompiler.Compile(new([fixedStep]))), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            await WaitForCount(pipe, beforeFixed.Count + 1, deadline.Token);
            Require(ProbeNative.GetCursorPos(out var fixedPointer) && fixedPointer.X == fixedPoint.X && fixedPointer.Y == fixedPoint.Y, "Desktop point followed the window instead of staying fixed.");
            var outside = fixedStep with { Position = fixedPoint with { X = -99999 } };
            Require(runtime.TryStart(SequenceCompiler.Compile(new([SequenceStep.Input("Must not execute", InputControl.Key(0x11), TimeSpan.FromMilliseconds(10)), outside]))), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 0, "Out-of-target desktop point escaped preflight.");
            Require(ProbeNative.SetWindowPos(targetHandle, 0, windowRect.Left, windowRect.Top, 0, 0, 0x15), "Fixed-point target restore failed.");
            await Task.Delay(50, deadline.Token);
            report.Checks.Add("A fixed desktop point stayed at its physical screen location after the target moved; an out-of-target desktop point rejected the entire sequence before any input.");

            report.Stage = "Named-point marker and intercepting picker";
            Require(ProbeNative.AreDpiAwarenessContextsEqual(ProbeNative.GetWindowDpiAwarenessContext(targetHandle), -4), "Receiver is not per-monitor-v2 aware.");
            var named = new NamedPoint(Guid.NewGuid(), "Receiver centre", anchor);
            var beforeMarkers = await Request(pipe, "state");
            var markers = await app.Dispatcher.InvokeAsync(() => { var view = new TargetPreviewWindow(runtime, [named], false); view.Start(); return view; });
            Require(!runtime.TryStart(clicks), "Playback started during input-free preview.");
            Require(ProbeNative.GetForegroundWindow() == targetHandle, "Markers stole target activation.");
            await FixtureClick();
            var throughMarkers = await WaitForCount(pipe, beforeMarkers.Count + 1, deadline.Token);
            Require(throughMarkers.Count == beforeMarkers.Count + 1, "Markers blocked a bounded fixture click.");
            await app.Dispatcher.InvokeAsync(markers.Close); await markers.Finished;
            var picker = await app.Dispatcher.InvokeAsync(() => { var view = new TargetPreviewWindow(runtime, [], true); view.Start(); return view; });
            Require(ProbeNative.GetForegroundWindow() == targetHandle, "Picker stole target activation.");
            await FixtureClick();
            var picked = await picker.Finished.WaitAsync(TimeSpan.FromSeconds(2), deadline.Token);
            Require(picked == anchor, $"Picker returned {picked}; expected {anchor}.");
            var afterPick = await Request(pipe, "state");
            Require(afterPick.Count == throughMarkers.Count && afterPick.ButtonDownEvents == throughMarkers.ButtonDownEvents
                && afterPick.ButtonUpEvents == throughMarkers.ButtonUpEvents, "Picker leaked its selecting click to the receiver.");
            Require(!runtime.Snapshot.Busy && runtime.Snapshot.InputsSent == 0, "Preview left runtime busy or sent playback input.");
            report.Checks.Add("Per-monitor-v2 receiver: marker preview stayed non-activating/click-through; picker intercepted both click edges and returned the exact client point; playback was excluded.");

            async Task FixtureClick()
            {
                var fixtureClock = new StopwatchClock();
                var fixture = new ClickExecutor(new BoundProbeInput(targetHandle, (uint)target.Id), fixtureClock, new DeadlineWait(fixtureClock), runtime);
                var fixturePlan = new HelperPlan(InputControl.Mouse(PointerButton.Left), HelperMode.Repeat, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 1);
                var fixtureResult = await Task.Run(() => fixture.Execute(fixturePlan, runtime.Current!.InstanceId, deadline.Token), deadline.Token);
                Require(fixtureResult.Outcome == ClickRunOutcome.Completed && fixtureResult.CleanupSucceeded, "Bounded preview fixture click failed.");
            }

            report.Stage = "Repeated scan-code key";
            var key = new HelperPlan(InputControl.Key(0x11), HelperMode.Repeat, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(30), 3);
            Require(runtime.TryStart(key), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            var keyed = await Request(pipe, "state");
            var receiptWait = Stopwatch.StartNew();
            while ((keyed.KeyDownEvents - receiver.KeyDownEvents < 3 || keyed.KeyUpEvents - receiver.KeyUpEvents < 3) && receiptWait.Elapsed < TimeSpan.FromSeconds(2))
            { await Task.Delay(20, deadline.Token); keyed = await Request(pipe, "state"); }
            report.ReceiverSamples.Add($"Repeat W: baseline down={receiver.KeyDownEvents}, up={receiver.KeyUpEvents}; actual down={keyed.KeyDownEvents}, up={keyed.KeyUpEvents}, held={keyed.KeysDown}, focused={keyed.Focused}");
            Require(keyed.KeyDownEvents - receiver.KeyDownEvents == 3 && keyed.KeyUpEvents - receiver.KeyUpEvents == 3 && keyed.KeysDown == "None observed", "Key repeat receipt mismatch; see ReceiverSamples.");
            report.Checks.Add("Three physical-position W taps received with separate down/up transitions.");


            report.Stage = "Scroll";
            var scroll = new HelperPlan(new(InputKind.Scroll, 120), HelperMode.Scroll, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 3);
            Require(runtime.TryStart(scroll), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require((await Request(pipe, "state")).WheelDelta - receiver.WheelDelta == 360, "Scroll receipt mismatch.");
            report.Checks.Add("Three wheel steps received in the bound foreground target.");
            var beforeHorizontal = await Request(pipe, "state");
            Require(runtime.TryStart(scroll with { Control = new(InputKind.HorizontalScroll, 120) }), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            var afterHorizontal = await Request(pipe, "state");
            for (int i = 0; i < 20 && afterHorizontal.HorizontalWheelDelta != beforeHorizontal.HorizontalWheelDelta + 360; i++)
            { await Task.Delay(20, deadline.Token); afterHorizontal = await Request(pipe, "state"); }
            Require(afterHorizontal.HorizontalWheelDelta == beforeHorizontal.HorizontalWheelDelta + 360 && !runtime.Snapshot.CleanupPending, "Horizontal wheel receipt mismatch.");
            report.Checks.Add("Three horizontal wheel steps were received.");

            report.Stage = "Focus-loss held-key cleanup and automatic resume";
            var hold = key with { Mode = HelperMode.Hold, Count = 0 };
            Require(runtime.TryStart(hold), runtime.Snapshot.Reason);
            await WaitFor(() => runtime.Snapshot.InputsSent == 1, deadline.Token);
            Require((await Request(pipe, "state")).KeysDown.Contains("W", StringComparison.Ordinal), "Receiver did not observe held W.");
            await app.Dispatcher.InvokeAsync(() => banner.Activate());
            await WaitFor(() => runtime.CanResume, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 2 && !runtime.Snapshot.CleanupPending, "Focus loss did not release held input.");
            var pausedActive = runtime.Snapshot.TotalActiveTime;
            Require(!runtime.TryResume(), "Resume accepted an inactive target.");
            await Request(pipe, "focus");
            await WaitFor(() => runtime.Current?.IsAvailable == true, deadline.Token);
            // The window has to stay in front for half a second before anything is sent again.
            await Task.Delay(150, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 2 && (await Request(pipe, "state")).KeysDown == "None observed", "Input restarted before the target settled in front.");
            await WaitFor(() => runtime.Snapshot.InputsSent == 3, deadline.Token);
            Require((await Request(pipe, "state")).KeysDown.Contains("W", StringComparison.Ordinal), "Automatic resume did not press W again.");
            Require(runtime.Snapshot.TotalActiveTime - pausedActive < TimeSpan.FromMilliseconds(400), "Paused time entered the active-time budget.");
            runtime.Stop("Resume fixture complete.");
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 4 && !runtime.Snapshot.CleanupPending, "Resumed hold cleanup failed.");
            report.Checks.Add("Focus loss paused and released W; inactive-target resume was refused; nothing was sent until the target had settled in front, then W was pressed again by itself with paused time excluded; Stop released it.");


            report.Stage = "Profile binding enable and disable";
            var macro = new ArmedHelper(new(Guid.NewGuid(), 0x75, false), hold, "Hold W");
            long beforeEnable = runtime.Snapshot.InputsSent;
            Require(await runtime.ConfigureHelpersAsync([macro]), "Profile binding registration failed.");
            await Task.Delay(100, deadline.Token);
            Require(!runtime.Snapshot.Busy && runtime.Snapshot.InputsSent == beforeEnable, "Enabling a profile emitted input.");
            Require(ProbeNative.SendProbeMacroKey(), "Macro trigger injection failed.");
            await WaitFor(() => runtime.Snapshot.Busy && runtime.Snapshot.InputsSent == 1, deadline.Token);
            Require(await runtime.ConfigureHelpersAsync([]), "Profile disable failed.");
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 2 && !runtime.Snapshot.CleanupPending, "Disabling a running macro did not clean its hold.");
            Require(ProbeNative.SendProbeMacroKey(), "Disabled binding probe failed.");
            await Task.Delay(100, deadline.Token);
            Require(!runtime.Snapshot.Busy && runtime.Snapshot.InputsSent == 2, "Disabled macro restarted.");
            report.Checks.Add("Profile F6 registration emitted nothing; its explicit trigger held W, disabling cleaned up, and a disabled trigger did not restart.");

            report.Stage = "Overlapping sequence through shared ownership";
            var sequence = SequenceCompiler.Compile(new([SequenceStep.Group("W with Space", TimeSpan.FromSeconds(2),
                [new(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(0x11), TimeSpan.FromSeconds(2)),
                 new(Guid.NewGuid(), TimeSpan.FromMilliseconds(250), InputControl.Key(0x39), TimeSpan.FromSeconds(1))])]));
            Require(runtime.TryStart(sequence), runtime.Snapshot.Reason);
            await WaitFor(() => runtime.Snapshot.InputsSent == 2, deadline.Token);
            var overlap = await Request(pipe, "state");
            Require(overlap.KeysDown.Contains("W", StringComparison.Ordinal) && overlap.KeysDown.Contains("Space", StringComparison.Ordinal),
                "Receiver did not observe simultaneous W and Space: " + overlap.KeysDown);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 4 && !runtime.Snapshot.CleanupPending, "Sequence did not release both inputs.");
            report.Checks.Add("One compiled timed group held W across an overlapping Space hold; both used the shared executor and released normally.");

            report.Stage = "Owned modifier chord and layout preflight";
            var chord = new SequenceDefinition([SequenceStep.Group("Shift W", TimeSpan.FromMilliseconds(300),
                [new(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(0x11), TimeSpan.FromMilliseconds(300)),
                 new(Guid.NewGuid(), TimeSpan.Zero, InputControl.Key(0x2A), TimeSpan.FromMilliseconds(300))])]);
            Require(runtime.TryStart(SequenceCompiler.Compile(chord)), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.State == "Completed" && runtime.Snapshot.InputsSent == 4 && !runtime.Snapshot.CleanupPending,
                "Owned modifier prevented its chord or failed to release.");
            Require((await Request(pipe, "state")).KeysDown == "None observed", "Chord left a held key.");
            Require(runtime.TryStart(SequenceCompiler.Compile(chord with { KeyboardLayout = "00000000" })), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 0 && !runtime.Snapshot.CleanupPending
                && runtime.Snapshot.Reason.Contains("keyboard layout", StringComparison.OrdinalIgnoreCase), "Mismatched keyboard layout sent input or did not report rejection.");
            report.Checks.Add("Shift/W chord used owned-modifier allowance and released both keys; mismatched recording layout rejected all input.");

            report.Stage = "Emergency stop with an owned modifier";
            var longChord = chord with { Steps = [chord.Steps[0] with { Duration = TimeSpan.FromSeconds(5),
                Children = chord.Steps[0].Children!.Select(c => c with { Duration = TimeSpan.FromSeconds(5) }).ToArray() }] };
            Require(runtime.TryStart(SequenceCompiler.Compile(longChord)), runtime.Snapshot.Reason);
            await WaitFor(() => runtime.Snapshot.InputsSent == 2, deadline.Token);
            expectedStop = true;
            Require(ProbeNative.SendProbeExternalStopKey(), "Modified F9 stop injection failed.");
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token, TimeSpan.FromSeconds(1));
            expectedStop = false;
            Require(runtime.Snapshot.InputsSent == 4 && !runtime.Snapshot.CleanupPending, "F9 with held Shift failed to clean the chord.");
            report.Checks.Add("F9 interrupted and released an owned Shift/W chord through the modifier-independent hook stop route.");

            report.Stage = "Recorder ignores injected triggers and excludes playback";
            using (var capture = new DesktopRecorder(runtime))
            {
                Require(!runtime.TryStart(key), "Playback started during explicit recording.");
                await app.Dispatcher.InvokeAsync(() => banner.Activate());
                await WaitFor(() => capture.Paused, deadline.Token);
                await Request(pipe, "focus");
                await WaitFor(() => runtime.Current?.IsAvailable == true, deadline.Token);
                await Task.Delay(100, deadline.Token);
                Require(capture.Paused && runtime.Snapshot.State == "Recording paused", "Focus restoration silently resumed capture.");
                capture.Resume();
                Require(!capture.Paused && runtime.Snapshot.State == "Recording", "Explicit capture resume failed.");
                Require(ProbeNative.SendProbeToggleKey(), "Injected trigger filtering probe failed.");
                await Task.Delay(100, deadline.Token);
                runtime.Stop("Bounded recorder filtering check complete.");
                await capture.Finished;
                Require(capture.Count == 0, "Injected trigger appeared in the physical recording buffer.");
                var draft = await capture.GetDraftAsync();
                Require(SequenceCompiler.Compile(draft.Sequence).Inputs.Count == 0, "Recorder synthesized actions from injected input.");
            }
            report.Checks.Add("Recorder excluded playback, paused on focus loss, stayed paused after restoration, resumed explicitly, and ignored injected F8 content.");

            report.Stage = "Production stop during blocked UI";
            var shutdown = new ShutdownSchedule();
            shutdown.Arm(new(ShutdownTrigger.CompletedLoops, Loops: 1), DateTimeOffset.Now, 0, TimeSpan.Zero);
            shutdown.Update(DateTimeOffset.Now, TimeSpan.Zero, 1, TimeSpan.Zero); shutdown.BeginCountdown(TimeSpan.Zero);
            runtime.StopIssued += () => shutdown.Cancel();
            Require(runtime.TryStart(hold), runtime.Snapshot.Reason);
            await WaitFor(() => runtime.Snapshot.InputsSent == 1, deadline.Token);
            using var uiBlocked = new ManualResetEventSlim();
            var block = app.Dispatcher.InvokeAsync(() => { uiBlocked.Set(); Thread.Sleep(1500); });
            Require(uiBlocked.Wait(2000), "UI block not entered.");
            expectedStop = true;
            var latency = Stopwatch.StartNew();
            Require(ProbeNative.SendProbeStopKey(), "F9 injection failed.");
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token, TimeSpan.FromSeconds(1));
            report.SyntheticF9ToCleanupMs = latency.Elapsed.TotalMilliseconds;
            Require(runtime.Snapshot.InputsSent == 2 && !runtime.Snapshot.CleanupPending, "F9 failed to clean held key.");
            Require(!shutdown.Armed && !shutdown.TryCommit(TimeSpan.FromMinutes(1), true, () => throw new InvalidOperationException("A cancelled fake shutdown committed.")),
                "F9 did not cancel the shutdown model independently of WPF.");
            await block;
            var final = await Request(pipe, "state");
            Require(final.KeysDown == "None observed" && final.ButtonsDown == "None observed", "Receiver still sees held input.");
            report.Checks.Add("Production F9 control stopped and cleaned a held key while WPF was blocked for 1.5 seconds.");
            expectedStop = false;
            report.Stage = "Click cap, active-time budget and keep-awake lifecycle";
            runtime.KeepAwakeWhileRunning = true; runtime.SetClickRateLimit(5);
            receiver = await Request(pipe, "focus"); ProbeNative.SetCursorPos(receiver.X, receiver.Y);
            var priorActive = runtime.Snapshot.TotalActiveTime;
            var beforeBudget = runtime.Snapshot.TotalCompletedLoops;
            runtime.CompletionLimit = beforeBudget + 3;
            var capped = clicks with { Interval = TimeSpan.FromMilliseconds(10), PressDuration = TimeSpan.FromMilliseconds(5), Count = 100 };
            Require(runtime.TryStart(capped, TimeSpan.FromMilliseconds(250)), runtime.Snapshot.Reason);
            await Task.Delay(100, deadline.Token);
            Require(runtime.Snapshot.TotalActiveTime == priorActive, "Startup countdown was counted as active playback.");
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            var powerResult = runtime.Snapshot;
            Require(powerResult.TotalCompletedLoops == beforeBudget + 3 && powerResult.InputsSent == 6 && !powerResult.CleanupPending,
                "Loop budget did not stop at exactly three clean clicks.");
            Require(powerResult.TotalActiveTime - priorActive >= TimeSpan.FromMilliseconds(400), "Global click cap was bypassed.");
            await Task.Delay(100, deadline.Token);
            Require(runtime.Snapshot.TotalActiveTime == powerResult.TotalActiveTime, "Idle time was counted as active playback.");
            final = await WaitForCount(pipe, receiver.Count + 3, deadline.Token);
            runtime.CompletionLimit = long.MaxValue; runtime.KeepAwakeWhileRunning = false;
            report.Checks.Add("5 CPS global cap slowed a 100 CPS request; lifetime loop budget stopped at exactly three received clicks. Startup countdown and idle time did not count; Windows accepted the scoped keep-awake request and the run released it.");
            runtime.ActiveTimeLimit = runtime.Snapshot.TotalActiveTime + TimeSpan.FromMilliseconds(150);
            using var budgetBlocked = new ManualResetEventSlim();
            var budgetBlock = app.Dispatcher.InvokeAsync(() => { budgetBlocked.Set(); Thread.Sleep(350); });
            Require(budgetBlocked.Wait(2000), "Budget UI block not entered.");
            Require(runtime.TryStart(hold), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token, TimeSpan.FromSeconds(1));
            Require(runtime.Snapshot.InputsSent == 2 && !runtime.Snapshot.CleanupPending && !budgetBlock.Task.IsCompleted, "Active-time budget depended on WPF or failed cleanup.");
            await budgetBlock;
            runtime.ActiveTimeLimit = TimeSpan.MaxValue;
            report.Checks.Add("Active-time budget ended an indefinite key hold while WPF was blocked; F9 independently cancelled a fake pending shutdown. No OS shutdown was invoked.");
            report.Stage = "Sequence conditions through the shared executor";
            var conditionPolicy = new ConditionPolicy(TimeSpan.FromMilliseconds(100), 2);
            var colourReceiptBefore = await Request(pipe, "state");
            var pixelCondition = new ConditionDefinition(ConditionKind.PixelColor, conditionPolicy, new(capturePoint, 1, 1), 0xD84A31, 2);
            var afterCondition = SequenceStep.Input("Tap after matching red", InputControl.Key(0x12), TimeSpan.FromMilliseconds(20));
            Require(runtime.TryStart(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Red patch", pixelCondition), afterCondition]))), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.State == "Completed" && runtime.Snapshot.InputsSent == 2, "Matching visual condition did not authorize the following action.");
            var absentColour = pixelCondition with { Kind = ConditionKind.ColorDisappears, Color = 0 };
            Require(runtime.TryStart(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Black absent", absentColour), afterCondition]))), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.State == "Completed" && runtime.Snapshot.InputsSent == 2 && !runtime.Snapshot.CleanupPending,
                "Colour-disappears condition did not continue with a clean tap.");
            var conditionTestReceipt = await Request(pipe, "state");
            var colourReceiptWait = Stopwatch.StartNew();
            while ((conditionTestReceipt.KeyDownEvents < colourReceiptBefore.KeyDownEvents + 2
                || conditionTestReceipt.KeyUpEvents < colourReceiptBefore.KeyUpEvents + 2) && colourReceiptWait.Elapsed < TimeSpan.FromSeconds(3))
            { await Task.Delay(50, deadline.Token); conditionTestReceipt = await Request(pipe, "state"); }
            Require(conditionTestReceipt.KeyDownEvents == colourReceiptBefore.KeyDownEvents + 2
                && conditionTestReceipt.KeyUpEvents == colourReceiptBefore.KeyUpEvents + 2, "Colour-wait taps were not received/released exactly once each.");
            var matchedTest = runtime.TestCondition(pixelCondition, deadline.Token);
            Require(matchedTest.Outcome == ConditionOutcome.Matched && matchedTest.Samples >= 2 && !runtime.Snapshot.Busy,
                "Single condition test did not confirm a stable colour match.");
            bool abortedTest = false;
            using (var conditionStop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
            {
                conditionStop.CancelAfter(TimeSpan.FromMilliseconds(600));
                try { runtime.TestCondition(pixelCondition with { Kind = ConditionKind.ColorDisappears }, conditionStop.Token); }
                catch (OperationCanceledException) { abortedTest = true; }
            }
            var conditionTestAfter = await Request(pipe, "state");
            Require(abortedTest && !runtime.Snapshot.Busy && !runtime.Snapshot.CleanupPending && runtime.Snapshot.InputsSent == 0
                && conditionTestAfter.Count == conditionTestReceipt.Count && conditionTestAfter.KeyDownEvents == conditionTestReceipt.KeyDownEvents
                && conditionTestAfter.KeyUpEvents == conditionTestReceipt.KeyUpEvents && conditionTestAfter.ButtonDownEvents == conditionTestReceipt.ButtonDownEvents,
                "Condition-only test emitted input or failed to abort/clean up.");
            report.Checks.Add("Colour disappears continued to a clean tap; condition-only test confirmed a stable match, kept scanning a still-present colour until cancellation, and stayed input-free after abort.");
            // A wait has no time limit: a colour that never appears keeps the run waiting, input-free, until Stop.
            var wrongColour = pixelCondition with { Color = 0, Policy = conditionPolicy with { StableSamples = 1 } };
            Require(runtime.TryStart(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Impossible black", wrongColour), afterCondition]))), runtime.Snapshot.Reason);
            await Task.Delay(TimeSpan.FromSeconds(2), deadline.Token);
            Require(runtime.Snapshot.Busy && runtime.Snapshot.InputsSent == 0, "An unmatched wait ended by itself or sent input.");
            runtime.Stop("Unmatched wait reviewed; stop the fixture.");
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 0, "Stopping an unmatched wait sent input.");
            await app.Dispatcher.InvokeAsync(() => banner.Activate());
            await WaitFor(() => ProbeNative.GetForegroundWindow() == bannerHandle, deadline.Token);
            var activeWindow = new ConditionDefinition(ConditionKind.WindowActive, conditionPolicy);
            Require(runtime.TryStart(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Foreground", activeWindow), afterCondition]))), runtime.Snapshot.Reason);
            await Task.Delay(150, deadline.Token);
            Require(!runtime.Snapshot.Busy && runtime.Snapshot.InputsSent == 0 && !runtime.Snapshot.CleanupPending,
                "Inactive foreground session did not reject playback without input.");
            await Request(pipe, "focus");
            Require(runtime.TryStart(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Window active", new(ConditionKind.WindowActive, conditionPolicy)), afterCondition]))), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.State == "Completed" && runtime.Snapshot.InputsSent == 2, "Window-active wait did not continue after fresh foreground observations.");
            final = await Request(pipe, "state");
            Require(final.KeysDown == "None observed" && final.ButtonsDown == "None observed", "Condition checks left input held.");
            report.Checks.Add("Fresh stable red-pixel wait continued to one key tap; a wrong-colour wait kept waiting with zero input until stopped. Inactive concurrent foreground session rejected input; explicit refocus/restart permitted the legacy window-active wait.");
            report.Stage = "Explicit template capture and image conditions";
            var imageRegion = new CaptureRegion(capturePoint, 8, 8);
            var capturedTemplate = ImageTemplate.FromSample(runtime.CaptureTemplate(imageRegion, deadline.Token));
            Require(!runtime.Snapshot.Busy && !runtime.Snapshot.CleanupPending, "Template capture did not leave the runtime idle.");
            var imageCondition = new ConditionDefinition(ConditionKind.ImageAppears, conditionPolicy, imageRegion, Tolerance: 2, Template: capturedTemplate);
            Require(runtime.TryStart(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Captured patch", imageCondition), afterCondition]))), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.State == "Completed" && runtime.Snapshot.InputsSent == 2, "Captured template failed to match its region.");
            var missingTemplate = ImageTemplate.FromSample(new(8, 8, new byte[8 * 8 * 4]));
            var disappeared = imageCondition with { Kind = ConditionKind.ImageDisappears, Template = missingTemplate };
            Require(runtime.TryStart(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Black absent", disappeared), afterCondition]))), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.State == "Completed" && runtime.Snapshot.InputsSent == 2, "Absent image did not continue after fresh observations.");
            var repeatedPixel = ImageTemplate.FromSample(new(1, 1, capturedTemplate.Decode().Pixels[..4].ToArray()));
            Require(runtime.TryStart(SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Ambiguous patch", imageCondition with { Template = repeatedPixel }), afterCondition]))), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.InputsSent == 0 && runtime.Snapshot.State == "Stopped", "Ambiguous image authorized input.");
            report.Checks.Add("Explicit template capture remained input-free; captured image and absent-image waits each continued to a clean tap. Multiple image matches stopped with zero subsequent input. No captured pixels persisted in the probe.");
            report.Stage = "Whole-sequence repetition";
            var loopSource = new SequenceDefinition([afterCondition], Repetition: new(3, TimeSpan.FromMilliseconds(20)));
            Require(runtime.TryStart(SequenceCompiler.Compile(loopSource)), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.CompletedCycles == 3 && runtime.Snapshot.InputsSent == 6, "Whole sequence loop count was incorrect.");
            var longBody = afterCondition with { Duration = TimeSpan.FromMilliseconds(150) };
            Require(runtime.TryStart(SequenceCompiler.Compile(loopSource with { Steps = [longBody], Repetition = new(0, ActiveDuration: TimeSpan.FromMilliseconds(50)) })), runtime.Snapshot.Reason);
            await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
            Require(runtime.Snapshot.CompletedCycles == 0 && runtime.Snapshot.InputsSent == 2 && !runtime.Snapshot.CleanupPending, "Sequence time limit did not release its partial hold.");
            report.Checks.Add("Whole-sequence count completed exactly three loops; active-duration limit released a partial hold with zero completed loops.");
            report.Stage = "Failing runtime observers cannot prevent stop";
            Action failingObserver = () => throw new InvalidOperationException("Deliberate runtime observer failure");
            runtime.Changed += failingObserver; runtime.StopIssued += failingObserver;
            try
            {
                Require(runtime.TryStart(SequenceCompiler.Compile(new([longBody]))), runtime.Snapshot.Reason);
                await WaitFor(() => runtime.Snapshot.InputsSent == 1, deadline.Token);
                runtime.Stop("Observer recovery test.");
                await WaitFor(() => !runtime.Snapshot.Busy, deadline.Token);
                Require(!runtime.Snapshot.CleanupPending && runtime.NotificationFailures >= 2, "Observer failure prevented cleanup.");
            }
            finally { runtime.Changed -= failingObserver; runtime.StopIssued -= failingObserver; }
            report.Checks.Add("Throwing status and stop observers were contained; active input still cancelled and released through the normal cleanup path.");
            final = await Request(pipe, "state");
            Require(final.KeysDown == "None observed" && final.ButtonsDown == "None observed", "Advanced flow left input held.");
            report.Stage = "Control shutdown settles pending settings";
            var pendingSettings = Enumerable.Range(0, 8).Select(_ => runtime.Control.ConfigureBindingsAsync([])).ToArray();
            runtime.Control.Dispose();
            foreach (var setting in pendingSettings)
            {
                try { await setting.WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
            Require(pendingSettings.All(t => t.IsCompleted), "Control shutdown left a settings request pending.");
            bool rejectedAfterClose = false;
            try { await runtime.Control.ConfigureAsync(0x77, 0x78); }
            catch (ObjectDisposedException) { rejectedAfterClose = true; }
            Require(rejectedAfterClose, "Disposed control accepted a new settings request.");
            report.Checks.Add("All eight queued settings requests settled after control disposal; later configuration was rejected. No input was owned during this lifecycle check.");
            report.Stage = "Native callback exception containment";
            using (var faultControl = new DesktopControl())
            {
                Require(faultControl.Available, "Fault probe could not register its controls.");
                int notified = 0;
                faultControl.StopRequested += _ => throw new InvalidOperationException("Deliberate failing observer");
                faultControl.StopRequested += _ => Interlocked.Increment(ref notified);
                faultControl.WindowChanged += (_, _) => throw new InvalidOperationException("Deliberate native callback failure");
                await app.Dispatcher.InvokeAsync(() => banner.Activate());
                await WaitFor(() => !faultControl.Available && Volatile.Read(ref notified) > 0, deadline.Token);
                Require(faultControl.Error?.Contains("Restart") == true, "Control failure did not explain recovery.");
            }
            report.Checks.Add("A deliberate window callback exception retired the control route; a failing stop observer did not prevent the next cleanup observer. The native boundary remained alive and no input was dispatched.");
            report.FinalReceiverCount = final.Count;
            report.ReceiverSamples.Add($"Final: clicks={final.Count}, keyDown={final.KeyDownEvents}, keyUp={final.KeyUpEvents}, wheel={final.WheelDelta}, keys={final.KeysDown}");
            report.Stage = "Completed"; report.Passed = true;
        }
        catch (Exception ex) { report.Error = ex.GetType().Name + ": " + ex.Message + " Runtime: " + runtime.Snapshot; }
        finally
        {
            runtime.Stop("Probe ending.");
            try { await WaitFor(() => !runtime.Snapshot.Busy, CancellationToken.None, TimeSpan.FromSeconds(2)); } catch (TimeoutException) { }
            if (overlay is not null) await app.Dispatcher.InvokeAsync(() => { overlay.Close(); });
            if (target is not null)
            {
                try { await Request(pipe, "close"); } catch (Exception) { }
                if (!target.WaitForExit(2000)) target.Kill();
                target.Dispose();
            }
            report.FinishedAtUtc = DateTimeOffset.UtcNow;
        }
        return report;
    }
}
