using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Engine;

public sealed record SessionAction(SequencePlan Plan, string Name, Guid MacroId, TimeSpan Countdown, CancellationTokenSource Cancellation);

/// <summary>Bounded requests/reservations for the existing scheduler, not an input-dispatch thread.</summary>
public sealed class ActionToggleSession : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, NamedHelper> holds = [];
    private readonly HashSet<Guid> stopping = [];
    private CancellationTokenSource changed = new();
    private SessionAction? action;
    private bool taken, closed;
    public bool LastActionCancelled { get; private set; }
    public bool ActionBusy { get { lock (gate) return action is not null; } }
    public Guid ActionId { get { lock (gate) return action?.MacroId ?? Guid.Empty; } }
    public string? ActionName { get { lock (gate) return action?.Name; } }
    public Guid[] ActiveIds { get { lock (gate) return holds.Keys.ToArray(); } }
    public InputControl[] ActiveControls { get { lock (gate) return holds.Values.Select(h => h.Plan.Control).Concat(action?.Plan.AllInputs().Select(i => i.Control) ?? []).Distinct().ToArray(); } }
    public CancellationToken Changed { get { lock (gate) return changed.Token; } }
    internal bool KeepHold(InputControl input) { lock (gate) return holds.Any(h => h.Value.Plan.Mode == HelperMode.Hold && h.Value.Plan.Control == input && !stopping.Contains(h.Key)); }
    public bool Toggle(NamedHelper helper)
    {
        lock (gate)
        {
            if (closed) return false;
            if (holds.ContainsKey(helper.Id)) { stopping.Add(helper.Id); changed.Cancel(); return true; }
            if (action?.Plan.LeaveCursorFree == true) throw new ArgumentException("Stop cursor-free Actions before starting a key hold.");
            if (helper.Plan is not { Mode: HelperMode.Hold or HelperMode.Repeat, Count: 0, ActiveDuration: null, Control.Kind: InputKind.ScanCode or InputKind.MouseButton }
                || helper.Plan.Validate().Count != 0 || helper.Plan.Position is not null || helper.Plan.LeaveCursorFree)
                throw new ArgumentException("Choose a valid until-stopped hold or repeat.");
            if (holds.Values.Any(h => Conflicts(h.Plan.Control, helper.Plan.Control)) || action?.Plan.AllInputs().Any(i => Conflicts(i.Control, helper.Plan.Control)) == true)
                throw new ArgumentException("This hold conflicts with Actions or another toggle. Modifiers affect other held keys; pointer actions share the cursor.");
            holds.Add(helper.Id, helper); changed.Cancel(); return true;
        }
    }
    private static bool Conflicts(InputControl a, InputControl b) => a == b || a.Kind is not InputKind.ScanCode && b.Kind is not InputKind.ScanCode
        || SequenceKeys.IsModifier(a) && b.Kind is InputKind.ScanCode or InputKind.Unicode
        || SequenceKeys.IsModifier(b) && a.Kind is InputKind.ScanCode or InputKind.Unicode;
    public bool Start(SequencePlan plan, string name, TimeSpan countdown, Guid macroId = default)
    {
        lock (gate)
        {
            if (closed || action is not null) return false;
            if (plan.LeaveCursorFree && holds.Count != 0) throw new ArgumentException("Stop active toggles before starting cursor-free Actions.");
            if (countdown < TimeSpan.Zero || countdown > TimeSpan.FromMinutes(1)) throw new ArgumentException("Choose a countdown up to 60 seconds.");
            if (plan.AllInputs().Any(i => holds.Values.Any(h => Conflicts(i.Control, h.Plan.Control))))
                throw new ArgumentException("Actions conflicts with an active toggle. Stop that toggle first. Modifiers affect other held keys; pointer actions share the cursor.");
            action = new(plan, name, macroId, countdown, new()); taken = false; changed.Cancel(); return true;
        }
    }
    public bool StopAction()
    {
        lock (gate) { if (action is null) return false; _ = action.Cancellation.CancelAsync(); changed.Cancel(); return true; }
    }
    public void Retain(IEnumerable<Guid> enabled)
    {
        lock (gate) { var ids = enabled.ToHashSet(); foreach (var id in holds.Keys.Where(id => !ids.Contains(id))) stopping.Add(id); changed.Cancel(); }
    }
    internal (NamedHelper[] Holds, Guid[] Stopping, CancellationToken Changed) Read()
    {
        lock (gate)
        {
            if (changed.IsCancellationRequested) { changed.Dispose(); changed = new(); }
            return (holds.Values.ToArray(), stopping.ToArray(), changed.Token);
        }
    }
    internal void Released(Guid id) { lock (gate) { holds.Remove(id); stopping.Remove(id); } }
    internal SessionAction? TakeAction() { lock (gate) { if (taken || action is null) return null; taken = true; return action; } }
    internal void CompletedAction() { lock (gate) { LastActionCancelled = action?.Cancellation.IsCancellationRequested == true; action?.Cancellation.Dispose(); action = null; taken = false; } }
    internal bool CloseIfEmpty() { lock (gate) { if (holds.Count > 0 || action is not null) return false; closed = true; return true; } }
    public void Dispose() { lock (gate) { closed = true; action?.Cancellation.Dispose(); action = null; holds.Clear(); stopping.Clear(); changed.Dispose(); } }
}
