using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Winshell;

/// <summary>
/// A crash report on disk, and what it is about. A window's report also names the window for a person,
/// <see cref="Subject"/>, and says what Winshell did about it, <see cref="Recovery"/>.
/// </summary>
internal sealed record CrashReport(string Path, string Where, Exception Error, bool Fatal)
{
    /// <summary>What failed, as a sentence names it: "the taskbar", "a window". Empty when it is not a window.</summary>
    public string Subject { get; init; } = "";

    /// <summary>What Winshell did about it, such as "Winshell opens it again in 1 second." Empty when nothing.</summary>
    public string Recovery { get; init; } = "";
}

/// <summary>
/// A report read back from its file. <see cref="Written"/> is the UTC instant in the file name.
/// <see cref="ExceptionLine"/> is the first line of the exception, or empty when the report has none.
/// </summary>
internal readonly record struct CrashRecord(string Path, DateTimeOffset Written, bool Closed, string ExceptionLine);

/// <summary>
/// A file named as a report that could not be read. <see cref="Written"/> is the UTC instant in its name.
/// </summary>
internal readonly record struct CrashFileError(string Path, DateTimeOffset Written, string Message);

/// <summary>
/// The reports in a folder, newest first. <see cref="FolderError"/> is set when the folder cannot be listed.
/// <see cref="FileError"/> is the newest file named as a report that could not be read. A missing folder has neither.
/// </summary>
internal readonly record struct CrashList(
    IReadOnlyList<CrashRecord> Reports,
    string? FolderError,
    CrashFileError? FileError);

/// <summary>
/// Writes and reads crash reports: plain text files in one folder on this PC, for a bug report. Nothing is sent
/// anywhere. A report says where the runtime failed and the exception with its stack, with the secrets
/// <see cref="CrashRedaction"/> recognizes taken out, and copies the end of the instance's log, which is written with
/// them taken out already. Several instances can share the folder: each report gets a file of its own, and only files
/// that are Winshell reports are deleted. A report shows up in the folder only once it is whole and closed. The UTC
/// instant in the file name is when the report says it was written.
/// </summary>
/// <param name="folder">The folder reports go to: the instance's <c>ShellLocations.CrashRoot</c>.</param>
/// <param name="log">
/// Where a report that could not be written, or a handler that threw, is recorded. Null when the caller only needs
/// the null return.
/// </param>
/// <param name="shellId">The shell id each report names. Empty when there is none.</param>
/// <param name="configPath">The configuration each report names. Empty when there is none.</param>
/// <param name="logPath">The instance's log, whose last lines go into each report. Empty when there is none.</param>
internal sealed partial class CrashReports(
    string folder,
    Log? log = null,
    string shellId = "",
    string configPath = "",
    string logPath = "")
{
    /// <summary>How many reports the folder keeps. Writing one deletes the oldest beyond this.</summary>
    private const int Keep = 50;

    private const string Heading = "Winshell crash report";
    private const string ClosedLine = "Winshell closed: yes";
    private const string OpenLine = "Winshell closed: no";
    private const string ExceptionHeading = "Exception";
    private const int LogLines = 40;
    private const int LogBytes = 16 * 1024;

    /// <summary>The subfolder a report is written in before it is moved into the folder whole.</summary>
    private const string WritingFolder = "writing";

    private static readonly object TimeGate = new();
    private static long _lastTicks;
    private readonly object _lock = new();
    private readonly HashSet<string> _reported = [];

    /// <summary>The folder reports go to.</summary>
    public string Folder { get; } = Path.GetFullPath(folder);

    /// <summary>The time a report is written at. Tests set it.</summary>
    internal TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Raised after a report is written, on the thread that wrote it.</summary>
    public event Action<CrashReport>? Written;

    /// <summary>
    /// Writes a report. Returns null when the folder cannot be written. Nothing it does throws: it runs when
    /// something else already failed, sometimes as the process ends.
    /// </summary>
    public CrashReport? Write(string where, Exception error, bool fatal = false, string subject = "",
        string recovery = "")
    {
        CrashReport? report;
        lock (_lock)
        {
            report = WriteLocked(where, error, fatal, subject, recovery);
        }

        Raise(report);
        return report;
    }

    /// <summary>
    /// Writes a report unless one was already written for the same exceptions thrown along the same stacks from the
    /// same place, so work that fails each time it runs, such as a timer, writes one report rather than filling
    /// the disk. A report that could not be written is tried again the next time.
    /// </summary>
    public CrashReport? WriteOnce(string where, Exception error, string subject = "", string recovery = "")
    {
        CrashReport? report;
        lock (_lock)
        {
            var fingerprint = Fingerprint(where, error);
            if (_reported.Contains(fingerprint))
                return null;
            report = WriteLocked(where, error, false, subject, recovery);
            if (report is not null)
                _reported.Add(fingerprint);
        }

        Raise(report);
        return report;
    }

    private void Raise(CrashReport? report)
    {
        if (report is null)
            return;
        try
        {
            Written?.Invoke(report);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log?.Error("shell", $"Could not write a crash report: {e.Message}");
        }
    }

    /// <summary>
    /// What makes two failures the same: where, and each exception's type and full stack, inner and aggregated ones
    /// included. Messages are left out, since they often carry values that change from one run to the next.
    /// </summary>
    internal static string Fingerprint(string where, Exception error)
    {
        var text = new StringBuilder(where);

        void Add(Exception e, int depth)
        {
            text.Append('\n').Append(depth).Append(' ').Append(e.GetType().FullName).Append('\n').Append(e.StackTrace);
            if (depth >= 16)
                return;
            if (e is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions)
                    Add(inner, depth + 1);
            else if (e.InnerException is { } inner) Add(inner, depth + 1);
        }

        Add(error, 0);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private CrashReport? WriteLocked(string where, Exception error, bool fatal, string subject, string recovery)
    {
        try
        {
            var now = Time.GetLocalNow();
            var text = Format(now, where, error, fatal, recovery);
            // A report is written whole in a subfolder, then moved in, so whoever reads the folder never finds one
            // half written or still open.
            var writing = Path.Combine(Folder, WritingFolder);
            Directory.CreateDirectory(writing);
            var temp = Path.Combine(writing, $"{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
                    {
                        writer.Write(text);
                    }

                    stream.Flush(true);
                }

                // The name is unique to this process and instant; another instance writing into the folder at the same
                // time cannot take it over, since a report is moved only to a name that is not there yet.
                var path = Path.Combine(Folder, FileName(NextTicks(now.UtcTicks), where));
                File.Move(temp, path, false);
                Prune(path);
                return new CrashReport(path, where, error, fatal) { Subject = subject, Recovery = recovery };
            }
            finally
            {
                // Already gone once moved; this deletes what a failed write or move left.
                try
                {
                    File.Delete(temp);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log?.Error("shell", $"Could not write a crash report: {e.Message}");
            return null;
        }
    }

    /// <summary>The instant of a new report: never the same as, or before, the last one this process wrote.</summary>
    private static long NextTicks(long utcTicks)
    {
        lock (TimeGate)
        {
            _lastTicks = Math.Max(utcTicks, _lastTicks + 1);
            return _lastTicks;
        }
    }

    /// <summary><c>crash-20261029-140312-1234567Z-4321-winshell.txt</c>: the UTC instant, the process, where.</summary>
    private static string FileName(long utcTicks, string where)
    {
        return
            $"crash-{new DateTime(utcTicks, DateTimeKind.Utc).ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture)}Z-{Environment.ProcessId}-{Slug(where)}.txt";
    }

    [GeneratedRegex(@"^crash-(?<time>\d{8}-\d{6}-\d{7})Z-(?<pid>\d{1,10})-[a-z0-9\-]{1,40}\.txt$")]
    private static partial Regex ReportName();

    /// <summary>
    /// Where a report stands among the others: by the UTC instant in its name, then the process, then the name. The
    /// greatest is the newest.
    /// </summary>
    private readonly record struct ReportOrder(long Ticks, long Pid, string Name) : IComparable<ReportOrder>
    {
        public DateTimeOffset Written => new(Ticks, TimeSpan.Zero);

        public int CompareTo(ReportOrder other)
        {
            var order = (Ticks, Pid).CompareTo((other.Ticks, other.Pid));
            return order != 0 ? order : string.CompareOrdinal(Name, other.Name);
        }
    }

    /// <summary>Where the file <paramref name="name"/> stands among reports, or null when it is not named as one.</summary>
    private static ReportOrder? OrderOf(string name)
    {
        var m = ReportName().Match(name);
        if (!m.Success || !DateTime.TryParseExact(m.Groups["time"].Value, "yyyyMMdd-HHmmss-fffffff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
            return null;
        return new ReportOrder(time.Ticks, long.Parse(m.Groups["pid"].Value, CultureInfo.InvariantCulture), name);
    }

    /// <summary>A file name part: lowercase letters and digits, the rest dashes.</summary>
    private static string Slug(string text)
    {
        var slug = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
            if (slug.Length >= 40)
                break;
        }

        var result = slug.ToString().Trim('-');
        return result.Length > 0 ? result : "crash";
    }

    private string Format(DateTimeOffset time, string where, Exception error, bool fatal, string recovery)
    {
        var text = new StringBuilder();
        text.AppendLine(Heading);
        text.AppendLine("This file stays on this PC. Passwords, tokens and keys Winshell recognizes are taken out,");
        text.AppendLine("but read it before you attach it to a bug report: it has the paths of your config.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Time: {time:yyyy-MM-dd HH:mm:ss.fff zzz}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Winshell: {ProductVersion.Current.Text}");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine(CultureInfo.InvariantCulture, $".NET: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Process: {Environment.ProcessId}");
        if (shellId.Length > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Shell id: {shellId}");
        if (configPath.Length > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Config: {configPath}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Where: {CrashRedaction.Redact(where)}");
        text.AppendLine(fatal ? ClosedLine : OpenLine);
        if (recovery.Length > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Then: {recovery}");
        text.AppendLine();
        text.AppendLine(ExceptionHeading);
        text.AppendLine(CrashRedaction.Redact(Describe(error)));
        var log = LogTail();
        if (log.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Log: {logPath}, last {log.Count} lines");
            foreach (var line in log)
                text.AppendLine(line);
        }

        return text.ToString();
    }

    /// <summary>The exception as .NET writes it, or its type and message when writing it throws too.</summary>
    private static string Describe(Exception error)
    {
        try
        {
            return error.ToString();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return $"{error.GetType().FullName}: (the exception could not be written: {e.GetType().Name})";
        }
    }

    /// <summary>The last lines of the log, as they are. Only the end of the log is read.</summary>
    private List<string> LogTail()
    {
        if (logPath.Length == 0 || !File.Exists(logPath))
            return [];
        List<string> lines;
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - LogBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            // The first line may start halfway; the last is empty after the final newline.
            if (start > 0 && lines.Count > 0)
                lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return [.. lines.TakeLast(LogLines)];
    }

    /// <summary>
    /// Deletes the oldest reports beyond <see cref="Keep"/>. Only files named and headed as Winshell reports are
    /// counted or deleted, and nothing is deleted when the folder is a link or junction to somewhere else.
    /// </summary>
    private void Prune(string written)
    {
        try
        {
            var folder = new DirectoryInfo(Folder);
            if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint) || folder.LinkTarget is not null)
                return;
            var reports = new List<(FileInfo File, ReportOrder Order)>();
            foreach (var file in folder.EnumerateFiles("crash-*.txt", SearchOption.TopDirectoryOnly))
            {
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    || !string.Equals(file.DirectoryName, Folder, StringComparison.OrdinalIgnoreCase)
                    || OrderOf(file.Name) is not { } order
                    || !StartsWithHeading(file.FullName))
                    continue;
                reports.Add((file, order));
            }

            var old = reports.OrderByDescending(r => r.Order).Skip(Keep);
            foreach (var (file, _) in old)
            {
                if (string.Equals(file.FullName, written, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    file.Delete();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The reports in <paramref name="folder"/>, newest first. A report's name carries a UTC instant and its first
    /// line is the heading. A missing folder has none. A file whose name is not a report is left out.
    /// </summary>
    public static CrashList List(string folder)
    {
        if (!Directory.Exists(folder))
            return new CrashList([], null, null);
        try
        {
            var found = new List<(CrashRecord Record, ReportOrder Order)>();
            var failed = new List<(CrashFileError Error, ReportOrder Order)>();
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("crash-*.txt"))
            {
                if (OrderOf(file.Name) is not { } order)
                    continue;
                var read = ReadRecord(file.FullName, order.Written);
                if (read.Error is { } why)
                    failed.Add((new CrashFileError(file.FullName, order.Written, why), order));
                else if (read.Record is { } record)
                    found.Add((record, order));
            }

            var reports = found.OrderByDescending(r => r.Order).Select(r => r.Record).ToList();
            CrashFileError? fileError = failed.Count > 0 ? failed.MaxBy(r => r.Order).Error : null;
            return new CrashList(reports, null, fileError);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new CrashList([], $"{folder}: {e.Message}", null);
        }
    }

    private readonly record struct RecordRead(CrashRecord? Record, string? Error);

    /// <summary>The report in <paramref name="path"/>, or why it cannot be read. A file that is not a report has neither.</summary>
    private static RecordRead ReadRecord(string path, DateTimeOffset written)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            if (!IsHeading(reader.ReadLine()))
                return default;
            var closed = false;
            string? exception = null;
            var afterException = false;
            for (var i = 0; i < 40 && reader.ReadLine() is { } line; i++)
            {
                if (line == ClosedLine)
                    closed = true;
                if (afterException && exception is null && line.Length > 0)
                    exception = line;
                afterException = line == ExceptionHeading;
            }

            return new RecordRead(new CrashRecord(path, written, closed, exception ?? ""), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new RecordRead(null, e.Message);
        }
    }

    /// <summary>Whether a line is the heading a report starts with. The whole line must match.</summary>
    private static bool IsHeading([NotNullWhen(true)] string? line)
    {
        return line == Heading;
    }

    /// <summary>Whether the file's first line is the report heading. A file that cannot be read is not one.</summary>
    private static bool StartsWithHeading(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return IsHeading(reader.ReadLine());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
