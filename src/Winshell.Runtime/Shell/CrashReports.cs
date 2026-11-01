using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Winshell.Runtime.Shell;

/// <summary>
/// A crash report on disk, and what it is about. A window's report also names the window for a person,
/// <see cref="Subject"/>, and says what Winshell did about it, <see cref="Recovery"/>.
/// </summary>
public sealed record CrashReport(string Path, string Where, Exception Error, bool Fatal)
{
    /// <summary>What failed, as a sentence names it: "the taskbar", "Winshell Settings", "a window". Empty when it is not a window.</summary>
    public string Subject { get; init; } = "";

    /// <summary>What Winshell did about it, such as "Winshell opens it again in 1 second." Empty when nothing.</summary>
    public string Recovery { get; init; } = "";
}

/// <summary>
/// Writes crash reports: plain text files in one folder on this PC, for a bug report. Nothing is sent anywhere.
/// A report says where the runtime failed, the exception with its stack, and the end of the instance's log, with
/// the secrets <see cref="CrashRedaction"/> recognizes taken out and what scripts logged left out. Several instances
/// can share the folder: each report gets a file of its own, and only files that are Winshell reports are deleted.
/// </summary>
public sealed partial class CrashReports(string folder)
{
    private const string Heading = "Winshell crash report";
    private const int LogLines = 40;
    private const int LogBytes = 16 * 1024;
    private const int ErrorFileExists = 80;
    private static readonly object TimeGate = new();
    private static long _lastTicks;
    private readonly object _lock = new();
    private readonly HashSet<string> _reported = [];

    /// <summary>The folder reports go to: <see cref="ShellLocations.CrashRoot"/> for <c>winshell.exe</c>.</summary>
    public string Folder { get; } = Path.GetFullPath(folder);

    /// <summary>How many reports the folder keeps. Writing one deletes the oldest beyond this.</summary>
    public int Keep { get; init; } = 50;

    public string ShellId { get; set; } = "";

    public string ConfigPath { get; set; } = "";

    /// <summary>The instance's log, whose last lines go into each report. Empty when there is none.</summary>
    public string LogPath { get; set; } = "";

    /// <summary>
    /// Where a report that could not be written, or a handler that threw, is recorded. Unset when the caller
    /// only needs the null return.
    /// </summary>
    public Log? Log { get; set; }

    /// <summary>The time a report is written at. Tests set it.</summary>
    internal TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Raised after a report is written, on the thread that wrote it.</summary>
    public event Action<CrashReport>? Written;

    /// <summary>
    /// Writes a report. Returns null when the folder cannot be written. Nothing it does throws: it runs when
    /// something else already failed, sometimes as the process ends.
    /// </summary>
    public CrashReport? Write(string where, Exception error, bool fatal = false, string subject = "", string recovery = "")
    {
        CrashReport? report;
        lock (_lock)
            report = WriteLocked(where, error, fatal, subject, recovery);
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
            report = WriteLocked(where, error, fatal: false, subject, recovery);
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
            Log?.Error("shell", $"Could not write a crash report: {e.Message}");
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
            {
                foreach (var inner in aggregate.InnerExceptions)
                    Add(inner, depth + 1);
            }
            else if (e.InnerException is { } inner)
            {
                Add(inner, depth + 1);
            }
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
            Directory.CreateDirectory(Folder);
            var ticks = NextTicks(now.UtcTicks);
            // The name is unique to this process and instant; another instance writing into the folder at the same
            // time cannot take it over, since each file is created only if it is not there yet.
            for (var attempt = 0; ; attempt++)
            {
                var path = Path.Combine(Folder, FileName(ticks, where));
                try
                {
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.Write(text);
                    Prune(path);
                    return new CrashReport(path, where, error, fatal) { Subject = subject, Recovery = recovery };
                }
                catch (IOException e) when ((e.HResult & 0xFFFF) == ErrorFileExists && attempt < 16)
                {
                    ticks = NextTicks(ticks);
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log?.Error("shell", $"Could not write a crash report: {e.Message}");
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

    /// <summary><c>crash-20261029-140312-1234567Z-4321-panelwindow-in-taskbar-qml.txt</c>: the UTC instant, the process, where.</summary>
    private static string FileName(long utcTicks, string where) =>
        $"crash-{new DateTime(utcTicks, DateTimeKind.Utc).ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture)}Z-{Environment.ProcessId}-{Slug(where)}.txt";

    [GeneratedRegex(@"^crash-(?<time>\d{8}-\d{6}-\d{7})Z-(?<pid>\d{1,10})-[a-z0-9\-]{1,40}\.txt$")]
    private static partial Regex ReportName();

    /// <summary>The order reports are kept in, newest first: the UTC instant in the name, then the process.</summary>
    private static (long Ticks, long Pid)? OrderOf(string name)
    {
        var m = ReportName().Match(name);
        if (!m.Success || !DateTime.TryParseExact(m.Groups["time"].Value, "yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
            return null;
        return (time.Ticks, long.Parse(m.Groups["pid"].Value, CultureInfo.InvariantCulture));
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
        text.AppendLine("This file stays on this PC. Passwords, tokens and keys Winshell recognizes are taken out, and what scripts logged is left out,");
        text.AppendLine("but read it before you attach it to a bug report: it has the paths of your config.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Time: {time:yyyy-MM-dd HH:mm:ss.fff zzz}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Winshell: {ProductVersion.Current.Text}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine(CultureInfo.InvariantCulture, $".NET: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Process: {Environment.ProcessId}");
        if (ShellId.Length > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Shell id: {ShellId}");
        if (ConfigPath.Length > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Config: {ConfigPath}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Where: {CrashRedaction.Redact(where)}");
        text.AppendLine(fatal ? "Winshell closed: yes" : "Winshell closed: no");
        if (recovery.Length > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Then: {recovery}");
        text.AppendLine();
        text.AppendLine("Exception");
        text.AppendLine(CrashRedaction.Redact(Describe(error)));
        var (log, omitted) = LogTail();
        if (log.Count > 0 || omitted > 0)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Log: {LogPath}, last {log.Count} lines");
            if (omitted > 0)
                text.AppendLine(CultureInfo.InvariantCulture, $"({omitted} lines of script output left out)");
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

    // A log line as winshell.exe writes it: "2026-10-29 12:00:00.000  INFO window: message". Older logs omit
    // the date. No category means QML's console. A line that does not start like this continues the line before.
    [GeneratedRegex(@"^(?:\d{4}-\d{2}-\d{2} )?\d{2}:\d{2}:\d{2}\.\d{3} +(?:DEBUG|INFO|WARN|ERROR|FATAL)(?<category> [^\s:]+)?:")]
    private static partial Regex LogLine();

    /// <summary>
    /// The last lines of the log, cleaned. Lines without a category are what a configuration's scripts printed,
    /// which can be anything, so they are left out and counted. Only the end of the log is read.
    /// </summary>
    private (List<string> Lines, int Omitted) LogTail()
    {
        if (LogPath.Length == 0 || !File.Exists(LogPath))
            return ([], 0);
        List<string> lines;
        try
        {
            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
            return ([], 0);
        }
        var kept = new List<string>();
        // For each kept line, how many lines were left out before the next kept one.
        var gaps = new List<int>();
        var before = 0;
        // Until the first line that says what it is, a line cannot be told from script output.
        var keep = false;
        foreach (var line in lines)
        {
            if (LogLine().Match(line) is { Success: true } m)
                keep = m.Groups["category"].Success;
            if (keep)
            {
                kept.Add(CrashRedaction.Redact(line));
                gaps.Add(0);
            }
            else if (gaps.Count > 0)
                gaps[^1]++;
            else
                before++;
        }
        // Only what was left out among the lines shown. With nothing kept, that is every line.
        if (kept.Count == 0)
            return ([], before);
        var first = Math.Max(0, kept.Count - LogLines);
        return (kept.GetRange(first, kept.Count - first), gaps.Skip(first).Sum() + (first == 0 ? before : 0));
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
            var reports = new List<(FileInfo File, (long Ticks, long Pid) Order)>();
            foreach (var file in folder.EnumerateFiles("crash-*.txt", SearchOption.TopDirectoryOnly))
            {
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    || !string.Equals(file.DirectoryName, Folder, StringComparison.OrdinalIgnoreCase)
                    || OrderOf(file.Name) is not { } order
                    || !StartsWithHeading(file.FullName))
                    continue;
                reports.Add((file, order));
            }
            var old = reports
                .OrderByDescending(r => r.Order.Ticks)
                .ThenByDescending(r => r.Order.Pid)
                .ThenByDescending(r => r.File.Name, StringComparer.Ordinal)
                .Skip(Keep);
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

    private static bool StartsWithHeading(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadLine() == Heading;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
