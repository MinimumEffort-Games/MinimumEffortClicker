using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class RecordingLabelsTests
{
    [TestMethod]
    public void ActionsKeepOnlyPausesBetweenRecordedInputs()
    {
        var mouse = InputControl.Mouse(PointerButton.Left);
        var recording = RecordingNormalizer.Normalize([
            new(TimeSpan.FromMinutes(1), mouse, true), new(TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(80), mouse, false),
            new(TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(280), mouse, true), new(TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(360), mouse, false)
        ], TimeSpan.FromMinutes(2)).Sequence;
        var actions = QuickActions.RecordedActions(recording, true);
        // The minute before the first click is dropped; the wait after the last one (until recording stopped) is kept,
        // so a looped recording repeats at the recorded pace.
        Assert.HasCount(4, actions.Steps);
        Assert.AreEqual(StepKind.Input, actions.Steps[0].Kind);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), actions.Steps[1].Duration);
        Assert.AreEqual(StepKind.Input, actions.Steps[2].Kind);
        Assert.AreEqual(TimeSpan.FromSeconds(60) - TimeSpan.FromMilliseconds(360), actions.Steps[3].Duration);
        Assert.AreEqual(TimeSpan.FromSeconds(60), SequenceCompiler.Compile(actions).Duration);
        Assert.HasCount(2, QuickActions.RecordedActions(recording, false).Steps);
    }
    [TestMethod]
    public void MouseOnlyActionsDoNotRequireAKeyboardLayoutButKeyRecordingsDo()
    {
        var mouse = new SequenceDefinition([SequenceStep.Input("Click", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(80))], "04090409");
        Assert.IsNull(QuickActions.RecordedActions(mouse, true).KeyboardLayout);
        var keys = mouse with { Steps = [SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(80))] };
        Assert.AreEqual("04090409", QuickActions.RecordedActions(keys, true).KeyboardLayout);
    }
    [TestMethod]
    public void ShortClickAndKeyPressRemainEditableAndKeepExactDuration()
    {
        var point = new ClientPoint(10, 20, 800, 600, 96);
        var mouse = InputControl.Mouse(PointerButton.Left);
        var key = InputControl.Key(0x11);
        var draft = RecordingNormalizer.Normalize([
            new(TimeSpan.Zero, mouse, true, point), new(TimeSpan.FromMilliseconds(80), mouse, false, point),
            new(TimeSpan.FromMilliseconds(100), key, true), new(TimeSpan.FromMilliseconds(160), key, false)], TimeSpan.FromMilliseconds(160));
        var inputs = draft.Sequence.Steps.Where(s => s.Kind == StepKind.Input).ToArray();
        Assert.AreEqual("Left click", inputs[0].Name);
        Assert.AreEqual("Press W", inputs[1].Name);
        Assert.AreEqual(TimeSpan.FromMilliseconds(80), inputs[0].Duration);
        Assert.AreEqual(point, inputs[0].EndPosition);
        Assert.HasCount(0, draft.Warnings);
    }
    [TestMethod]
    public void LongAndInterruptedPressesRemainHolds()
    {
        var mouse = InputControl.Mouse(PointerButton.Left);
        var complete = RecordingNormalizer.Normalize([new(TimeSpan.Zero, mouse, true), new(TimeSpan.FromSeconds(1), mouse, false)], TimeSpan.FromSeconds(1));
        Assert.AreEqual("Hold Left", complete.Sequence.Steps[0].Name);
        var interrupted = RecordingNormalizer.Normalize([new(TimeSpan.Zero, mouse, true)], TimeSpan.FromMilliseconds(50));
        Assert.AreEqual("Hold Left", interrupted.Sequence.Steps[0].Name);
        Assert.HasCount(1, interrupted.Warnings);
    }
    [TestMethod]
    public void SessionCanStayOpenBeyondFiveMinutesUntilExplicitStop()
    {
        var session = new RecordingSession();
        var mouse = InputControl.Mouse(PointerButton.Left);
        Assert.IsTrue(session.TryAppend(new(TimeSpan.FromMinutes(6), mouse, true)));
        Assert.IsTrue(session.TryAppend(new(TimeSpan.FromMinutes(6) + TimeSpan.FromMilliseconds(80), mouse, false)));
        session.Close(TimeSpan.FromMinutes(7));
        Assert.AreEqual(TimeSpan.FromMinutes(7), SequenceCompiler.Compile(session.Normalize().Sequence).Duration);
    }

    [TestMethod]
    public void DragIsOneEditableGroupAndKeepsPathEvenWithoutRecordedPauses()
    {
        var point = new ClientPoint(10, 10, 800, 600, 96); var mouse = InputControl.Mouse(PointerButton.Left);
        var edges = new RecordedEdge[] { new(default, mouse, true, point),
            new(TimeSpan.FromMilliseconds(50), new(InputKind.PointerMove, 0), true, point with { X = 50 }),
            new(TimeSpan.FromMilliseconds(100), new(InputKind.PointerMove, 0), true, point with { Y = 60 }),
            new(TimeSpan.FromMilliseconds(200), mouse, false, point) };
        var steps = QuickActions.RecordedActions(RecordingNormalizer.Normalize(edges, TimeSpan.FromMilliseconds(300)).Sequence, false, true).Steps;
        Assert.HasCount(1, steps); Assert.AreEqual("Drag Left", steps[0].Name);
        Assert.HasCount(3, steps[0].Children!); Assert.IsNull(steps[0].Motion);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), steps[0].Duration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), steps[0].Children![1].Offset);
    }
    [TestMethod]
    public void MovementUsesOnlyRetainedPausesAndHonoursTheirRandomTiming()
    {
        var click = SequenceStep.Input("Click", InputControl.Mouse(PointerButton.Left), TimeSpan.FromMilliseconds(20)) with { Position = new(20, 20, 800, 600, 96) };
        var pause = QuickActions.Pause(TimeSpan.FromMilliseconds(100)) with { Timing = new(MaximumPause: TimeSpan.FromMilliseconds(200)) };
        var recording = new SequenceDefinition([click, pause, click with { Id = Guid.NewGuid() }]);
        var actions = QuickActions.RecordedActions(recording, true, true);
        var plan = SequenceCompiler.Compile(actions).SampleTiming(() => 0.5);
        Assert.AreEqual(TimeSpan.FromMilliseconds(150), plan.Inputs[2].PauseBefore);
        Assert.AreEqual(TimeSpan.FromMilliseconds(170), plan.Inputs[2].Offset);
        Assert.AreEqual(TimeSpan.Zero, plan.Inputs[0].PauseBefore);
        Assert.IsTrue(QuickActions.RecordedActions(recording, false, true).Steps.All(s => s.Motion is null));
    }

    [TestMethod]
    public void FastDragWithOnlyAnEndpointIsNotRelabelledAsAnOrdinaryClick()
    {
        var point = new ClientPoint(10, 10, 800, 600, 96); var mouse = InputControl.Mouse(PointerButton.Middle);
        var step = RecordingNormalizer.Normalize([new(default, mouse, true, point), new(TimeSpan.FromMilliseconds(30), mouse, false, point with { X = 90 })], TimeSpan.FromMilliseconds(30)).Sequence.Steps[0];
        Assert.AreEqual("Drag Middle", step.Name); Assert.IsFalse(QuickActions.IsOrdinaryClick(step));
        Assert.AreEqual(90, step.EndPosition!.X);
    }
}
