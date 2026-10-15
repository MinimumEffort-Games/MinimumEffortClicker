using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class RunPauseStatusTests
{
    [TestMethod]
    public void StoppingAPausedRunKeepsTheStopReason()
    {
        // The engine unpauses from its cleanup when a paused run is stopped; the log must still say why it stopped.
        Assert.IsNull(RunPauseStatus.After(paused: false, automatic: true, cancelling: true, "Stopped: left and right mouse buttons pressed together."));
        Assert.AreEqual(("Running", RunPauseStatus.ResumedByUser), RunPauseStatus.After(paused: false, automatic: false, cancelling: false, "Resuming."));
        Assert.AreEqual(("Running", RunPauseStatus.TargetBack),
            RunPauseStatus.After(paused: false, automatic: true, cancelling: false, "The target window is back in front. Continuing."));
        Assert.AreEqual(("Paused", RunPauseStatus.TargetLost), RunPauseStatus.After(paused: true, automatic: true, cancelling: true, "Stopped."));
        Assert.AreEqual(("Paused", RunPauseStatus.PausedByUser), RunPauseStatus.After(paused: true, automatic: false, cancelling: false, ""));
    }
}
