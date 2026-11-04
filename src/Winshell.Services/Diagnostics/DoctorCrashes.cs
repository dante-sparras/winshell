namespace Winshell.Diagnostics;

/// <summary>The doctor's check of the crash reports Winshell wrote in the last 7 days.</summary>
internal static class DoctorCrashes
{
    /// <summary>Crash reports in a folder, or why the folder could not be listed.</summary>
    internal readonly record struct CrashFolderRead(IReadOnlyList<(string Path, DateTime WrittenUtc)> Reports, string? Unreadable);

    /// <summary>The start of one crash file. No text and no error means the file is not a Winshell crash report.</summary>
    internal readonly record struct CrashPeek(string? Text, string? Unreadable);

    internal static DoctorCheck CrashesIn(CrashFolderRead folder, DateTime nowUtc, Func<string, CrashPeek> peek)
    {
        if (folder.Unreadable is not null)
            return new("crashes", "Crashes", CheckStatus.Warn, $"Could not read the crash folder: {folder.Unreadable}", "Make sure your account can read that folder, then run winshell doctor again.");
        return Crashes(folder.Reports, nowUtc, peek);
    }

    internal static DoctorCheck Crashes(IReadOnlyList<(string Path, DateTime WrittenUtc)> reports, DateTime nowUtc, Func<string, CrashPeek> peek)
    {
        var recent = reports.Where(r => nowUtc - r.WrittenUtc <= TimeSpan.FromDays(7)).OrderByDescending(r => r.WrittenUtc).ToList();
        var opened = new List<(string Path, string Text)>();
        string? unreadable = null;
        foreach (var report in recent)
        {
            var head = peek(report.Path);
            if (head.Unreadable is not null)
            {
                unreadable ??= head.Unreadable;
                continue;
            }
            if (head.Text is null)
                continue;
            opened.Add((report.Path, head.Text));
        }
        if (unreadable is not null && opened.Count == 0)
            return new("crashes", "Crashes", CheckStatus.Warn, $"Could not read a crash report: {unreadable}", $"Read the crash folder, then attach a report to a bug report at {Doctor.BugReports}.");
        if (opened.Count == 0)
            return new("crashes", "Crashes", CheckStatus.Pass, "No crash reports in the last 7 days");

        var countText = opened.Count == 1 ? "1 crash report" : $"{opened.Count} crash reports";
        var message = $"{countText} in the last 7 days. The newest is {opened[0].Path}.";
        var fatal = opened.FirstOrDefault(r => r.Text.Contains("Winshell closed: yes", StringComparison.Ordinal));
        if (fatal.Path is not null)
        {
            message += " Winshell closed: yes.";
            var line = ExceptionLine(fatal.Text);
            if (line is not null)
                message += " " + line;
            if (!string.Equals(fatal.Path, opened[0].Path, StringComparison.OrdinalIgnoreCase))
                message += $" That report is {fatal.Path}.";
        }
        else if (ExceptionLine(opened[0].Text) is { } line)
        {
            message += " " + line;
        }
        if (unreadable is not null)
            message += $" Could not read a crash report: {unreadable}.";
        return new("crashes", "Crashes", CheckStatus.Warn, message, $"Read it, then attach it to a bug report at {Doctor.BugReports}.");
    }

    private static string? ExceptionLine(string head)
    {
        var lines = head.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!string.Equals(lines[i].TrimEnd('\r'), "Exception", StringComparison.Ordinal) || i + 1 >= lines.Length)
                continue;
            var line = lines[i + 1].TrimEnd('\r');
            if (line.Length > 0)
                return line;
        }
        return null;
    }
}
