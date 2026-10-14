using System.Diagnostics;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Windows;

public sealed record RecordingChannels(bool Keyboard = true, bool MouseButtons = false, bool Scroll = false, bool PointerPath = false);

/// <summary>Explicit bounded capture. No global history exists before Start or after Stop.</summary>
public sealed class DesktopRecorder : IDisposable
{
    private readonly DesktopRuntime runtime;
    private readonly RecordingSession buffer = new();
    private readonly object captureGate = new();
    private readonly TaskCompletionSource<string> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long origin = Stopwatch.GetTimestamp();
    private int ended, active;
    private readonly RecordingChannels channels;
    private readonly string? keyboardLayout;
    private ClientPoint? frame;
    private TimeSpan lastMove;
    private readonly HashSet<InputControl> mouseHolds = [];
    private readonly Dictionary<InputControl, ClientPoint> clickPositions = [];
    private readonly HashSet<InputControl> ignoredClicks = [];
    private readonly HashSet<InputControl> dragged = [];
    // Copy of the captured edges per segment, so the UI can show actions while recording continues.
    private readonly List<(TimeSpan Start, TimeSpan? End, List<RecordedEdge> Edges)> live = [(TimeSpan.Zero, null, [])];
    private int liveVersion;
    /// <summary>Changes whenever an input is captured, paused or resumed.</summary>
    public int LiveVersion => Volatile.Read(ref liveVersion);
    public Task<string> Finished => finished.Task;
    public int Count => buffer.Count;
    public bool Paused => buffer.Paused;

    public DesktopRecorder(DesktopRuntime runtime, RecordingChannels? channels = null)
    {
        this.runtime = runtime;
        this.channels = channels ?? new();
        if (!this.channels.Keyboard && !this.channels.MouseButtons && !this.channels.Scroll && !this.channels.PointerPath)
            throw new ArgumentException("Choose at least one recording channel.");
        runtime.Control.PhysicalChanged += Capture;
        runtime.Control.MouseObserved += CaptureMouse;
        try
        {
            runtime.BeginCapture(Stop, Pause);
            keyboardLayout = this.channels.Keyboard ? runtime.TargetKeyboardLayout : null;
            if (Volatile.Read(ref ended) == 0)
            { Volatile.Write(ref active, 1); }
        }
        catch { runtime.Control.PhysicalChanged -= Capture; runtime.Control.MouseObserved -= CaptureMouse; throw; }
    }
    private void CaptureMouse(PhysicalMouse sample)
    {
        lock (captureGate) CaptureMouseCore(sample);
    }
    private void CaptureMouseCore(PhysicalMouse sample)
    {
        if (Volatile.Read(ref ended) != 0 || Volatile.Read(ref active) == 0) return;
        if (runtime.Control.IsShortcutInput(sample.Control)) return;
        bool movement = sample.Control.Kind == InputKind.PointerMove;
        if (sample.Control.Kind == InputKind.MouseButton && !channels.MouseButtons || sample.Control.Kind is InputKind.Scroll or InputKind.HorizontalScroll && !channels.Scroll) return;
        var at = Stopwatch.GetElapsedTime(origin);
        // Ignore free pointer movement; retain only button-held drag paths.
        if (movement && (!channels.PointerPath || mouseHolds.Count == 0)) return;
        // Do not turn interacting with the clicker into a macro that opens/stops itself.
        if (sample.Control.Kind == InputKind.MouseButton)
        {
            if (sample.Down && runtime.IsDesktopMode && runtime.IsOwnPoint(sample.X, sample.Y)) { ignoredClicks.Add(sample.Control); return; }
            if (!sample.Down && ignoredClicks.Remove(sample.Control)) return;
        }
        var point = runtime.CapturePoint(sample.X, sample.Y);
        if (!sample.Down && clickPositions.TryGetValue(sample.Control, out var pressedAt)
            && (!channels.PointerPath || !dragged.Contains(sample.Control) && point is not null && Math.Abs(point.X - pressedAt.X) <= 3 && Math.Abs(point.Y - pressedAt.Y) <= 3)) point = pressedAt;
        if (point is null || frame is { } calibration && !point.Matches(calibration.Width, calibration.Height, calibration.Dpi))
        { Pause("Pointer left the target, or target size/DPI changed. Finish with the recording hotkey and review the draft."); return; }
        frame ??= point;
        if (movement && at - lastMove < TimeSpan.FromMilliseconds(50)) return;
        if (movement)
        {
            if (dragged.Count == 0 && clickPositions.Values.All(p => Math.Abs(p.X - point.X) <= 3 && Math.Abs(p.Y - point.Y) <= 3)) return;
            lastMove = at; dragged.UnionWith(mouseHolds);
        }
        if (sample.Control.Kind == InputKind.MouseButton)
        {
            if (sample.Down) { if (mouseHolds.Count == 0) lastMove = at - TimeSpan.FromMilliseconds(50); mouseHolds.Add(sample.Control); clickPositions[sample.Control] = point; }
            else { mouseHolds.Remove(sample.Control); clickPositions.Remove(sample.Control); dragged.Remove(sample.Control); }
        }
        if (!Append(new(at, sample.Control, sample.Down, point))) Pause("Recording reached its event limit. Finish with the recording hotkey to retain the bounded draft.");
    }
    private void Capture(InputControl input, bool down)
    {
        lock (captureGate) CaptureCore(input, down);
    }
    private void CaptureCore(InputControl input, bool down)
    {
        if (!channels.Keyboard || Volatile.Read(ref ended) != 0 || Volatile.Read(ref active) == 0 || input.Kind != InputKind.ScanCode) return;
        if (runtime.Control.IsShortcutInput(input)) return; // Exclude actual controls, not every F-key.
        if (!runtime.CaptureTargetAvailable) { Pause("Target lost focus. Resume recording explicitly when ready."); return; }
        if (!string.Equals(keyboardLayout, runtime.TargetKeyboardLayout, StringComparison.OrdinalIgnoreCase))
        { Pause("Target keyboard layout changed. Restore the recorded layout before resuming."); return; }
        if (!SequenceKeys.Supported.Values.Contains(input.Code))
        {
            if (down) Pause("Unsupported key or modifier encountered. Finish with the recording hotkey and review the draft.");
            return;
        }
        if (!Append(new(Stopwatch.GetElapsedTime(origin), input, down))) Pause("Recording reached its event limit. Finish with the recording hotkey to retain the bounded draft.");
    }
    private bool Append(RecordedEdge edge)
    {
        if (!buffer.TryAppend(edge)) return false;
        var segment = live[^1]; segment.Edges.Add(edge with { At = edge.At - segment.Start });
        Interlocked.Increment(ref liveVersion); return true;
    }
    /// <summary>
    /// Actions captured so far, normalized like the final draft. Holds still down end now; the final
    /// draft is taken from the recording itself when it stops.
    /// </summary>
    public SequenceDefinition LivePreview()
    {
        lock (captureGate)
        {
            var now = Stopwatch.GetElapsedTime(origin);
            var steps = new List<SequenceStep>();
            foreach (var (start, end, edges) in live)
            {
                try { steps.AddRange(RecordingNormalizer.Normalize(edges, (end ?? now) - start).Sequence.Steps); }
                catch (ArgumentException) { }
            }
            return new(steps);
        }
    }
    public void Stop(string reason = "Recording stopped by you.")
    {
        lock (captureGate) StopCore(reason);
    }
    private void StopCore(string reason)
    {
        if (Interlocked.Exchange(ref ended, 1) != 0) return;
        buffer.Close(Stopwatch.GetElapsedTime(origin));
        runtime.Control.PhysicalChanged -= Capture;
        runtime.Control.MouseObserved -= CaptureMouse;
        runtime.EndCapture(reason);
        finished.TrySetResult(reason);
    }
    public void Pause(string reason = "Recording paused by you.")
    {
        lock (captureGate)
        {
            if (Volatile.Read(ref ended) != 0 || Interlocked.Exchange(ref active, 0) == 0) return;
            var at = Stopwatch.GetElapsedTime(origin); buffer.Pause(at);
            live[^1] = live[^1] with { End = at }; Interlocked.Increment(ref liveVersion); mouseHolds.Clear(); clickPositions.Clear(); dragged.Clear(); ignoredClicks.Clear();
            runtime.CapturePaused(reason + " Interrupted holds end at this boundary; review before playback.");
        }
    }
    public void Resume()
    {
        lock (captureGate)
        {
            if (Volatile.Read(ref ended) != 0 || !buffer.Paused) throw new InvalidOperationException("Recording is not paused.");
            runtime.ValidateCaptureResume();
            if (channels.Keyboard && !string.Equals(keyboardLayout, runtime.TargetKeyboardLayout, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Restore the recorded keyboard layout before resuming this capture.");
            var at = Stopwatch.GetElapsedTime(origin); buffer.Resume(at);
            live.Add((at, null, [])); Interlocked.Increment(ref liveVersion);
            Volatile.Write(ref active, 1); runtime.CaptureResumed();
        }
    }
    public Task<RecordingDraft> GetDraftAsync() => Task.Run(() =>
    {
        var draft = buffer.Normalize();
        return draft with { Sequence = draft.Sequence with { KeyboardLayout = keyboardLayout } };
    });
    public void Dispose() { Stop(); }
}
