namespace MinimumEffortClicker.Core;

/// <summary>
/// The text and zip of a bug report. Every report ends up in one inbox, <see cref="Address"/> (User, 2026-10-19): Send
/// report posts it through <see cref="BugReportSender"/>, and when that fails the report can go as an e-mail instead.
/// </summary>
public static class BugReport
{
    public const string Address = "bugs@minimumeffort.eu";
    /// <summary>Mail apps and the system refuse very long mailto links; the full text is in the zip anyway.</summary>
    public const int MaximumBodyLength = 1500;
    /// <summary>The most log lines a report carries: the end of the log, where the problem is.</summary>
    public const int LogLines = 300;

    /// <summary>
    /// Replaces personal words (the user name, the home folder, the computer name) with a neutral one. Longer words go
    /// first, so a home folder that contains the user name is replaced whole; a word is only replaced where it stands
    /// alone, so a user called "Admin" leaves "administrator" alone.
    /// </summary>
    public static string Redact(string text, IEnumerable<(string Secret, string Replacement)> words)
    {
        foreach (var (secret, replacement) in words.Where(w => w.Secret.Length >= 2).OrderByDescending(w => w.Secret.Length))
            text = System.Text.RegularExpressions.Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(secret)}(?![\p{{L}}\p{{N}}])",
                replacement.Replace("$", "$$"), System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return text;
    }

    /// <summary>The last <paramref name="count"/> lines of a log.</summary>
    public static string Tail(string log, int count = LogLines)
    {
        var lines = log.Replace("\r\n", "\n").Split('\n');
        return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - count)));
    }

    /// <summary>The last unexpected error in a log, with its trace, or null when there is none. With <paramref name="notBefore"/>,
    /// an older error does not count: one from days ago would point the report at the wrong problem.</summary>
    public static string? LastError(string log, DateTime? notBefore = null)
    {
        var lines = log.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindLastIndex(lines, l => l.Contains("Unexpected error in ", StringComparison.Ordinal));
        if (start < 0) return null;
        if (notBefore is { } limit && (lines[start].Length < 23 || !DateTime.TryParseExact(lines[start][..23], "yyyy-MM-dd HH:mm:ss.fff",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at) || at < limit)) return null;
        // The trace runs until the next timestamped log line.
        int end = start + 1;
        while (end < lines.Length && !(lines[end].Length > 4 && char.IsDigit(lines[end][0]) && lines[end][4] == '-')) end++;
        return string.Join(Environment.NewLine, lines[start..end]).TrimEnd();
    }

    /// <summary>
    /// The report as plain text for the e-mail and report.txt: the user's text, the details and a recent error. The
    /// caller redacts it.
    /// </summary>
    public static string Text(string happened, string expected, string? details, string? error)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("What happened:").AppendLine(happened.Trim()).AppendLine();
        if (expected.Trim() is { Length: > 0 } wanted) text.AppendLine("What I expected:").AppendLine(wanted).AppendLine();
        if (details is not null) text.AppendLine("App and system:").AppendLine(details).AppendLine();
        if (error is not null) text.AppendLine("Last error:").AppendLine(error).AppendLine();
        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Writes the report zip: report.txt, and the chosen parts (the end of the log, a picture of the clicker, the Action
    /// List as a macro file). An older zip at the same path is replaced.
    /// </summary>
    public static void WriteZip(string path, string report, string? log, byte[]? picture, Action<string>? saveActionList)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        void Text(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(content); }
        Text("report.txt", report);
        if (log is not null) Text("log.txt", log);
        if (picture is not null) { using var stream = zip.CreateEntry("clicker.png").Open(); stream.Write(picture); }
        if (saveActionList is not null)
        {
            string temporary = Path.Combine(Path.GetTempPath(), $"mec-report-{Guid.NewGuid():N}.mecprofile");
            try { saveActionList(temporary); System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, temporary, "action-list.mecprofile"); }
            finally { File.Delete(temporary); }
        }
    }

    /// <summary>The subject line: the app's name and the first line of what happened, shortened.</summary>
    public static string Subject(string happened)
    {
        string firstLine = happened.Trim().Replace("\r\n", "\n").Split('\n')[0].Trim();
        return "Minimum Effort Clicker bug: " + (firstLine.Length > 80 ? firstLine[..80] + "…" : firstLine);
    }

    /// <summary>A mailto link that opens a new e-mail to <see cref="Address"/> with the subject and body filled in.</summary>
    public static string MailLink(string subject, string body)
    {
        if (body.Length > MaximumBodyLength) body = body[..MaximumBodyLength] + Environment.NewLine + "… (the rest is in the attached zip)";
        // Mail apps expect CR LF line breaks in a mailto body.
        body = body.Replace("\r\n", "\n").Replace("\n", "\r\n");
        return $"mailto:{Address}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
    }
}
