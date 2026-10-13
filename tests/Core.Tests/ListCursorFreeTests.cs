using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class ListCursorFreeTests
{
    private static readonly ClientPoint Point = new(10, 20, 800, 600, 96);
    private static SequenceStep Click() => QuickActions.Click(PointerButton.Left, QuickClickKind.Single,
        TimeSpan.FromMilliseconds(20), TimeSpan.Zero, TimeSpan.FromMilliseconds(20), Point);

    [TestMethod]
    public void AppliesToExistingClicksWithoutChangingOriginal()
    {
        var source = new SequenceDefinition([Click(), SequenceStep.Wait(TimeSpan.FromMilliseconds(100)), Click(), SequenceStep.Screenshot()]);
        var updated = QuickActions.WithCursorFree(source, true);
        var plan = SequenceCompiler.Compile(updated);
        Assert.IsTrue(plan.Inputs.All(i => i.LeaveCursorFree));
        Assert.IsFalse(source.Steps.Any(s => s.LeaveCursorFree));
        Assert.IsFalse(SequenceCompiler.Compile(QuickActions.WithCursorFree(updated, false)).LeaveCursorFree);
    }

    [TestMethod]
    public void UnsupportedKeyboardAndMovementAreRejectedRatherThanSilentlySkipped()
    {
        var keys = QuickActions.Keys([0x11], TimeSpan.FromMilliseconds(20), TimeSpan.Zero, false);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(QuickActions.WithCursorFree(new([Click(), keys]), true)));
        var motion = Click() with { Motion = new(TimeSpan.FromMilliseconds(200)) };
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(QuickActions.WithCursorFree(new([motion]), true)));
    }
}
