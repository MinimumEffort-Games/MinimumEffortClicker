using System.Diagnostics;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Linux;

// Opt-in native probe for the Linux runtime. It sends real input, so it refuses to run anywhere but
// the private headless GNOME Shell started by scripts/linux/run-headless-probe.sh.
string display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
if (args is not ["--isolated-headless-shell"] || Environment.GetEnvironmentVariable("MEC_PROBE_SHELL") != "headless" || !display.StartsWith("mec-probe-", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Refusing to run: this probe only runs inside scripts/linux/run-headless-probe.sh.");
    return 2;
}

var failures = new List<string>();
void Check(bool condition, string what) { Console.WriteLine($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0:0.000} " + (condition ? "PASS " : "FAIL ") + what); if (!condition) failures.Add(what); }
bool WaitFor(Func<bool> condition, int milliseconds = 5000)
{
    var watch = Stopwatch.StartNew();
    while (watch.ElapsedMilliseconds < milliseconds) { if (condition()) return true; Thread.Sleep(20); }
    return condition();
}

// Each helper call on its own, so a failure names the exact call.
using (var helper = DesktopHelper.Connect())
{
    void Try(string name, Func<object?> call)
    {
        try { Console.WriteLine($"PASS helper {name}: {call()}"); }
        catch (Exception ex) { Console.WriteLine($"FAIL helper {name}: {ex.GetType().Name} {ex.Message}"); failures.Add("helper " + name); }
    }
    Try("GetPointer", () => helper.GetPointer());
    Try("GetMonitors", () => string.Join(";", helper.GetMonitors()));
    Try("ListWindows", () => string.Join(";", helper.ListWindows()));
    Try("GetFocusWindow", () => helper.GetFocusWindow());
    Try("QueryTarget", () => helper.QueryTarget(0));
    Try("WindowAt", () => helper.WindowAt(10, 10, DesktopRuntime.OverlayTitle));
    Try("GetKeyboardLayout", () => helper.GetKeyboardLayout());
    Try("GrabAccelerator", () => { uint a = helper.GrabAccelerator("F8"); helper.UngrabAccelerator(a); return a; });
    // The pointer starts in the hot corner; a move out of it can be clipped by GNOME's corner barrier once.
    Try("PointerMove", () => { helper.PointerMove(640.5, 400.5); Thread.Sleep(20); helper.PointerMove(640.5, 400.5); return helper.GetPointer(); });
}

using var runtime = new DesktopRuntime();
Console.WriteLine("Platform: " + runtime.Control.PlatformSummary);
Check(runtime.Control.Helper is not null, "GNOME helper connected and allowed this probe");
Check(runtime.Control.Available, "global controls available (" + runtime.Control.Error + ")");
var frame = runtime.DesktopFrame();
Check(frame.Width > 0 && frame.Height > 0, $"desktop frame {frame.X},{frame.Y} {frame.Width}x{frame.Height}");

var target = runtime.ListTargets().FirstOrDefault(t => t.Title == "MEC Probe Target");
Check(target is not null, "probe target window listed");
if (target is null) return 1;
runtime.SelectTarget(target);
Check(runtime.BringTargetForward(), "target brought forward");
var client = runtime.GetTargetFrame();
Console.WriteLine($"Target client area {client.X},{client.Y} {client.Width}x{client.Height} dpi {client.Dpi}");

// The headless shell starts in the overview; let its closing animation finish before clicking.
Check(WaitFor(() => runtime.Control.Helper!.GetShellState().InputReady), "shell ready for input");
Console.WriteLine("Before playback: " + runtime.Control.Helper!.QueryTarget(target.Handle) + " shell " + runtime.Control.Helper!.GetShellState());
var point = new ClientPoint(120, 90, client.Width, client.Height, client.Dpi);
var steps = new List<SequenceStep>
{
    QuickActions.Click(PointerButton.Left, QuickClickKind.Single, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(50), point),
    QuickActions.Click(PointerButton.Right, QuickClickKind.Double, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(50), point with { X = 200, Y = 150 }),
    QuickActions.Keys([0x2A, SequenceKeys.Supported["W"]], TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(50), false),
    QuickActions.Keys([SequenceKeys.Supported["F2"]], TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(50), false)
};
var plan = SequenceCompiler.Compile(new(steps, Repetition: new(1)));
var watching = true;
var watcher = new Thread(() =>
{
    string last = "";
    while (Volatile.Read(ref watching))
    {
        string now = runtime.Control.Helper!.GetShellState().ToString() + " focus=" + runtime.Control.Helper!.GetFocusWindow();
        if (now != last) Console.WriteLine($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0:0.000} shell {now}");
        last = now; Thread.Sleep(10);
    }
});
watcher.Start();
Check(runtime.TryStartQuick(plan, steps.Count, TimeSpan.Zero, false), "playback started: " + runtime.Snapshot.Reason);
Check(WaitFor(() => !runtime.Snapshot.Busy), "playback finished");
var snapshot = runtime.Snapshot;
Console.WriteLine($"State {snapshot.State}: {snapshot.Reason} · inputs {snapshot.InputsSent}");
Check(snapshot.State == "Completed", "playback completed");
 Volatile.Write(ref watching, false); watcher.Join();
Console.WriteLine("After playback: " + runtime.Control.Helper!.QueryTarget(target.Handle) + " shell " + runtime.Control.Helper!.GetShellState());

// A loop that would run forever must stop promptly on Stop All and release what it holds.
var hold = SequenceCompiler.Compile(new([QuickActions.Click(PointerButton.Left, QuickClickKind.Hold, TimeSpan.FromSeconds(30), TimeSpan.Zero, TimeSpan.Zero, point)], Repetition: new(0)));
Check(runtime.TryStartQuick(hold, 1, TimeSpan.Zero, false), "long hold started");
Thread.Sleep(400);
var stopped = Stopwatch.StartNew();
runtime.Stop("Probe Stop All.");
Check(WaitFor(() => !runtime.Snapshot.Busy, 2000), $"Stop All ended the hold in {stopped.ElapsedMilliseconds} ms");
Console.WriteLine($"State {runtime.Snapshot.State}: {runtime.Snapshot.Reason} · inputs {runtime.Snapshot.InputsSent}");
Check(!runtime.Snapshot.CleanupPending, "held button released after Stop All");

// A looping run whose window target goes behind another window pauses and continues by itself once the
// target is back in front for half a second. A pause the user asked for stays paused.
var other = runtime.ListTargets().FirstOrDefault(t => t.Title == "MEC Probe Other");
Check(other is not null, "second probe window listed");
if (other is not null)
{
    runtime.BringTargetForward();
    Check(WaitFor(() => runtime.Control.Helper!.GetFocusWindow() == target.Handle), "target in front before the loop");
    var loop = SequenceCompiler.Compile(new([QuickActions.Click(PointerButton.Left, QuickClickKind.Single, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(80), TimeSpan.Zero, point)], Repetition: new(0)));
    Check(runtime.TryStartQuick(loop, 1, TimeSpan.Zero, false), "loop started: " + runtime.Snapshot.Reason);
    Check(WaitFor(() => runtime.Snapshot.InputsSent >= 4), "loop clicking");
    runtime.Control.Helper!.ActivateWindow(other.Handle);
    Check(WaitFor(() => runtime.Snapshot.State == "Paused"), $"loop paused when the target went behind ({runtime.Snapshot.State}: {runtime.Snapshot.Reason})");
    Check(runtime.Snapshot.Reason.Contains("continues by itself", StringComparison.Ordinal), "pause says it continues by itself");
    long pausedAt = runtime.Snapshot.InputsSent;
    Thread.Sleep(800);
    Check(runtime.Snapshot.Busy && runtime.Snapshot.InputsSent == pausedAt, "no input while the target is behind");
    var back = Stopwatch.StartNew();
    runtime.Control.Helper!.ActivateWindow(target.Handle);
    Check(WaitFor(() => runtime.Snapshot.State == "Running" && runtime.Snapshot.InputsSent > pausedAt), $"loop continued by itself after {back.ElapsedMilliseconds} ms ({runtime.Snapshot.State}: {runtime.Snapshot.Reason})");
    Check(back.ElapsedMilliseconds >= 450, "waited about half a second before continuing");
    Check(runtime.Pause(), "user pause accepted");
    Check(WaitFor(() => runtime.Snapshot.State == "Paused"), "user pause took effect");
    runtime.Control.Helper!.ActivateWindow(other.Handle); Thread.Sleep(300);
    runtime.Control.Helper!.ActivateWindow(target.Handle);
    long userPausedAt = runtime.Snapshot.InputsSent;
    Thread.Sleep(1500);
    Check(runtime.Snapshot.State == "Paused" && runtime.Snapshot.InputsSent == userPausedAt, "a user pause stays paused when the window comes back");
    runtime.Stop("Probe Stop All.");
    Check(WaitFor(() => !runtime.Snapshot.Busy, 2000), "loop stopped");
    Check(!runtime.Snapshot.CleanupPending, "nothing held after the focus-loss loop");
}

// Colour sampling of the probe target's known fill (#3366CC), from the window's own pixels.
runtime.BringTargetForward();
var region = new CaptureRegion(new ClientPoint(100, 200, client.Width, client.Height, client.Dpi), 4, 4);
var colour = runtime.CaptureTemplate(region, CancellationToken.None);
var pixel = colour.Pixels.ToArray();
Check(colour.Width == 4 && colour.Height == 4, "captured a 4×4 client region");
Check(Math.Abs(pixel[2] - 0x33) <= 8 && Math.Abs(pixel[1] - 0x66) <= 8 && Math.Abs(pixel[0] - 0xCC) <= 8, $"captured the target fill colour (#{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2})");
var shot = DesktopScreenshot.Capture(runtime.Control.Helper!, CancellationToken.None);
Check(shot.Bgr32Pixels.Length == shot.Bounds.Width * shot.Bounds.Height * 4, $"desktop screenshot {shot.Bounds.Width}x{shot.Bounds.Height}");

runtime.UseDesktop();
// Desktop mode: a colour wait in desktop pixels reads the screen on whichever monitor the area is on.
var monitors = runtime.ListMonitors();
Check(monitors.Count >= 1 && monitors[0].Primary, "monitors listed, main first: " + string.Join("; ", monitors));
var desktop = runtime.GetTargetFrame();
var onScreen = new CaptureRegion(new ClientPoint(client.X + 100, client.Y + 200, desktop.Width, desktop.Height, desktop.Dpi, CoordinateSpace.Desktop), 4, 4);
var fill = new ConditionDefinition(ConditionKind.RegionColor, new(TimeSpan.FromMilliseconds(100)), onScreen, 0x3366CC, 8);
var seen = runtime.TestCondition(fill, CancellationToken.None);
Check(seen.Outcome == ConditionOutcome.Matched, "Desktop-mode colour wait saw the target fill on the monitor (" + seen.Outcome + ")");
var picture = runtime.CaptureMonitorPreview(monitors[0], CancellationToken.None);
Check(picture.Width == monitors[0].Width && picture.Pixels.Length == picture.Width * picture.Height * 4, $"monitor picture {picture.Width}x{picture.Height}");
// The probe shell has a second, empty monitor: a wait there for the colour its picture shows is met, read from that monitor.
if (monitors.Count > 1)
{
    var second = runtime.CaptureMonitorPreview(monitors[1], CancellationToken.None);
    Check(second.Width == monitors[1].Width && second.Height == monitors[1].Height, $"second monitor picture {second.Width}x{second.Height} at {monitors[1].X},{monitors[1].Y}");
    int at = (300 * second.Width + 10) * 4;
    int background = second.Pixels[at + 2] << 16 | second.Pixels[at + 1] << 8 | second.Pixels[at];
    var there = fill with { Region = new(new ClientPoint(monitors[1].X + 10, monitors[1].Y + 300, desktop.Width, desktop.Height, desktop.Dpi, CoordinateSpace.Desktop), 1, 1), Color = background, Kind = ConditionKind.PixelColor };
    var met = runtime.TestCondition(there, CancellationToken.None);
    Check(met.Outcome == ConditionOutcome.Matched && background != 0x3366CC, $"a wait on the second monitor saw its colour #{background:X6} ({met.Outcome})");
}
Console.WriteLine(failures.Count == 0 ? "PROBE PASSED" : "PROBE FAILED: " + string.Join("; ", failures));
return failures.Count == 0 ? 0 : 1;
