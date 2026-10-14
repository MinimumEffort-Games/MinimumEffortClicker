using System.Diagnostics;
using Engine.Tests.Fakes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace Engine.Tests;

/// <summary>
/// The window target can leave the front between the scheduler's last check and the moment an input goes out. With a
/// window target (PauseOnTargetLoss) that must become the automatic pause, and after it the refused input is sent once,
/// never the whole action again. Releases are never turned into a pause.
/// </summary>
[TestClass]
public sealed class FocusRaceTests
{
    private static readonly InputControl W = InputControl.Key(0x11);
    private static readonly InputControl E = InputControl.Key(0x12);
    private static readonly InputControl Shift = InputControl.Key(0x2A);
    private static readonly InputControl Left = InputControl.Mouse(PointerButton.Left);
    private static readonly ClientPoint Point = new(100, 100, 800, 600, 96);

    [TestMethod]
    public void FocusLostWhileADownGoesOutPausesAndSendsThatInputOnceAfterwards()
    {
        var f = new Fixture();
        // The target leaves exactly while E's press is being sent: the runtime refuses it.
        f.Input.Refuse = t => t.Control == E && t.Edge == InputEdge.Down && f.Refused++ == 0 && f.Lose();
        int pauses = 0; bool automatic = false;
        f.Executor.PlaybackPaused = paused =>
        {
            if (!paused) return;
            pauses++; automatic = f.Playback.Automatic;
            f.SetAvailable(true); Assert.IsTrue(f.Playback.RequestResume());
        };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", W, Ms(20)), SequenceStep.Input("E", E, Ms(20))]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(1, pauses);
        Assert.IsTrue(automatic);
        CollectionAssert.AreEqual(new[] { "W down", "W up", "E down", "E up" }, f.Input.Log());
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void ARefusedSecondPressOfADoubleClickIsSentOnceSoItStaysTwoClicks()
    {
        var f = new Fixture();
        f.Input.Refuse = t => t.Control == Left && t.Edge == InputEdge.Down && t.ClickCount == 2 && f.Refused++ == 0 && f.Lose();
        f.Executor.PlaybackPaused = paused => { if (paused) { f.SetAvailable(true); f.Playback.RequestResume(); } };
        var click = QuickActions.Click(PointerButton.Left, QuickClickKind.Double, Ms(20), Ms(40), Ms(50), Point);
        var result = f.Executor.Execute(SequenceCompiler.Compile(new([click])), f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(2, f.Refused); // Two attempts at the second press: refused once, then sent once.
        var downs = f.Input.Accepted.Where(i => i.Control == Left && i.Edge == InputEdge.Down).ToArray();
        Assert.AreEqual(2, downs.Length);
        CollectionAssert.AreEqual(new[] { 1, 2 }, downs.Select(d => d.ClickCount).ToArray());
        Assert.AreEqual(2, f.Input.Accepted.Count(i => i.Control == Left && i.Edge == InputEdge.Up));
        Assert.AreEqual(2, f.Executor.TotalCompletedClicks);
    }

    [TestMethod]
    public void AHeldKeyIsReleasedDuringThePauseAndTheRefusedKeyIsNotDoubled()
    {
        var f = new Fixture();
        f.Input.Refuse = t => t.Control == W && t.Edge == InputEdge.Down && f.Refused++ == 0 && f.Lose();
        var log = new List<string>();
        f.Executor.PlaybackPaused = paused =>
        {
            if (!paused) return;
            log.AddRange(f.Input.Log()); // What had gone out when the pause began.
            Assert.IsFalse(f.Executor.HasOwnedInput);
            f.SetAvailable(true); f.Playback.RequestResume();
        };
        var plan = SequenceCompiler.Compile(new([QuickActions.Keys([0x2A, 0x11], Ms(30), Ms(50), false)]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        // Shift was released when the pause began and pressed again when it ended (as for every held key); W went out once.
        CollectionAssert.AreEqual(new[] { "Shift down", "Shift up" }, log);
        CollectionAssert.AreEqual(new[] { "Shift down", "Shift up", "Shift down", "W down", "W up", "Shift up" }, f.Input.Log());
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void TheTargetGoneAtTheLastCheckBeforeAnInputPausesInsteadOfEndingTheRun()
    {
        var f = new Fixture();
        // The target leaves after the wait's own check and just before the check that comes right before E is sent.
        bool lost = false;
        f.Target.BeforeRead = () =>
        {
            if (!lost && f.Input.Accepted.Count == 2 && new StackTrace().ToString().Contains(".CanContinue(", StringComparison.Ordinal))
            { lost = true; f.SetAvailable(false); }
        };
        int pauses = 0;
        f.Executor.PlaybackPaused = paused => { if (paused) { pauses++; f.SetAvailable(true); f.Playback.RequestResume(); } };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", W, Ms(20)), SequenceStep.Input("E", E, Ms(20))]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.IsTrue(lost);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(1, pauses);
        CollectionAssert.AreEqual(new[] { "W down", "W up", "E down", "E up" }, f.Input.Log());
    }

    [TestMethod]
    public void APauseTheUserAskedForStaysTheirsWhenAnInputIsRefused()
    {
        var f = new Fixture();
        f.Input.Refuse = t =>
        {
            if (t.Control != E || t.Edge != InputEdge.Down || f.Refused++ != 0) return false;
            f.Playback.RequestPause(); // The user pressed Pause just before the target left.
            return f.Lose();
        };
        bool automatic = true;
        f.Executor.PlaybackPaused = paused =>
        {
            if (!paused) return;
            automatic = f.Playback.Automatic;
            f.SetAvailable(true); f.Playback.RequestResume(); // The user resumes.
        };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", W, Ms(20)), SequenceStep.Input("E", E, Ms(20))]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.IsFalse(automatic);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        CollectionAssert.AreEqual(new[] { "W down", "W up", "E down", "E up" }, f.Input.Log());
    }

    [TestMethod]
    public void ARefusedReleaseKeepsTheCleanupPathAndNeverPauses()
    {
        var f = new Fixture();
        f.Input.Refuse = t => t.Control == W && t.Edge == InputEdge.Up && f.Refused++ == 0 && f.Lose();
        int pauses = 0;
        f.Executor.PlaybackPaused = paused => { if (paused) pauses++; };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", W, Ms(20)), SequenceStep.Input("E", E, Ms(20))]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.InputRejected, result.Outcome);
        Assert.AreEqual(0, pauses);
        // The final cleanup still released W; E never went out.
        CollectionAssert.AreEqual(new[] { "W down", "W up" }, f.Input.Log());
        Assert.IsFalse(f.Executor.HasOwnedInput);
    }

    [TestMethod]
    public void WithoutPauseOnTargetLossARefusedDownStillEndsTheRun()
    {
        var f = new Fixture(); f.Executor.PauseOnTargetLoss = false;
        f.Input.Refuse = t => t.Control == E && t.Edge == InputEdge.Down && f.Lose();
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", W, Ms(20)), SequenceStep.Input("E", E, Ms(20))]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.InputRejected, result.Outcome);
        CollectionAssert.AreEqual(new[] { "W down", "W up" }, f.Input.Log());
    }

    [TestMethod]
    public void ARefusedDownWithTheTargetStillInFrontStillEndsTheRun()
    {
        // Refused for another reason (for example the user is holding that key): not a target loss, so no pause.
        var f = new Fixture();
        f.Input.Refuse = t => t.Control == E && t.Edge == InputEdge.Down;
        int pauses = 0;
        f.Executor.PlaybackPaused = paused => { if (paused) pauses++; };
        var plan = SequenceCompiler.Compile(new([SequenceStep.Input("W", W, Ms(20)), SequenceStep.Input("E", E, Ms(20))]));
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.InputRejected, result.Outcome);
        Assert.AreEqual(0, pauses);
    }

    [TestMethod]
    public void AHelperClickRefusedWhileTheTargetLeavesIsRepeatedOnceAfterThePause()
    {
        var f = new Fixture();
        f.Input.Refuse = t => t.Control == Left && t.Edge == InputEdge.Down && f.Input.Accepted.Count == 4 && f.Refused++ == 0 && f.Lose();
        f.Executor.PlaybackPaused = paused => { if (paused) { f.SetAvailable(true); f.Playback.RequestResume(); } };
        var plan = new HelperPlan(Left, HelperMode.Repeat, Ms(100), Ms(20), 4) { Position = Point };
        var result = f.Executor.Execute(plan, f.TargetId, default);
        Assert.AreEqual(ClickRunOutcome.Completed, result.Outcome, result.Error);
        Assert.AreEqual(4, f.Input.Accepted.Count(i => i.Edge == InputEdge.Down));
        Assert.AreEqual(4, f.Input.Accepted.Count(i => i.Edge == InputEdge.Up));
    }

    private static TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);

    private sealed class Wait(FakeClock clock) : IWaitStrategy
    {
        public void WaitUntil(TimeSpan due, CancellationToken stop)
        { stop.ThrowIfCancellationRequested(); if (due > clock.Elapsed) clock.Advance(due - clock.Elapsed); }
    }

    /// <summary>Like the runtime: refuses an input when told to, and records the ones that went out.</summary>
    private sealed class RefusingSink : IInputSink
    {
        private readonly List<InputTransition> accepted = [];
        public IReadOnlyList<InputTransition> Accepted => accepted;
        public Func<InputTransition, bool>? Refuse { get; set; }
        public bool TrySend(InputTransition transition)
        {
            if (Refuse?.Invoke(transition) == true) return false;
            accepted.Add(transition);
            return true;
        }
        public string[] Log() => accepted.Select(i => $"{Name(i.Control)} {(i.Edge == InputEdge.Down ? "down" : "up")}").ToArray();
        private static string Name(InputControl control) =>
            control == W ? "W" : control == E ? "E" : control == Shift ? "Shift" : control == Left ? "Left" : control.ToString();
    }

    /// <summary>Foreground provider whose reads can be watched, to change the target at an exact moment.</summary>
    private sealed class WatchedTarget : IForegroundTargetProvider
    {
        private ForegroundTarget? current;
        public Action? BeforeRead { get; set; }
        public ForegroundTarget? Current { get { BeforeRead?.Invoke(); return current; } set => current = value; }
    }

    private sealed class Fixture
    {
        public readonly FakeClock Clock = new();
        public readonly WatchedTarget Target = new();
        public readonly RefusingSink Input = new();
        public readonly ClickExecutor Executor;
        public readonly PlaybackControl Playback = new();
        public readonly Guid TargetId = Guid.NewGuid();
        public int Refused;
        public Fixture()
        {
            Target.Current = new(TargetId, true);
            Executor = new(Input, Clock, new Wait(Clock), Target) { Playback = Playback, PauseOnTargetLoss = true };
        }
        public void SetAvailable(bool available) => Target.Current = new(TargetId, available);
        /// <summary>The target leaves the front; true, so a refusal can say why in one expression.</summary>
        public bool Lose() { SetAvailable(false); return true; }
    }
}
