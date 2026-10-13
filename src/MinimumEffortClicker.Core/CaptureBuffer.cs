namespace MinimumEffortClicker.Core;

/// <summary>Bounded explicit-recording storage. Append does no normalization, I/O or UI work.</summary>
public sealed class CaptureBuffer
{
    private readonly object gate = new();
    private readonly RecordedEdge[] edges;
    private int count;
    private bool closed;
    private TimeSpan end;
    public CaptureBuffer(int capacity = 4096)
    {
        if (capacity is < 1 or > SequenceCompiler.MaximumActions) throw new ArgumentOutOfRangeException(nameof(capacity));
        edges = new RecordedEdge[capacity];
    }
    public int Count { get { lock (gate) return count; } }
    public bool TryAppend(RecordedEdge edge)
    {
        lock (gate)
        {
            if (closed || count == edges.Length) return false;
            edges[count++] = edge; return true;
        }
    }
    public void Close(TimeSpan endedAt) { lock (gate) { if (closed) return; closed = true; end = endedAt; } }
    public RecordingDraft Normalize()
    {
        RecordedEdge[] copy; TimeSpan endedAt;
        lock (gate)
        {
            if (!closed) throw new InvalidOperationException("Stop recording before normalization.");
            copy = edges.Take(count).ToArray(); endedAt = end;
        }
        return RecordingNormalizer.Normalize(copy, endedAt);
    }
}
