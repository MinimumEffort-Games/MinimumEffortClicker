namespace MinimumEffortClicker.Core;

/// <summary>
/// The text side of a bug report (User, 2026-10-18: reports go to GitHub issues). The app never sends anything itself:
/// it saves a zip and opens a prefilled issue in the browser, and the user attaches the zip there.
/// </summary>
public static class BugReport
{
    public const string IssuesUrl = "https://github.com/MinimumEffort-Games/MinimumEffortClicker/issues/new";
    /// <summary>Browsers and GitHub refuse very long links; the full text is in the zip anyway.</summary>
    public const int MaximumBodyLength = 6000;
    /// <summary>The most log lines a report carries: the end of the log, where the problem is.</summary>
    public const int LogLines = 300;

    /// <summary>
    /// Replaces personal words (the user name, the home folder, the computer name) with a neutral one, so a report can
    /// be public. Longer words go first, so a home folder that contains the user name is replaced whole; a word is only
    /// replaced where it stands alone, so a user called "Admin" leaves "administrator" alone.
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

    /// <summary>A link that opens a new issue with the bug label, the title and the body filled in.</summary>
    public static string IssueLink(string title, string body)
    {
        if (body.Length > MaximumBodyLength) body = body[..MaximumBodyLength] + Environment.NewLine + "… (the rest is in the attached zip)";
        return $"{IssuesUrl}?labels=bug&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
    }
}
