using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class ScrollActionsTests
{
    private static readonly ClientPoint Spot = new(300, 200, 800, 600, 96);
    private static TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);
    private static RecordedEdge Edge(double ms, InputControl control, bool down = true) => new(Ms(ms), control, down, Spot);

    [TestMethod]
    public void ScrollActionHoldsOneNotchPerInputAndReadsBack()
    {
        var step = QuickActions.Scroll(ScrollDirection.Down, 5, Ms(40), Ms(100), Spot);
        Assert.AreEqual("Scroll down ×5", step.Name);
        Assert.HasCount(5, step.Children!);
        Assert.IsTrue(step.Children!.All(c => c.Control == new InputControl(InputKind.Scroll, -QuickActions.Notch) && c.Position == Spot && c.Duration == TimeSpan.Zero));
        CollectionAssert.AreEqual(new[] { 0d, 40, 80, 120, 160 }, step.Children!.Select(c => c.Offset.TotalMilliseconds).ToArray());
        Assert.AreEqual(Ms(260), step.Duration);
        Assert.AreEqual(new QuickScroll(ScrollDirection.Down, 5, Ms(40), Spot, Ms(100)), QuickActions.ReadScroll(step));
        Assert.AreEqual(5, SequenceCompiler.Compile(new([step])).Inputs.Count(i => i.Control.Kind == InputKind.Scroll));

        var right = QuickActions.Scroll(ScrollDirection.Right, 1, Ms(30), TimeSpan.Zero);
        Assert.AreEqual("Scroll right", right.Name);
        Assert.AreEqual(new InputControl(InputKind.HorizontalScroll, QuickActions.Notch), right.Children![0].Control);
        Assert.IsNull(QuickActions.ReadScroll(right)!.Position);
        Assert.ThrowsExactly<ArgumentException>(() => QuickActions.Scroll(ScrollDirection.Up, 0, Ms(30), TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentException>(() => QuickActions.Scroll(ScrollDirection.Up, 101, Ms(30), TimeSpan.Zero));
        Assert.IsNull(QuickActions.ReadScroll(QuickActions.Click(PointerButton.Left, QuickClickKind.Single, Ms(20), Ms(0), Ms(20), Spot)));
    }

    [TestMethod]
    public void RecordedNotchesBecomeOneScrollPerBurst()
    {
        var down = new InputControl(InputKind.Scroll, -QuickActions.Notch); var up = new InputControl(InputKind.Scroll, QuickActions.Notch);
        var left = InputControl.Mouse(PointerButton.Left);
        // Three quick notches down, a lone notch down after a long gap, a notch up (new direction), then a click.
        var recording = RecordingNormalizer.Normalize([Edge(0, down), Edge(50, down), Edge(90, down), Edge(1200, down), Edge(1300, up),
            Edge(2000, left), Edge(2060, left, false)], Ms(2500)).Sequence;

        var actions = QuickActions.RecordedActions(recording, includePauses: true);
        CollectionAssert.AreEqual(new[] { "Scroll down ×3", "Pause", "Scroll down", "Pause", "Scroll up", "Pause", "Left click" }, actions.Steps.Select(s => s.Name).ToArray());
        CollectionAssert.AreEqual(new[] { 0d, 50, 90 }, actions.Steps[0].Children!.Select(c => c.Offset.TotalMilliseconds).ToArray());
        Assert.AreEqual(Ms(1110), actions.Steps[1].Duration);
        Assert.AreEqual(new QuickScroll(ScrollDirection.Down, 3, Ms(45), Spot, TimeSpan.Zero), QuickActions.ReadScroll(actions.Steps[0]));

        var withoutPauses = QuickActions.RecordedActions(recording, includePauses: false);
        CollectionAssert.AreEqual(new[] { "Scroll down ×3", "Scroll down", "Scroll up", "Left click" }, withoutPauses.Steps.Select(s => s.Name).ToArray());
    }

    [TestMethod]
    public void TouchpadStepsAddUpToWholeNotches()
    {
        // A touchpad reports a third of a notch per step; six of them make two notches and replay exactly as recorded.
        var third = new InputControl(InputKind.Scroll, -40);
        var recording = RecordingNormalizer.Normalize(Enumerable.Range(0, 6).Select(i => Edge(i * 20, third)).ToArray(), Ms(200)).Sequence;
        var scroll = QuickActions.RecordedActions(recording, includePauses: true).Steps.Single();
        Assert.AreEqual("Scroll down ×2", scroll.Name);
        Assert.HasCount(6, scroll.Children!);
        Assert.IsTrue(scroll.Children!.All(c => c.Control == third));
    }
}
