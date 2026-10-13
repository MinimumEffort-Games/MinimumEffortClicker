namespace MinimumEffortClicker.Core;

public sealed record MacroDefinition(Guid Id, string Name, HelperPlan? Plan, string Notes = "", SequenceDefinition? Sequence = null, bool IsToggle = false)
{
    public override string ToString() => Name;
}

/// <summary>Portable configuration only. Availability, bindings and native target instances live locally.</summary>
public sealed record ProfileDefinition(int SchemaVersion, Guid Id, string Name, IReadOnlyList<MacroDefinition> Macros, IReadOnlyList<NamedPoint>? Points = null)
{
    public const int CurrentVersion = 15;
    public override string ToString() => Name;
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        try { NamedPoint.Index(Points); } catch (ArgumentException ex) { errors.Add(ex.Message); }
        if (SchemaVersion != CurrentVersion || Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 100)
            errors.Add("Invalid or unsupported profile.");
        if (Macros is null || Macros.Count is < 1 or > 100) { errors.Add("A profile needs 1 to 100 macros."); return errors; }
        if (Macros.Select(x => x?.Id).Distinct().Count() != Macros.Count) errors.Add("Macro identities must be unique.");
        foreach (var macro in Macros)
        {
            if (macro is null || macro.Id == Guid.Empty || string.IsNullOrWhiteSpace(macro.Name) || macro.Name.Length > 100
                || macro.Notes is null || macro.Notes.Length > 4000 || (macro.Plan is null) == (macro.Sequence is null))
            { errors.Add("Invalid macro."); continue; }
            if (macro.Plan is { } helper) errors.AddRange(helper.Validate());
            else try { SequenceCompiler.Compile(macro.Sequence!, Points); } catch (ArgumentException ex) { errors.Add(ex.Message); }
            if (macro.IsToggle && (macro.Plan is not { Mode: HelperMode.Hold or HelperMode.Repeat, Control.Kind: InputKind.ScanCode or InputKind.MouseButton, Count: 0, ActiveDuration: null }
                || macro.Plan.Position is not null || macro.Plan.LeaveCursorFree))
                errors.Add("A toggle must hold or repeat one key or mouse button until stopped.");
        }
        return errors;
    }
}
