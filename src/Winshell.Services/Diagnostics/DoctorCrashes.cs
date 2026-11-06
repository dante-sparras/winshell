namespace Winshell.Diagnostics;

/// <summary>The doctor's check of the crash reports Winshell wrote in the last 7 days.</summary>
internal static class DoctorCrashes
{
    internal static DoctorCheck CrashesIn(CrashList folder, DateTime nowUtc)
    {
        if (folder.FolderError is not null)
            return new DoctorCheck("crashes", "Crashes", CheckStatus.Warn,
                $"Could not read the crash folder: {folder.FolderError}",
                "Make sure your account can read that folder, then run winshell doctor again.");
        return Crashes(folder.Reports, nowUtc, folder.FileError);
    }

    internal static DoctorCheck Crashes(IReadOnlyList<CrashRecord> reports, DateTime nowUtc, CrashFileError? fileError)
    {
        // List already returns newest first: the time in the file name, then the process, then the name.
        var recent = reports.Where(r => nowUtc - r.Written.UtcDateTime <= TimeSpan.FromDays(7)).ToList();
        // The same window as a report that did read. An older file that cannot be opened is not this check.
        string? recentError = null;
        if (fileError is { } failed && nowUtc - failed.Written.UtcDateTime <= TimeSpan.FromDays(7))
            recentError = $"{failed.Path}: {failed.Message}";
        if (recentError is not null && recent.Count == 0)
            return new DoctorCheck("crashes", "Crashes", CheckStatus.Warn,
                $"Could not read a crash report: {recentError}",
                $"Read the crash folder, then attach a report to a bug report at {Doctor.BugReports}.");
        if (recent.Count == 0)
            return new DoctorCheck("crashes", "Crashes", CheckStatus.Pass, "No crash reports in the last 7 days");

        var countText = recent.Count == 1 ? "1 crash report" : $"{recent.Count} crash reports";
        var message = $"{countText} in the last 7 days. The newest is {recent[0].Path}.";
        var fatal = recent.FirstOrDefault(r => r.Closed);
        if (fatal.Path is not null)
        {
            message += " Winshell closed: yes.";
            if (fatal.ExceptionLine.Length > 0)
                message += " " + fatal.ExceptionLine;
            if (!string.Equals(fatal.Path, recent[0].Path, StringComparison.OrdinalIgnoreCase))
                message += $" That report is {fatal.Path}.";
        }
        else if (recent[0].ExceptionLine.Length > 0)
        {
            message += " " + recent[0].ExceptionLine;
        }

        if (recentError is not null)
            message += $" Could not read a crash report: {recentError}.";
        return new DoctorCheck("crashes", "Crashes", CheckStatus.Warn, message,
            $"Read it, then attach it to a bug report at {Doctor.BugReports}.");
    }
}
