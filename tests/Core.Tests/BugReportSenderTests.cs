using System.Net;
using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class BugReportSenderTests
{
    private sealed class FakeServer(HttpStatusCode status, string answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Form { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Request = request;
            Form = await request.Content!.ReadAsStringAsync(cancel);
            return new HttpResponseMessage(status) { Content = new StringContent(answer) };
        }
    }

    private static string Zip(int bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mec-test-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [TestMethod]
    public async Task AReportIsPostedWithItsTextAndZip()
    {
        var server = new FakeServer(HttpStatusCode.OK, """{"ok":true,"id":"x"}""");
        string zip = Zip(10);
        try
        {
            var result = await BugReportSender.SendAsync(new HttpClient(server), new Uri("https://example.test/bugreport/"), "Subject line", "What happened", "me@example.test", "Clicker 1", zip);
            Assert.IsTrue(result.Sent);
            Assert.AreEqual(HttpMethod.Post, server.Request!.Method);
            Assert.AreEqual("1", server.Request.Headers.GetValues("X-ME-Report").Single());
            foreach (string part in new[] { "Subject line", "What happened", "me@example.test", "Clicker 1", "name=report", Path.GetFileName(zip) })
                StringAssert.Contains(server.Form, part);
        }
        finally { File.Delete(zip); }
    }

    [TestMethod]
    public async Task TheServersReasonIsShownWhenItRefuses()
    {
        var server = new FakeServer(HttpStatusCode.TooManyRequests, """{"ok":false,"error":"Too many reports from here right now."}""");
        string zip = Zip(10);
        try
        {
            var result = await BugReportSender.SendAsync(new HttpClient(server), new Uri("https://example.test/"), "s", "b", null, "a", zip);
            Assert.IsFalse(result.Sent);
            Assert.AreEqual("Too many reports from here right now.", result.Problem);
        }
        finally { File.Delete(zip); }
    }

    [TestMethod]
    public async Task ATooLargeZipIsNotSent()
    {
        var server = new FakeServer(HttpStatusCode.OK, "{}");
        string zip = Zip((int)BugReportSender.MaximumZipBytes + 1);
        try
        {
            var result = await BugReportSender.SendAsync(new HttpClient(server), new Uri("https://example.test/"), "s", "b", null, "a", zip);
            Assert.IsFalse(result.Sent);
            Assert.IsNull(server.Request, "nothing goes out");
        }
        finally { File.Delete(zip); }
    }
}
