namespace MinimumEffortClicker.Core;

public sealed record ClickRateLimit(int ClicksPerSecond = 20)
{
    public TimeSpan MinimumGap => ClicksPerSecond is >= 1 and <= 100
        ? TimeSpan.FromSeconds(1d / ClicksPerSecond) : throw new ArgumentException("Choose a click limit from 1 to 100 per second.");
}
