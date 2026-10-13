namespace MinimumEffortClicker.Core;

/// <summary>A toggle or helper with a display name, as handed to the runtime session.</summary>
public sealed record NamedHelper(Guid Id, string Name, HelperPlan Plan);
