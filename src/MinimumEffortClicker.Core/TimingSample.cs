namespace MinimumEffortClicker.Core;

/// <summary>The one place a random sample becomes a duration or an offset, so every timing feature samples the same way.</summary>
public static class TimingSample
{
    /// <summary>A number in [0, 1) from the injected source; anything else is a broken source, not a value to clamp.</summary>
    public static double Unit(Func<double> random)
    {
        var sample = random();
        if (!double.IsFinite(sample) || sample < 0 || sample >= 1) throw new InvalidOperationException("Invalid timing sample.");
        return sample;
    }
    /// <summary>Uniform between the minimum and the optional maximum; the minimum itself when there is no range.</summary>
    public static TimeSpan Range(TimeSpan minimum, TimeSpan? maximum, Func<double> random)
        => maximum is null || maximum == minimum ? minimum : minimum + TimeSpan.FromTicks((long)((maximum.Value - minimum).Ticks * Unit(random)));
}
