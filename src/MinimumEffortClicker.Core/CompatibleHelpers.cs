namespace MinimumEffortClicker.Core;

public sealed record NamedHelper(Guid Id, string Name, HelperPlan Plan);

/// <summary>A bounded, explicitly reviewed set for one foreground target. Sequences are exclusive.</summary>
public sealed class CompatibleHelpers
{
    public const int MaximumHelpers = 4;
    private CompatibleHelpers(NamedHelper[] helpers) => Helpers = Array.AsReadOnly(helpers);
    public IReadOnlyList<NamedHelper> Helpers { get; }
    public static CompatibleHelpers Compile(IEnumerable<NamedHelper> helpers)
    {
        ArgumentNullException.ThrowIfNull(helpers);
        var copy = helpers.Take(MaximumHelpers + 1).ToArray();
        if (copy.Length is < 1 or > MaximumHelpers) throw new ArgumentException("Choose one to four simple helpers.");
        var ids = new HashSet<Guid>(); var keys = new HashSet<InputControl>(); bool pointer = false;
        foreach (var item in copy)
        {
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 200
                || item.Plan is null || item.Plan.Validate().Count != 0) throw new ArgumentException("Each helper needs a unique identity and valid simple action.");
            if (!keys.Add(item.Plan.Control)) throw new ArgumentException($"'{item.Name}' conflicts with another helper on the same input.");
            if (SequenceKeys.IsModifier(item.Plan.Control)) throw new ArgumentException("Run modifier holds separately from legacy helper sets.");
            if (item.Plan.LeaveCursorFree) throw new ArgumentException("Run cursor-free clicks on their own, outside a combined helper set.");
            if (item.Plan.Control.Kind != InputKind.ScanCode)
            {
                if (pointer) throw new ArgumentException("Only one pointer or scroll helper may run in a compatible set.");
                pointer = true;
            }
        }
        return new(copy);
    }
}
