using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Winshell.Runtime.Shell;

/// <summary>A crash report on disk, and what it is about.</summary>
public sealed record CrashReport(string Path, string Where, Exception Error, bool Fatal);

/// <summary>
/// Writes crash reports: plain text files in one folder on this PC, for a bug report. Nothing is sent anywhere.
/// A report says where the runtime failed, the exception with its stack, and the end of the instance's log.
/// </summary>
public sealed class CrashReports(string folder)
{
    private const int LogLines = 40;
    private readonly object _lock = new();
    private readonly HashSet<string> _reported = [];

    /// <summary>The folder reports go to: <see cref="ShellLocations.CrashRoot"/> for <c>winshell.exe</c>.</summary>
    public string Folder { get; } = folder;

    /// <summary>How many reports the folder keeps. Writing one deletes the oldest beyond this.</summary>
    public int Keep { get; init; } = 50;

    public string ShellId { get; set; } = "";

    public string ConfigPath { get; set; } = "";

    /// <summary>The instance's log, whose last lines go into each report. Empty when there is none.</summary>
    public string LogPath { get; set; } = "";

    /// <summary>Raised after a report is written, on the thread that wrote it.</summary>
    public event Action<CrashReport>? Written;

    /// <summary>Writes a report. Returns null when the folder cannot be written; a report must never crash the shell.</summary>
    public CrashReport? Write(string where, Exception error, bool fatal = false)
    {
        CrashReport report;
        lock (_lock)
        {
            var time = DateTimeOffset.Now;
            try
            {
                Directory.CreateDirectory(Folder);
                var path = NewPath(time, where);
                File.WriteAllText(path, Format(time, where, error, fatal));
                report = new CrashReport(path, where, error, fatal);
                Prune();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        Written?.Invoke(report);
        return report;
    }

    /// <summary>
    /// Writes a report unless one was already written for the same exception thrown from the same place, so work
    /// that fails each time it runs, such as a timer, writes one report rather than filling the disk.
    /// </summary>
    public CrashReport? WriteOnce(string where, Exception error)
    {
        lock (_lock)
        {
            if (!_reported.Add(Signature(where, error)))
                return null;
        }
        return Write(where, error);
    }

    private static string Signature(string where, Exception error)
    {
        var frame = error.StackTrace?.Split('\n', 2)[0].Trim() ?? "";
        return $"{where}|{error.GetType().FullName}|{frame}";
    }

    private string NewPath(DateTimeOffset time, string where)
    {
        var name = time.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Slug(where);
        var path = System.IO.Path.Combine(Folder, name + ".txt");
        for (var i = 2; File.Exists(path); i++)
            path = System.IO.Path.Combine(Folder, $"{name}-{i}.txt");
        return path;
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

    private string Format(DateTimeOffset time, string where, Exception error, bool fatal)
    {
        var text = new StringBuilder();
        text.AppendLine("Winshell crash report");
        text.AppendLine("This file stays on this PC. Attach it to a bug report if you like; read it first, it has the paths of your config.");
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
        text.AppendLine(CultureInfo.InvariantCulture, $"Where: {where}");
        text.AppendLine(fatal ? "Winshell closed: yes" : "Winshell closed: no");
        text.AppendLine();
        text.AppendLine("Exception");
        text.AppendLine(error.ToString());
        var log = LogTail();
        if (log.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Log: {LogPath}, last {log.Count} lines");
            foreach (var line in log)
                text.AppendLine(line);
        }
        return text.ToString();
    }

    /// <summary>The last lines of the log. Only its end is read, however long it has grown.</summary>
    private List<string> LogTail()
    {
        if (LogPath.Length == 0 || !File.Exists(LogPath))
            return [];
        try
        {
            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Max(0, stream.Length - 16 * 1024), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            // The first line may start halfway; the last is empty after the final newline.
            if (stream.Length > 16 * 1024 && lines.Count > 0)
                lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines.TakeLast(LogLines).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Prune()
    {
        var reports = new DirectoryInfo(Folder).GetFiles("*.txt").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
        foreach (var old in reports.Skip(Keep))
        {
            try
            {
                old.Delete();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
