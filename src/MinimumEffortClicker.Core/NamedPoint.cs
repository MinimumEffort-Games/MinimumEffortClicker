namespace MinimumEffortClicker.Core;

public sealed record NamedPoint(Guid Id, string Name, ClientPoint Position)
{
    public override string ToString() => Name;
    public static IReadOnlyDictionary<Guid, NamedPoint> Index(IReadOnlyList<NamedPoint>? points)
    {
        if (points is null) return new Dictionary<Guid, NamedPoint>();
        if (points.Count > 1000 || points.Any(p => p is null || p.Id == Guid.Empty || string.IsNullOrWhiteSpace(p.Name)
            || p.Name.Length > 100 || p.Position is not { IsValid: true })
            || points.Select(p => p.Id).Distinct().Count() != points.Count
            || points.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != points.Count)
            throw new ArgumentException("Named points need unique identities/names and valid calibrated positions (maximum 1,000).");
        return points.ToDictionary(p => p.Id);
    }
}
