using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class AppVersionTests
{
    [TestMethod]
    public void TheVersionShowsTheShortCommit()
    {
        Assert.AreEqual("1.0.0 (617701c)", AppVersion.Display("1.0.0+617701cbcc901f565c3e6be97e20b8833b1c6418"));
        Assert.AreEqual("1.0.0", AppVersion.Display("1.0.0"));
        Assert.AreEqual("1.0.0", AppVersion.Display("1.0.0+"));
        Assert.AreEqual("unknown", AppVersion.Display(null));
    }
}
