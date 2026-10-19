using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class HelpManualTests
{
    [TestMethod]
    public void EachVersionGetsItsOwnSections()
    {
        string[] Titles(bool linux, bool steam, bool deck) => HelpManual.For(linux, steam, deck).Select(s => s.Title).ToArray();
        var windows = Titles(false, false, false);
        CollectionAssert.DoesNotContain(windows, "Linux setup"); CollectionAssert.DoesNotContain(windows, "Steam Deck");
        CollectionAssert.DoesNotContain(windows, "Steam Cloud and Workshop");
        CollectionAssert.Contains(Titles(false, true, false), "Steam Cloud and Workshop");
        CollectionAssert.Contains(Titles(true, false, false), "Linux setup");
        CollectionAssert.Contains(Titles(true, false, true), "Steam Deck");
        foreach (var set in new[] { windows, Titles(true, false, true), Titles(false, true, false) })
            Assert.AreEqual(set.Length, set.Distinct().Count(), "every section title is a unique link target");
    }

    [TestMethod]
    public void PlatformLinesMatchTheVersion()
    {
        string Text(bool linux, bool deck) => string.Join("\n", HelpManual.For(linux, false, deck).SelectMany(s => s.Lines));
        StringAssert.Contains(Text(false, false), "Ctrl+Alt+F9");
        Assert.IsFalse(Text(true, false).Contains("Ctrl+Alt+F9"), "Linux has no backup stop key");
        StringAssert.Contains(Text(true, true), "L4 Start / Stop");
        StringAssert.Contains(Text(false, false), "A Start / Stop");
        StringAssert.Contains(Text(false, false), "Minimize to taskbar");
        StringAssert.Contains(Text(true, false), "Minimize to tray");
    }
}
