using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class SequenceTests
{
    [TestMethod]
    public void RemovedInputKindsAndStepKindsAreRejected()
    {
        // Numeric values 4 and 5 (Unicode text, logical key) and step kinds 4–6 were retired in schema 16.
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Input("raw", new((InputKind)4, 65), TimeSpan.FromMilliseconds(10))])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Wait(TimeSpan.Zero) with { Kind = (StepKind)6 }])));
        Assert.IsNotEmpty(new HelperPlan(new((InputKind)5, 65), HelperMode.Repeat, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10), 1).Validate());
        var disabled = SequenceStep.Wait(TimeSpan.FromMilliseconds(5)) with { Enabled = false };
        Assert.IsEmpty(SequenceCompiler.Compile(new([disabled])).Inputs);
        Assert.AreEqual(TimeSpan.Zero, SequenceCompiler.Compile(new([disabled])).Duration);
    }

    private static readonly InputControl W = InputControl.Key(0x11), Space = InputControl.Key(0x39);
    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    private static RecordingDraft Overlap() => RecordingNormalizer.Normalize([
        new(Ms(100), W, true), new(Ms(200), Space, true), new(Ms(250), Space, false),
        new(Ms(400), Space, true), new(Ms(450), Space, false), new(Ms(1000), W, false)], Ms(1200));

    [TestMethod]
    public void ChordOrdersModifierDownFirstAndReleaseLastAtSharedBoundaries()
    {
        var shift = InputControl.Key(0x2A);
        var group = SequenceStep.Group("Shift W", Ms(100), [new(Guid.NewGuid(), Ms(0), W, Ms(100)), new(Guid.NewGuid(), Ms(0), shift, Ms(100))]);
        var plan = SequenceCompiler.Compile(new([group], "04090409"));
        CollectionAssert.AreEqual(new[] { shift, W, W, shift }, plan.Inputs.Select(i => i.Control).ToArray());
        Assert.AreEqual("04090409", plan.KeyboardLayout);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([group], "unknown")));
    }

    [TestMethod]
    public void FunctionKeysAndModifiersCanBeAuthoredButWindowsKeysStayExcluded()
    {
        foreach (int code in new[] { 0x3B, 0x43, 0x57 })
            Assert.HasCount(2, SequenceCompiler.Compile(new([SequenceStep.Input("Function key", InputControl.Key(code), Ms(10))])).Inputs);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Input("Windows key", InputControl.Key(0x15B), Ms(10))])));
        Assert.IsEmpty(new HelperPlan(InputControl.Key(0x1D), HelperMode.Hold, Ms(100), Ms(20), 0).Validate());
    }

    [TestMethod]
    public void EditsAndHistoryRetainRecordedKeyboardLayout()
    {
        var original = new SequenceDefinition([SequenceStep.Wait(Ms(100))], "04090409");
        var history = new SequenceHistory(original);
        history.Commit(SequenceEdits.ChangeIdleGaps(history.Current, _ => Ms(10)));
        Assert.AreEqual(original.KeyboardLayout, history.Current.KeyboardLayout);
        history.Commit(SequenceEdits.Replace(history.Current, 0, 1, [SequenceStep.Wait(Ms(20))]));
        history.Undo(); history.Redo(); Assert.AreEqual(original.KeyboardLayout, history.Current.KeyboardLayout);
    }

    [TestMethod]
    public void FiniteHelperConversionRetainsCadenceAndPressDuration()
    {
        var helper = new HelperPlan(W, HelperMode.Repeat, Ms(100), Ms(20), 3);
        var plan = SequenceCompiler.Compile(SequenceCompiler.FromHelper(helper));
        CollectionAssert.AreEqual(new double[] { 0, 20, 100, 120, 200, 220 }, plan.Inputs.Select(x => x.Offset.TotalMilliseconds).ToArray());
        var repeating = SequenceCompiler.Compile(SequenceCompiler.FromHelper(helper with { Count = 0 }));
        Assert.AreEqual(0, repeating.Repetition.Count);
        Assert.AreEqual(Ms(100), repeating.Duration);
        CollectionAssert.AreEqual(new double[] { 0, 20 }, repeating.Inputs.Select(x => x.Offset.TotalMilliseconds).ToArray());
        var timed = SequenceCompiler.Compile(SequenceCompiler.FromHelper(helper with { Count = 0, ActiveDuration = Ms(205) }));
        Assert.AreEqual(Ms(205), timed.Duration);
    }

    [TestMethod]
    public void NormalizePreservesWAcrossSpaceTaps()
    {
        var draft = Overlap(); Assert.HasCount(0, draft.Warnings);
        CollectionAssert.AreEqual(new[] { StepKind.Wait, StepKind.TimedGroup, StepKind.Wait }, draft.Sequence.Steps.Select(s => s.Kind).ToArray());
        var plan = SequenceCompiler.Compile(draft.Sequence);
        CollectionAssert.AreEqual(new double[] { 100, 200, 250, 400, 450, 1000 }, plan.Inputs.Select(e => e.Offset.TotalMilliseconds).ToArray());
        Assert.AreEqual(W, plan.Inputs[0].Control); Assert.AreEqual(W, plan.Inputs[^1].Control);
    }

    [TestMethod]
    public void RemovingIdleGapsDoesNotShortenAnOverlappingHold()
    {
        var original = Overlap().Sequence;
        var transformed = SequenceEdits.ChangeIdleGaps(original, _ => TimeSpan.Zero);
        var plan = SequenceCompiler.Compile(transformed);
        Assert.AreEqual(Ms(900), plan.Duration);
        Assert.AreEqual(Ms(900), plan.Inputs.Last(x => x.Control == W).Offset);
        CollectionAssert.AreEqual(original.Steps[1].Children!.ToArray(), transformed.Steps[1].Children!.ToArray());
    }

    [TestMethod]
    public void ConflictingOverlapAndInvalidTimeAreRejected()
    {
        var group = SequenceStep.Group("Conflict", Ms(100), [new(Guid.NewGuid(), Ms(0), W, Ms(90)), new(Guid.NewGuid(), Ms(10), W, Ms(90))]);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([group])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Input("Zero hold", W, TimeSpan.Zero)])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Wait(TimeSpan.FromDays(2))])));
    }

    [TestMethod]
    public void LateCaptureStopProducesReviewableBoundedHoldWarning()
    {
        var draft = RecordingNormalizer.Normalize([new(Ms(10), W, true)], Ms(1000));
        Assert.HasCount(1, draft.Warnings);
        Assert.AreEqual(Ms(990), draft.Sequence.Steps.Last().Duration);
        Assert.AreEqual(Ms(1000), SequenceCompiler.Compile(draft.Sequence).Duration);
    }

    [TestMethod]
    public void CorruptCaptureIsRejectedInsteadOfGuessingPairings()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RecordingNormalizer.Normalize([new(Ms(10), W, false)], Ms(20)));
        Assert.ThrowsExactly<ArgumentException>(() => RecordingNormalizer.Normalize([new(Ms(10), W, true), new(Ms(11), W, true)], Ms(20)));
        Assert.ThrowsExactly<ArgumentException>(() => RecordingNormalizer.Normalize([new(Ms(10), W, true), new(Ms(9), W, false)], Ms(20)));
    }

    [TestMethod]
    public void PartialReplacementIsUndoableAndDoesNotMutateCompiledRun()
    {
        var source = Overlap().Sequence; var running = SequenceCompiler.Compile(source);
        var history = new SequenceHistory(source);
        history.Commit(SequenceEdits.Replace(history.Current, 1, 1, [SequenceStep.Input("Tap Space", Space, Ms(40))]));
        Assert.HasCount(2, SequenceCompiler.Compile(history.Current).Inputs);
        Assert.HasCount(6, running.Inputs);
        history.Undo(); Assert.HasCount(6, SequenceCompiler.Compile(history.Current).Inputs);
        history.Redo(); Assert.HasCount(2, SequenceCompiler.Compile(history.Current).Inputs);
        history.Undo(); history.Commit(SequenceEdits.ChangeIdleGaps(history.Current, _ => Ms(50)));
        Assert.IsFalse(history.CanRedo);
    }

    [TestMethod]
    public void HistoryBoundsAndSnapshotDoNotRetainMutableCallerLists()
    {
        var steps = new List<SequenceStep> { SequenceStep.Wait(Ms(1)) };
        var history = new SequenceHistory(new(steps)); steps.Clear(); Assert.HasCount(1, history.Current.Steps);
        for (int i = 0; i < 100; i++) history.Commit(new([SequenceStep.Wait(Ms(i))]));
        int count = 0; while (history.CanUndo) { history.Undo(); count++; }
        Assert.AreEqual(50, count);
    }

    [TestMethod]
    public void CaptureOverflowRetainsBoundedReviewableDraftAndRejectsFurtherInput()
    {
        var buffer = new CaptureBuffer(2);
        Assert.IsTrue(buffer.TryAppend(new(Ms(10), W, true)));
        Assert.IsTrue(buffer.TryAppend(new(Ms(20), Space, true)));
        Assert.IsFalse(buffer.TryAppend(new(Ms(30), Space, false)));
        buffer.Close(Ms(30));
        Assert.IsFalse(buffer.TryAppend(new(Ms(40), W, false)));
        var draft = buffer.Normalize(); Assert.AreEqual(2, buffer.Count); Assert.HasCount(1, draft.Warnings);
        Assert.AreEqual(Ms(30), SequenceCompiler.Compile(draft.Sequence).Duration);
    }
}
