using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class RecordingSessionTests
{
    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
    [TestMethod]
    public void PauseTruncatesHoldAndResumeExcludesPausedTime()
    {
        var session = new RecordingSession(); var w = InputControl.Key(0x11);
        Assert.IsTrue(session.TryAppend(new(Ms(10), w, true)));
        session.Pause(Ms(110));
        Assert.IsTrue(session.Paused); Assert.IsFalse(session.TryAppend(new(Ms(150), w, false)));
        session.Resume(Ms(1000));
        Assert.IsTrue(session.TryAppend(new(Ms(1010), w, true)));
        Assert.IsTrue(session.TryAppend(new(Ms(1060), w, false)));
        session.Close(Ms(1100));
        var draft = session.Normalize(); var plan = SequenceCompiler.Compile(draft.Sequence);
        Assert.AreEqual(Ms(210), plan.Duration); Assert.HasCount(1, draft.Warnings);
        CollectionAssert.AreEqual(new double[] { 10, 110, 120, 170 }, plan.Inputs.Select(x => x.Offset.TotalMilliseconds).ToArray());
    }
    [TestMethod]
    public void RepeatedPausesAndClosedCaptureStayBounded()
    {
        var session = new RecordingSession();
        for (int i = 0; i < 31; i++) { session.Pause(Ms(i * 2 + 1)); session.Resume(Ms(i * 2 + 2)); }
        session.Pause(Ms(63));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Resume(Ms(64)));
        session.Close(Ms(100));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Resume(Ms(101)));
        Assert.HasCount(0, SequenceCompiler.Compile(session.Normalize().Sequence).Inputs);
    }
    [TestMethod]
    public void CopyRegeneratesEveryIdentityAndPreservesCalibrationAndNotes()
    {
        var point = new ClientPoint(10, 20, 800, 600, 96);
        var group = SequenceStep.Group("Click", Ms(100), [new(Guid.NewGuid(), Ms(0), InputControl.Mouse(PointerButton.Left), Ms(100), point, point)]) with { Notes = "Review destination" };
        var copy = SequenceEdits.CopyWithNewIds([group]).Single();
        Assert.AreNotEqual(group.Id, copy.Id); Assert.AreNotEqual(group.Children![0].Id, copy.Children![0].Id);
        Assert.AreEqual(point, copy.Children[0].Position); Assert.AreEqual(group.Notes, copy.Notes);
        Assert.HasCount(4, SequenceCompiler.Compile(new([group, copy])).Inputs);
    }
}
