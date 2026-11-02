using System.Globalization;

namespace Winshell;

/// <summary>
/// The next start's look at the crash folder. A fatal report newer than the last clean exit is shown once.
/// The record is TOML in the state folder: <c>clean</c> is the last exit that was not a crash, and <c>shown</c>
/// is the report already shown.
/// </summary>
public static class CrashFollowUp
{
    public readonly record struct Pending(string Path, string ExceptionLine);

    /// <summary>The newest crash report that says Winshell closed, when it has not been shown and it is newer than the last clean exit.</summary>
    public static Pending? Find(string crashFolder, string stateFile)
    {
        try
        {
            if (!Directory.Exists(crashFolder))
                return null;
            FileInfo? newest = null;
            foreach (var file in new DirectoryInfo(crashFolder).EnumerateFiles("crash-*.txt"))
            {
                if (!StartsWithHeading(file.FullName))
                    continue;
                if (newest is null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                    newest = file;
            }
            if (newest is null)
                return null;
            var state = Read(stateFile);
            if (state.Shown is not null && string.Equals(state.Shown, newest.FullName, StringComparison.OrdinalIgnoreCase))
                return null;
            if (state.Clean is { } clean && newest.LastWriteTimeUtc <= clean.UtcDateTime)
                return null;
            return ReadClosed(newest.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Records an exit that was not a crash.</summary>
    public static void MarkClean(string stateFile, DateTimeOffset when)
    {
        var state = Read(stateFile);
        Write(stateFile, when, state.Shown);
    }

    /// <summary>Records that <paramref name="reportPath"/> was shown, so the next start does not show it again.</summary>
    public static void MarkShown(string stateFile, string reportPath)
    {
        var state = Read(stateFile);
        Write(stateFile, state.Clean, reportPath);
    }

    private readonly record struct State(DateTimeOffset? Clean, string? Shown);

    private static Pending? ReadClosed(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var closed = false;
        string? exception = null;
        var afterException = false;
        for (var i = 0; i < 40 && reader.ReadLine() is { } line; i++)
        {
            if (i == 0 && line != "Winshell crash report")
                return null;
            if (line == "Winshell closed: yes")
                closed = true;
            if (afterException && exception is null && line.Length > 0)
                exception = line;
            afterException = line == "Exception";
        }
        return closed ? new Pending(path, exception ?? "") : null;
    }

    private static bool StartsWithHeading(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadLine() == "Winshell crash report";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static State Read(string stateFile)
    {
        if (!File.Exists(stateFile))
            return default;
        DateTimeOffset? clean = null;
        string? shown = null;
        foreach (var raw in File.ReadAllLines(stateFile))
        {
            var line = raw.Trim();
            if (line.StartsWith("clean = ", StringComparison.Ordinal) && Unquote(line[8..]) is { } cleanText
                && DateTimeOffset.TryParse(cleanText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                clean = when;
            else if (line.StartsWith("shown = ", StringComparison.Ordinal))
                shown = Unquote(line[8..]);
        }
        return new State(clean, shown);
    }

    private static void Write(string stateFile, DateTimeOffset? clean, string? shown)
    {
        var dir = Path.GetDirectoryName(stateFile);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var text = new System.Text.StringBuilder();
        if (clean is { } when)
            text.Append("clean = ").Append(Quote(when.ToString("o", CultureInfo.InvariantCulture))).Append('\n');
        if (shown is { Length: > 0 })
            text.Append("shown = ").Append(Quote(shown)).Append('\n');
        File.WriteAllText(stateFile, text.ToString());
    }

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string? Unquote(string token)
    {
        token = token.Trim();
        if (token.Length < 2 || token[0] != '"' || token[^1] != '"')
            return null;
        return token[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}
