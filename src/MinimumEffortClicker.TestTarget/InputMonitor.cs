using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

namespace MinimumEffortClicker.TestTarget;

/// <summary>In-memory receiver model. No OS input, persistence, networking or global observation.</summary>
public sealed class InputMonitor : INotifyPropertyChanged
{
    public const int LogCapacity = 200;
    private readonly HashSet<string> keys = new(StringComparer.Ordinal);
    private readonly HashSet<string> buttons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> clicks = new(StringComparer.Ordinal);
    private readonly ObservableCollection<string> events = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long sequence;
    private readonly bool deferViewUpdates;
    private bool viewDirty;

    public InputMonitor(bool deferViewUpdates = false)
    { this.deferViewUpdates = deferViewUpdates; Events = new ReadOnlyObservableCollection<string>(events); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ViewInvalidated;
    public ReadOnlyObservableCollection<string> Events { get; }
    public IReadOnlyList<string> VisibleEvents { get; private set; } = [];
    public void RefreshView()
    {
        if (!viewDirty) return;
        viewDirty = false; VisibleEvents = events.ToArray();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public bool WindowActive { get; private set; }
    public bool AreaFocused { get; private set; }
    public int ClickCount { get; private set; }
    public int ButtonDownEvents { get; private set; }
    public int ButtonUpEvents { get; private set; }
    public int PointerLeaveEvents { get; private set; }
    public int PointerMoves { get; private set; }
    public int DragMoves { get; private set; }
    public string PointerPosition { get; private set; } = "Not observed";
    private TimeSpan lastPointerView;
    public void PointerMoved(int x, int y)
    {
        PointerMoves++; if (buttons.Count > 0) DragMoves++;
        PointerPosition = $"{x}, {y}";
        if (clock.Elapsed - lastPointerView < TimeSpan.FromMilliseconds(50)) return;
        lastPointerView = clock.Elapsed; Log($"Pointer: {x}, {y}; held: {ButtonsDown}");
    }
    public int KeyDownEvents { get; private set; }
    public int KeyUpEvents { get; private set; }
    public int WheelDelta { get; private set; }
    public int HorizontalWheelDelta { get; private set; }
    public void HorizontalWheel(int delta)
    { if (AreaFocused) { HorizontalWheelDelta += delta; Log($"Horizontal wheel: {delta}"); } }
    public string FocusStatus => $"Window: {(WindowActive ? "active" : "inactive")} · Input area: {(AreaFocused ? "focused" : "not focused")}";
    public string KeysDown => keys.Count == 0 ? "None observed" : string.Join(", ", keys.Order());
    public string ButtonsDown => buttons.Count == 0 ? "None observed" : string.Join(", ", buttons.Order());
    public string ClickSummary => clicks.Count == 0 ? "No clicks yet" : string.Join(" · ", clicks.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value}"));

    public void SetFocus(bool windowActive, bool areaFocused)
    {
        if (WindowActive == windowActive && AreaFocused == areaFocused) return;
        WindowActive = windowActive;
        AreaFocused = windowActive && areaFocused;
        if (!AreaFocused)
        {
            keys.Clear();
            buttons.Clear();
        }
        Log(FocusStatus + (AreaFocused ? "" : "; observed holds cleared (physical state unknown)"));
    }

    public void KeyDown(string key, bool repeat)
    {
        if (!AreaFocused) return;
        if (!repeat) KeyDownEvents++;
        keys.Add(key);
        Log($"Key down: {key}{(repeat ? " (repeat)" : "")}");
    }

    public void KeyUp(string key)
    {
        if (keys.Remove(key)) { KeyUpEvents++; Log($"Key up: {key}"); }
    }

    public void ButtonDown(string button)
    {
        ButtonDownEvents++;
        if (!AreaFocused) return;
        buttons.Add(button);
        Log($"Button down: {button}");
    }

    public void ButtonUp(string button, bool insideArea)
    {
        ButtonUpEvents++;
        if (!buttons.Remove(button)) return;
        if (insideArea && AreaFocused)
        {
            ClickCount++;
            clicks[button] = clicks.GetValueOrDefault(button) + 1;
        }
        Log($"Button up: {button}{(insideArea ? "" : " (outside area; no click)")}");
    }

    public void PointerLeft()
    {
        PointerLeaveEvents++;
        if (buttons.Count == 0) return;
        buttons.Clear();
        Log("Pointer left input area; observed button holds cleared (physical state unknown).");
    }

    public void Wheel(int delta)
    {
        if (AreaFocused) { WheelDelta += delta; Log($"Wheel: {delta}"); }
    }

    public void Reset()
    {
        keys.Clear();
        buttons.Clear();
        clicks.Clear();
        ClickCount = 0;
        ButtonDownEvents = 0;
        ButtonUpEvents = 0;
        PointerLeaveEvents = 0; PointerMoves = DragMoves = 0; PointerPosition = "Not observed"; lastPointerView = default;
        KeyDownEvents = 0; KeyUpEvents = 0; WheelDelta = 0; HorizontalWheelDelta = 0;
        events.Clear();
        sequence = 0;
        clock.Restart();
        Log("Cleared counters and session log. Held state must be observed again.");
    }

    private void Log(string message)
    {
        if (events.Count == LogCapacity) events.RemoveAt(0);
        events.Add($"{++sequence:000000}  {clock.Elapsed.TotalSeconds:F3}s  {message}");
        bool wasDirty = viewDirty; viewDirty = true;
        if (!deferViewUpdates) RefreshView();
        else if (!wasDirty) ViewInvalidated?.Invoke();
    }
}
