using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class BugReportTests
{
    [TestMethod]
    public void PersonalWordsAreReplacedOnlyWhereTheyStandAlone()
    {
        (string, string)[] words = [(@"C:\Users\Admin", "%USERPROFILE%"), ("Admin", "User"), ("DESKTOP-42", "PC")];
        string text = @"Saved C:\Users\Admin\Macros\a.mecprofile on desktop-42 as admin; run as administrator.";
        Assert.AreEqual(@"Saved %USERPROFILE%\Macros\a.mecprofile on PC as User; run as administrator.", BugReport.Redact(text, words));
        Assert.AreEqual("a b", BugReport.Redact("a b", [("", "x"), ("a", "y")]), "words shorter than two letters are left alone");
    }

    [TestMethod]
    public void TheLogEndAndItsLastErrorAreFound()
    {
        string log = string.Join("\n",
            "2026-10-18 10:00:00.000  Start: Minimum Effort Clicker 0.5",
            "2026-10-18 10:01:00.000  Unexpected error in the app:",
            "System.InvalidOperationException: first",
            "   at A.B()",
            "2026-10-18 10:02:00.000  Run ended (Actions): Stopped.",
            "2026-10-18 10:03:00.000  Unexpected error in a background thread:",
            "System.ArgumentException: second",
            "   at C.D()",
            "2026-10-18 10:04:00.000  Controllers: none connected");
        StringAssert.StartsWith(BugReport.LastError(log)!, "2026-10-18 10:03:00.000  Unexpected error in a background thread:");
        StringAssert.EndsWith(BugReport.LastError(log)!, "   at C.D()");
        Assert.IsNull(BugReport.LastError("2026-10-18 10:00:00.000  Start: fine"));
        Assert.IsNull(BugReport.LastError(log, new DateTime(2026, 10, 18, 10, 5, 0)), "an error before the limit does not count");
        Assert.IsNotNull(BugReport.LastError(log, new DateTime(2026, 10, 18, 10, 2, 0)));
        Assert.AreEqual("2026-10-18 10:04:00.000  Controllers: none connected", BugReport.Tail(log, 1));
    }

    [TestMethod]
    public void TheIssueLinkIsEscapedAndBounded()
    {
        string link = BugReport.IssueLink("Look & feel #2", "line one\nline two");
        StringAssert.StartsWith(link, BugReport.IssuesUrl + "?labels=bug&title=Look%20%26%20feel%20%232&body=line%20one%0Aline%20two");
        string longLink = BugReport.IssueLink("t", new string('x', BugReport.MaximumBodyLength + 500));
        StringAssert.Contains(Uri.UnescapeDataString(longLink), "the rest is in the attached zip");
        Assert.IsTrue(longLink.Length < BugReport.MaximumBodyLength + 300);
    }
}
