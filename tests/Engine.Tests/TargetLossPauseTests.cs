using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

/// <summary>
/// A run whose window target goes behind another window waits for it (an automatic pause) instead of ending, and carries on
/// where it was once the runtime resumes it. A pause the user asks for keeps the old rules.
/// </summary>
[TestClass]
public sealed class TargetLossPauseTests
{
    private static readonly InputControl W = InputControl.Key(0x11);
    private static readonly InputControl Left = InputControl.Mouse(PointerButton.Left);

    [TestMethod]
    public void ALoopKeepsGoingWhenTheTargetLeavesAndComesBack()
    {
        var f = new Fixture(); int waits = 0; bool sawAutomatic = false;
        f.Waits.Before = _ => { if (++waits == 4) f.SetAvailable(false); };
        f.Executor.PlaybackPaused = paused =>
        {
            if (!paused) return;
            sawAutomatic = f.Playback.Automatic;
            f.SetAvailable(true); Assert.IsTrue(f.Playback.RequestResume());
        };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", W, Ms(20))], Repetition: new(5, Ms(50))));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(5, result.CompletedCycles);
        Assert.IsTrue(sawAutomatic);
        Assert.AreEqual(f.Input.Accepted.Count(i => i.Edge == InputEdge.Down), f.Input.Accepted.Count(i => i.Edge == InputEdge.Up));
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void AClickCaughtMidPressFinishesThereAndTheRunContinues()
    {
        var f = new Fixture(); bool lost = false;
        f.Waits.Before = _ => { if (!lost && f.Executor.Owns(Left)) { lost = true; f.SetAvailable(false); f.Playback.RequestPause(automatic: true); } };
        f.Executor.PlaybackPaused = paused => { if (paused) { f.SetAvailable(true); f.Playback.RequestResume(); } };
        SequenceStep Click() => QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(0), Ms(100), new(100, 100, 800, 600, 96));
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([Click(), Click()])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.IsTrue(lost);
        // The interrupted click is released once and never pressed again; the second click still happens.
        Assert.AreEqual(2, f.Input.Accepted.Count(i => i.Control == Left && i.Edge == InputEdge.Down));
        Assert.AreEqual(2, f.Input.Accepted.Count(i => i.Control == Left && i.Edge == InputEdge.Up));
        Assert.AreEqual(2, f.Executor.TotalCompletedClicks);
    }

    [TestMethod]
    public void AUserPauseMidClickStillEndsTheRunForReview()
    {
        var f = new Fixture(); bool paused = false;
        f.Waits.Before = _ => { if (!paused && f.Executor.Owns(Left)) { paused = true; f.Playback.RequestPause(); } };
        var click = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(0), Ms(100), new(100, 100, 800, 600, 96));
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([click])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Paused, result.Outcome);
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void AColourWaitWaitsForTheTargetInsteadOfStopping()
    {
        var f = new Fixture(); int observations = 0;
        f.Executor.EvaluateCondition = (_, _) =>
        {
            // The first look fails because the target went behind another window.
            if (++observations == 1) { f.SetAvailable(false); return new(ConditionOutcome.Unavailable, 1, 1); }
            return new(ConditionOutcome.Matched, 1, 1);
        };
        f.Executor.PlaybackPaused = paused => { if (paused) { f.SetAvailable(true); f.Playback.RequestResume(); } };
        var colour = new ConditionDefinition(ConditionKind.PixelColor, new(Ms(100), TimeSpan.FromSeconds(1)),
            new CaptureRegion(new(10, 10, 800, 600, 96), 1, 1), 0xFF0000, 8);
        var plan = SequenceCompiler.Compile(new([SequenceStep.WaitUntil("Red", colour), SequenceStep.Input("W", W, Ms(20))]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(2, observations);
        Assert.AreEqual(2, f.Input.Accepted.Count);
    }

    [TestMethod]
    public void WithoutPauseOnTargetLossALostTargetStillEndsTheRun()
    {
        var f = new Fixture(); f.Executor.PauseOnTargetLoss = false; int waits = 0;
        f.Waits.Before = _ => { if (++waits == 2) f.SetAvailable(false); };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", W, Ms(20))], Repetition: new(5, Ms(50))));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.TargetUnavailable, result.Outcome);
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void TheUserCanTurnAnAutomaticPauseIntoTheirOwn()
    {
        using var control = new PlaybackControl();
        Assert.IsTrue(control.RequestPause(automatic: true));
        Assert.IsTrue(control.Automatic);
        Assert.IsTrue(control.MakeManual());
        Assert.IsFalse(control.Automatic);
        Assert.IsFalse(control.MakeManual());
    }

    private static TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);

    private sealed class Wait(FakeClock clock) : IWaitStrategy
    {
        public Action<TimeSpan>? Before { get; set; }
        public void WaitUntil(TimeSpan due, CancellationToken stop)
        { Before?.Invoke(due); stop.ThrowIfCancellationRequested(); if (due > clock.Elapsed) clock.Advance(due - clock.Elapsed); }
    }

    private sealed class Fixture
    {
        public readonly FakeClock Clock = new();
        public readonly FakeForegroundProvider Target = new() { Current = new(Guid.NewGuid(), true) };
        public readonly FakeInputSink Input = new();
        public readonly Wait Waits;
        public readonly ClickExecutor Executor;
        public readonly PlaybackControl Playback = new();
        public readonly Guid TargetId;
        public Fixture()
        {
            TargetId = Target.Current!.InstanceId;
            Waits = new(Clock);
            Executor = new(Input, Clock, Waits, Target) { Playback = Playback, PauseOnTargetLoss = true };
        }
        public void SetAvailable(bool available) => Target.Current = new(TargetId, available);
    }
}
