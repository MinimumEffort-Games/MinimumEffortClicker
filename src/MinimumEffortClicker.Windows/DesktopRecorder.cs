using System.Diagnostics;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Windows;

/// <param name="Look">Mouse movement while a game holds the cursor, recorded as relative Look steps (User, 2026-10-18).</param>
public sealed record RecordingChannels(bool Keyboard = true, bool MouseButtons = false, bool Scroll = false, bool PointerPath = false, bool Look = false);

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
    // Buttons pressed while a game held the cursor: recorded without a spot, also when released after it lets go.
    private readonly HashSet<InputControl> heldButtons = [];
    // Look movement collected into one step per LookStep, so a 1,000 Hz mouse is not a thousand inputs a second.
    private static readonly TimeSpan LookStep = TimeSpan.FromMilliseconds(16);
    private TimeSpan? lookStart;
    private int lookX, lookY;
    private long heldCheckedAt;
    private bool heldNow;
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
        if (this.channels.Look) runtime.Control.RawMouseMoved += CaptureLook;
        try
        {
            runtime.BeginCapture(Stop, Pause);
            if (this.channels.Look) _ = runtime.Control.WatchRawMouseAsync(true).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            keyboardLayout = this.channels.Keyboard ? runtime.TargetKeyboardLayout : null;
            if (Volatile.Read(ref ended) == 0)
            { Volatile.Write(ref active, 1); }
        }
        catch { runtime.Control.PhysicalChanged -= Capture; runtime.Control.MouseObserved -= CaptureMouse; runtime.Control.RawMouseMoved -= CaptureLook; throw; }
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
        // While a game holds the cursor its position means nothing: pointer movement is recorded as Look, and clicks keep no
        // spot, so playback clicks where the game has the cursor instead of moving it (which would turn the camera).
        bool held = channels.Look && CursorHeld();
        if (held && movement) return;
        if (sample.Control.Kind == InputKind.MouseButton && (held && sample.Down && !mouseHolds.Contains(sample.Control) || !sample.Down && heldButtons.Contains(sample.Control)))
        {
            if (!runtime.CaptureTargetAvailable) { Pause("Target lost focus. Resume recording explicitly when ready."); return; }
            if (sample.Down) heldButtons.Add(sample.Control); else heldButtons.Remove(sample.Control);
            if (!Append(new(at, sample.Control, sample.Down))) Pause("Recording reached its event limit. Finish with the recording hotkey to retain the bounded draft.");
            return;
        }
        // Ignore free pointer movement; retain only button-held drag paths.
        if (movement && (!channels.PointerPath || mouseHolds.Count == 0)) return;
        // Do not turn interacting with the clicker into a macro that opens/stops itself.
        if (sample.Control.Kind == InputKind.MouseButton)
        {
            if (sample.Down && runtime.IsDesktopMode && runtime.IsOwnPoint(sample.X, sample.Y)) { ignoredClicks.Add(sample.Control); return; }
            if (!sample.Down && ignoredClicks.Remove(sample.Control)) return;
        }
        // Scrolling the clicker itself (its Action List) is not part of the macro.
        if (sample.Control.Kind is InputKind.Scroll or InputKind.HorizontalScroll && runtime.IsDesktopMode && runtime.IsOwnPoint(sample.X, sample.Y)) return;
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
        // Movement collected so far comes first, so the edges stay in time order.
        FlushLook();
        return AppendEdge(edge);
    }
    private bool AppendEdge(RecordedEdge edge)
    {
        if (!buffer.TryAppend(edge)) return false;
        var segment = live[^1]; segment.Edges.Add(edge with { At = edge.At - segment.Start });
        Interlocked.Increment(ref liveVersion); return true;
    }
    private void CaptureLook(int dx, int dy)
    {
        lock (captureGate) CaptureLookCore(dx, dy);
    }
    private void CaptureLookCore(int dx, int dy)
    {
        if (Volatile.Read(ref ended) != 0 || Volatile.Read(ref active) == 0) return;
        var at = Stopwatch.GetElapsedTime(origin);
        // Free pointer movement on the desktop is not a look, and a window target has to be in front.
        if (!CursorHeld() || !runtime.CaptureTargetAvailable) { FlushLook(); return; }
        if (lookStart is { } start && at - start >= LookStep) FlushLook();
        lookStart ??= at;
        lookX = Math.Clamp(lookX + dx, short.MinValue, short.MaxValue); lookY = Math.Clamp(lookY + dy, short.MinValue, short.MaxValue);
    }
    /// <summary>Writes the movement collected since the step began as one Look step, timed at its start.</summary>
    private void FlushLook()
    {
        if (lookStart is not { } start) return;
        int x = lookX, y = lookY; lookStart = null; lookX = lookY = 0;
        if ((x != 0 || y != 0) && !AppendEdge(new(start, InputControl.Look(x, y), true)))
            Pause("Recording reached its event limit. Finish with the recording hotkey to retain the bounded draft.");
    }
    /// <summary>Whether the app in front holds the cursor, checked at most every 50 ms (a fast mouse reports far more often).</summary>
    private bool CursorHeld()
    {
        long now = Stopwatch.GetTimestamp();
        if (heldCheckedAt == 0 || Stopwatch.GetElapsedTime(heldCheckedAt, now) >= TimeSpan.FromMilliseconds(50)) { heldNow = DesktopNative.CursorHeld(); heldCheckedAt = now; }
        return heldNow;
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
        FlushLook();
        buffer.Close(Stopwatch.GetElapsedTime(origin));
        runtime.Control.PhysicalChanged -= Capture;
        runtime.Control.MouseObserved -= CaptureMouse;
        if (channels.Look)
        {
            runtime.Control.RawMouseMoved -= CaptureLook;
            _ = runtime.Control.WatchRawMouseAsync(false).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        runtime.EndCapture(reason);
        finished.TrySetResult(reason);
    }
    public void Pause(string reason = "Recording paused by you.")
    {
        lock (captureGate)
        {
            if (Volatile.Read(ref ended) != 0 || Interlocked.Exchange(ref active, 0) == 0) return;
            FlushLook();
            var at = Stopwatch.GetElapsedTime(origin); buffer.Pause(at);
            live[^1] = live[^1] with { End = at }; Interlocked.Increment(ref liveVersion); mouseHolds.Clear(); clickPositions.Clear(); dragged.Clear(); ignoredClicks.Clear(); heldButtons.Clear();
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
