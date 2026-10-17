using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class KeyboardLayoutIdTests
{
    [TestMethod]
    public void WindowsHandlesAndLinuxNamesAreBothValid()
    {
        Assert.IsTrue(KeyboardLayoutId.Valid("04090409"));
        Assert.IsTrue(KeyboardLayoutId.Valid("linux:xkb:us+intl"));
        foreach (var invalid in new[] { "", "0409", "linux:", "linux:xkb:us intl", "xkb:us", "linux:" + new string('a', KeyboardLayoutId.MaxLinuxLength) })
            Assert.IsFalse(KeyboardLayoutId.Valid(invalid), invalid);
    }

    [TestMethod]
    public void ARecordingOnALinuxOnlyLayoutCompiles()
    {
        // A Steam Deck recording stopped with "Invalid keyboard-layout context" because only Windows handles passed.
        var steps = new[] { SequenceStep.Input("Key A", InputControl.Key(0x1E), TimeSpan.FromMilliseconds(50)) };
        var plan = SequenceCompiler.Compile(new SequenceDefinition(steps, "linux:xkb:us+intl"));
        Assert.AreEqual("linux:xkb:us+intl", plan.KeyboardLayout);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new SequenceDefinition(steps, "linux:bad layout")));
    }
}
