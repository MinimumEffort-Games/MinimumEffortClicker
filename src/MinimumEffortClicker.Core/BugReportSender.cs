using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MinimumEffortClicker.Core;

/// <summary>
/// Sends a bug report to the Minimum Effort server, which e-mails it to <see cref="BugReport.Address"/> (User,
/// 2026-10-19: reporting must take one press, no mail app or account). This is the app's only network call outside
/// Steam, and it runs only when the user presses Send report.
/// </summary>
public static class BugReportSender
{
    public const string Url = "https://minimumeffort.games/bugreport/";
    /// <summary>The server refuses bigger zips; the app checks first so the user hears why.</summary>
    public const long MaximumZipBytes = 8 * 1024 * 1024;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Whether it arrived, and what to tell the user when it did not.</summary>
    public readonly record struct Result(bool Sent, string? Problem);

    public static Task<Result> SendAsync(string subject, string body, string? replyTo, string app, string zipPath, CancellationToken cancel = default) =>
        SendAsync(Client, new Uri(Url), subject, body, replyTo, app, zipPath, cancel);

    /// <summary>The request itself; tests pass their own client and address.</summary>
    public static async Task<Result> SendAsync(HttpClient client, Uri url, string subject, string body, string? replyTo, string app, string zipPath, CancellationToken cancel = default)
    {
        byte[] zip;
        try
        {
            if (new FileInfo(zipPath).Length > MaximumZipBytes) return new(false, "The report is larger than 8 MB. Leave out the picture or the Action List and try again.");
            zip = await File.ReadAllBytesAsync(zipPath, cancel);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(false, "The report zip could not be read: " + ex.Message); }
        using var form = new MultipartFormDataContent
        {
            { new StringContent(subject), "subject" },
            { new StringContent(body), "body" },
            { new StringContent(replyTo ?? ""), "reply" },
            { new StringContent(app), "app" }
        };
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "report", Path.GetFileName(zipPath));
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Add("X-ME-Report", "1");
        request.Headers.UserAgent.ParseAdd("MinimumEffortClicker/1");
        try
        {
            using var response = await client.SendAsync(request, cancel);
            if (response.IsSuccessStatusCode) return new(true, null);
            return new(false, await ServerProblem(response, cancel));
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { return new(false, "The server did not answer in time. Check the internet connection and try again."); }
        catch (HttpRequestException ex) { return new(false, "Could not reach the server (" + ex.Message + "). Check the internet connection and try again."); }
    }

    /// <summary>The server's own reason when it gives one, otherwise the status.</summary>
    private static async Task<string> ServerProblem(HttpResponseMessage response, CancellationToken cancel)
    {
        try
        {
            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            if (answer.RootElement.TryGetProperty("error", out var error) && error.GetString() is { Length: > 0 } text) return text;
        }
        catch (JsonException) { }
        return response.StatusCode == HttpStatusCode.TooManyRequests ? "Too many reports from here right now. Try again in an hour."
            : $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.";
    }
}
