using System.Globalization;

namespace Winshell;

/// <summary>
/// The next start's look at the crash folder. A fatal report newer than the last clean exit is shown once.
/// The record is TOML in the state folder: <c>clean</c> is the last exit that was not a crash, and <c>shown</c>
/// is the report already shown.
/// </summary>
internal sealed partial class CrashReports
{
    /// <summary>The newest crash report that says Winshell closed, when it has not been shown and it is newer than the last clean exit.</summary>
    public static CrashRecord? Find(string crashFolder, string stateFile)
    {
        try
        {
            var reports = List(crashFolder).Reports;
            if (reports.Count == 0 || !reports[0].Closed)
                return null;
            var newest = reports[0];
            var state = ReadState(stateFile);
            if (state.Shown is not null &&
                string.Equals(state.Shown, newest.Path, StringComparison.OrdinalIgnoreCase))
                return null;
            if (state.Clean is { } clean && newest.Written <= clean)
                return null;
            return newest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Records an exit that was not a crash.</summary>
    public static void MarkClean(string stateFile, DateTimeOffset when)
    {
        Update(stateFile, when, null);
    }

    /// <summary>Records that <paramref name="reportPath"/> was shown, so the next start does not show it again.</summary>
    public static void MarkShown(string stateFile, string reportPath)
    {
        Update(stateFile, null, reportPath);
    }

    /// <summary>
    /// Sets what is given and keeps the rest. Instances share the record, so another can save it between this read
    /// and write; then it is read again, a few times at most, and the mark is dropped after that.
    /// </summary>
    private static void Update(string stateFile, DateTimeOffset? clean, string? shown)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var state = ReadState(stateFile);
            if (Put(stateFile, clean ?? state.Clean, shown ?? state.Shown, state.Text))
                return;
        }
    }

    private readonly record struct ExitState(DateTimeOffset? Clean, string? Shown, string? Text);

    private static ExitState ReadState(string stateFile)
    {
        var load = TomlDocument.Load(stateFile);
        switch (load.Kind)
        {
            case TomlRead.Missing:
                return default;
            case TomlRead.Unreadable or TomlRead.TooLarge:
                throw new IOException(load.Message);
            case TomlRead.Invalid:
                return new ExitState(null, null, load.Text);
            case TomlRead.Parsed:
                var values = load.Document!.Values;
                DateTimeOffset? clean = null;
                if (values.GetValueOrDefault("clean") is string cleanText &&
                    DateTimeOffset.TryParse(cleanText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                        out var when))
                    clean = when;
                var shown = values.GetValueOrDefault("shown") as string;
                return new ExitState(clean, shown, load.Text);
            default:
                return default;
        }
    }

    /// <summary>Writes the record, or returns false when a hand save landed and the file was left as it is.</summary>
    private static bool Put(string stateFile, DateTimeOffset? clean, string? shown, string? expected)
    {
        // Edit the file that is there. A file that is not TOML is replaced with the record alone.
        if (expected is null || !TomlDocument.TryParse(stateFile, expected, out var document, out _))
            document = TomlDocument.Empty(stateFile);
        if (clean is { } when)
            document.Set("clean", when.ToString("o", CultureInfo.InvariantCulture));
        if (shown is { Length: > 0 })
            document.Set("shown", shown);
        // File.WriteAllText writes UTF-8 without a byte order mark. Keep that.
        return AtomicFile.Save(stateFile, document.ToString(), false, expected) == AtomicSave.Saved;
    }
}
