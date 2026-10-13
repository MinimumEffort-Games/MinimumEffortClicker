namespace MinimumEffortClicker.Core;

/// <summary>Bounded capture segments. Pauses close holds for review; resuming never joins an interrupted drag.</summary>
public sealed class RecordingSession
{
    private readonly object gate = new();
    private readonly List<CaptureBuffer> segments = [];
    private CaptureBuffer? current;
    private TimeSpan segmentStart;
    private bool closed;
    private int completedCount;
    public RecordingSession() { current = new(); segments.Add(current); }
    public int Count { get { lock (gate) return completedCount + (current?.Count ?? 0); } }
    public bool Paused { get { lock (gate) return !closed && current is null; } }
    public bool TryAppend(RecordedEdge edge)
    {
        lock (gate) return !closed && current is not null && current.TryAppend(edge with { At = edge.At - segmentStart });
    }
    public void Pause(TimeSpan at)
    {
        lock (gate)
        {
            if (closed || current is null) return;
            current.Close(at - segmentStart); completedCount += current.Count; current = null;
        }
    }
    public void Resume(TimeSpan at)
    {
        lock (gate)
        {
            if (closed || current is not null) throw new InvalidOperationException("Capture is not paused.");
            if (completedCount >= 4096 || segments.Count >= 32) throw new InvalidOperationException("Capture reached its event or pause limit. Stop and review this recording.");
            current = new(4096 - completedCount); segments.Add(current); segmentStart = at;
        }
    }
    public void Close(TimeSpan at) { lock (gate) { Pause(at); closed = true; } }
    public RecordingDraft Normalize()
    {
        CaptureBuffer[] copy;
        lock (gate) { if (!closed) throw new InvalidOperationException("Stop capture before review."); copy = segments.ToArray(); }
        var drafts = copy.Select(s => s.Normalize()).ToArray();
        var sequence = new SequenceDefinition(drafts.SelectMany(d => d.Sequence.Steps).ToArray());
        SequenceCompiler.Compile(sequence);
        return new(sequence, drafts.SelectMany(d => d.Warnings).Distinct().ToArray());
    }
}
