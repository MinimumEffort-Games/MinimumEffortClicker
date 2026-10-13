using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class CoordinateTests
{
    [TestMethod]
    public void WindowMoveRetainsClientPointButResizeAndDpiChangeReject()
    {
        var point = new ClientPoint(100, 50, 800, 600, 144);
        Assert.AreEqual((-1820, -150), point.Resolve(-1920, -200, 800, 600, 144));
        Assert.ThrowsExactly<ArgumentException>(() => point.Resolve(0, 0, 801, 600, 144));
        Assert.ThrowsExactly<ArgumentException>(() => point.Resolve(0, 0, 800, 600, 96));
        Assert.IsFalse((point with { X = 800 }).IsValid);
    }

    [TestMethod]
    public void AbsoluteCoordinatesRoundTripEveryPixelAcrossNegativeOriginDesktop()
    {
        for (int x = -1920; x < 2560; x++)
        {
            int normalized = ClientPoint.NormalizeAbsolute(x, -1920, 4480);
            Assert.AreEqual(x, (int)((long)normalized * 4480 / 65536) - 1920);
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ClientPoint.NormalizeAbsolute(2560, -1920, 4480));
    }

    [TestMethod]
    public void DragNormalizationPreservesAnchorsAndIntermediatePath()
    {
        var start = new ClientPoint(20, 30, 800, 600, 96); var end = start with { X = 220 };
        var button = InputControl.Mouse(PointerButton.Left);
        var draft = RecordingNormalizer.Normalize([
            new(TimeSpan.Zero, button, true, start),
            new(TimeSpan.FromMilliseconds(50), new(InputKind.PointerMove, 0), true, start with { X = 100 }),
            new(TimeSpan.FromMilliseconds(100), button, false, end)], TimeSpan.FromMilliseconds(100));
        var plan = SequenceCompiler.Compile(draft.Sequence);
        Assert.HasCount(3, plan.Inputs); Assert.AreEqual(start, plan.Inputs[0].Position);
        Assert.AreEqual(InputKind.PointerMove, plan.Inputs[1].Control.Kind); Assert.AreEqual(end, plan.Inputs[2].Position);
        Assert.IsFalse(plan.Inputs[2].Down);
    }

    [TestMethod]
    public void PositionsCannotAttachToKeysOrInventUncalibratedMovement()
    {
        var point = new ClientPoint(10, 10, 800, 600, 96);
        var step = SequenceStep.Input("W", InputControl.Key(0x11), TimeSpan.FromMilliseconds(10)) with { Position = point };
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([step])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Input("Move", new(InputKind.PointerMove, 0), TimeSpan.Zero)])));
        var scroll = SequenceStep.Input("Precise wheel", new(InputKind.Scroll, 30), TimeSpan.Zero) with { Position = point };
        Assert.HasCount(1, SequenceCompiler.Compile(new([scroll])).Inputs);
    }
}
