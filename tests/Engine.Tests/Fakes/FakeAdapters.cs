using MinimumEffortClicker.Engine;

namespace Engine.Tests.Fakes;

internal sealed class FakeClock : IClock
{
    public TimeSpan Elapsed { get; private set; }
    public void Advance(TimeSpan amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);
        Elapsed += amount;
    }
}

internal sealed class FakeForegroundProvider : IForegroundTargetProvider
{
    public ForegroundTarget? Current { get; set; }
}

/// <summary>Records into managed memory only, including a deterministic failure after N sends.</summary>
internal sealed class FakeInputSink(int acceptedLimit = int.MaxValue) : IInputSink
{
    private readonly List<InputTransition> accepted = [];
    public IReadOnlyList<InputTransition> Accepted => accepted.AsReadOnly();
    public bool TrySend(InputTransition transition)
    {
        if (accepted.Count >= acceptedLimit) return false;
        accepted.Add(transition);
        return true;
    }
}
