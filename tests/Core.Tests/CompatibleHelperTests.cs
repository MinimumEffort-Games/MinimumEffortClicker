using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class CompatibleHelperTests
{
    private static NamedHelper Key(int code) => new(Guid.NewGuid(), "Key", new(InputControl.Key(code), HelperMode.Repeat,
        TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 3));
    [TestMethod]
    public void SnapshotsBoundedIndependentKeys()
    {
        var items = new[] { Key(0x11), Key(0x12) }; var plan = CompatibleHelpers.Compile(items);
        items[0] = Key(0x39);
        Assert.AreEqual(0x11, plan.Helpers[0].Plan.Control.Code);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<NamedHelper>)plan.Helpers).Clear());
        Assert.ThrowsExactly<ArgumentException>(() => CompatibleHelpers.Compile([]));
        Assert.ThrowsExactly<ArgumentException>(() => CompatibleHelpers.Compile(Enumerable.Repeat(Key(0x11), 5)));
    }
    [TestMethod]
    public void RejectsDuplicateKeysModifiersAndCompetingPointerWriters()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CompatibleHelpers.Compile([Key(0x11), Key(0x11)]));
        Assert.ThrowsExactly<ArgumentException>(() => CompatibleHelpers.Compile([Key(0x2A)]));
        var left = Key(0x11) with { Plan = Key(0x11).Plan with { Control = InputControl.Mouse(PointerButton.Left) } };
        var right = left with { Id = Guid.NewGuid(), Plan = left.Plan with { Control = InputControl.Mouse(PointerButton.Right) } };
        Assert.ThrowsExactly<ArgumentException>(() => CompatibleHelpers.Compile([left, right]));
        Assert.HasCount(2, CompatibleHelpers.Compile([left, Key(0x11)]).Helpers);
    }
}
