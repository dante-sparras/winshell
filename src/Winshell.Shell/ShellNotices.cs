using Winshell.Native;

namespace Winshell;

internal enum NoticeKind
{
    /// <summary>The configuration did not build. It stays until it is fixed or dismissed.</summary>
    BuildError,

    /// <summary>A TOML file does not read, under <c>winshell dev</c>. It stays until the file is fixed.</summary>
    ConfigError,

    /// <summary>Something failed and Winshell wrote a crash report. It goes away by itself.</summary>
    Crash,
}

/// <summary>What a notice says, and the file its button opens. A zero <paramref name="Timeout"/> keeps it until it is dismissed.</summary>
internal sealed record Notice(NoticeKind Kind, string Title, string Detail, string Body, string Note, string ActionText, string Target, TimeSpan Timeout);

/// <summary>
/// Winshell's own notices: a configuration that did not build, and something that failed. The host shows them, so they
/// appear whatever the configuration is doing. One shows at a time, and a new one replaces it, except that a failure does
/// not replace a build error, which is what the user can fix. A notice does not take the keyboard;
/// <c>winshell ipc call notice dismiss</c> closes whichever one is showing. Nothing is sent anywhere: the button opens a
/// file on this PC, and never runs one.
/// </summary>
internal sealed class ShellNotices : IDisposable
{
    /// <summary>How long a crash notice stays, as a long Windows notification does.</summary>
    public static readonly TimeSpan CrashTimeout = TimeSpan.FromSeconds(25);

    private const int QunsBusy = 2, QunsFullScreen = 3, QunsPresentation = 4, QunsQuietTime = 6;

    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly CrashReports _crashes;
    private readonly Func<Notice, Action, Action, IDisposable> _show;
    private readonly Action<string> _open;
    private IDisposable? _window;
    private bool _disposed;

    /// <param name="show">Shows a notice, given what its button does and what closing it does. Disposing takes it away.</param>
    /// <param name="open">Opens a file: the project that did not build, or a crash report.</param>
    public ShellNotices(IEventLoop loop, Log log, CrashReports crashes, Func<Notice, Action, Action, IDisposable> show, Action<string> open)
    {
        _loop = loop;
        _log = log;
        _crashes = crashes;
        _show = show;
        _open = open;
        crashes.Written += OnReport;
    }

    /// <summary>Whether Windows is holding notifications back. A test replaces this.</summary>
    internal Func<bool> IsQuiet { get; set; } = () => Win32.SHQueryUserNotificationState(out var state) == 0 && QuietState(state);

    /// <summary>The notice showing now, or null.</summary>
    public Notice? Current { get; private set; }

    /// <summary>A full-screen app, a presentation, a busy state or quiet hours, the states in which Windows holds a toast back.</summary>
    internal static bool QuietState(int state) => state is QunsBusy or QunsFullScreen or QunsPresentation or QunsQuietTime;

    /// <summary>How long a crash notice stays for someone who asked Windows to keep notifications up. A shorter setting doesn't shorten it.</summary>
    internal static TimeSpan CrashTimeoutFor(uint messageSeconds) =>
        TimeSpan.FromSeconds(Math.Max(CrashTimeout.TotalSeconds, messageSeconds));

    /// <summary>The notice for a configuration that did not build, while the last one that did keeps running.</summary>
    public static Notice ForBuildError(string error, string projectFile) => new(
        NoticeKind.BuildError,
        "Your configuration did not build",
        Path.GetFileName(projectFile),
        error,
        "Winshell keeps running the last version that built.",
        "Open project",
        projectFile,
        TimeSpan.Zero);

    /// <summary>The notice for a config file that does not read, while the values from before keep working.</summary>
    public static Notice ForConfigError(string error, string file) => new(
        NoticeKind.ConfigError,
        "A config file has an error",
        Path.GetFileName(file),
        error,
        "Winshell keeps the values it had.",
        "Open file",
        file,
        TimeSpan.Zero);

    /// <summary>Shows a config file's error, or takes it away once the file reads again.</summary>
    public void ShowConfigError(string file, string error)
    {
        if (error.Length > 0)
            Show(ForConfigError(error, file));
        else if (Current is { Kind: NoticeKind.ConfigError } current && current.Target == file)
            Hide();
    }

    /// <summary>The notice for something that failed: what failed, what Winshell did, and the report.</summary>
    public static Notice ForCrash(CrashReport report, TimeSpan timeout)
    {
        var subject = report.Subject.Length > 0 ? report.Subject : "a window";
        var title = char.ToUpperInvariant(subject[0]) + subject[1..] + " stopped working";
        return new Notice(NoticeKind.Crash, title, "", report.Recovery, "A crash report was saved on this PC.", "Open report", report.Path, timeout);
    }

    /// <summary>Shows a notice in place of the one showing. The notice is what reports problems, so one it has itself goes to the log.</summary>
    public void Show(Notice notice)
    {
        if (_disposed)
            return;
        Hide();
        try
        {
            _log.Info("notice", $"{notice.Title}: {notice.Detail}");
            Current = notice;
            _window = _show(notice, Act, () => _loop.Post(Hide));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Current = null;
            _log.Error("notice", $"Could not show a notice: {e.Message}");
        }
    }

    /// <summary>Closes the notice showing, if any.</summary>
    public void Hide()
    {
        Current = null;
        _window?.Dispose();
        _window = null;
    }

    /// <summary>Says once that Winshell closed the last time it ran, when it left a report no one has seen.</summary>
    public void ShowPrevious(string stateFile, TimeSpan timeout)
    {
        if (CrashFollowUp.Find(_crashes.Folder, stateFile) is not { } closed)
            return;
        CrashFollowUp.MarkShown(stateFile, closed.Path);
        var recovery = closed.ExceptionLine.Length > 0 ? closed.ExceptionLine : "Winshell closed the last time it ran.";
        var previous = new CrashReport(closed.Path, "Winshell", new InvalidOperationException(recovery), true) { Subject = "Winshell", Recovery = recovery };
        _loop.Post(() => Show(ForCrash(previous, timeout)));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _crashes.Written -= OnReport;
        Hide();
    }

    private void Act()
    {
        if (Current is not { } notice)
            return;
        _open(notice.Target);
        _loop.Post(Hide);
    }

    // A report can be written on any thread, and a process that is ending has no time to show one.
    private void OnReport(CrashReport report)
    {
        if (report.Fatal || report.Recovery.Length == 0)
            return;
        _loop.Post(() =>
        {
            if (Current?.Kind == NoticeKind.BuildError)
                return;
            if (IsQuiet())
            {
                _log.Info("shell", $"A crash notice was held back while Windows is quiet. See {report.Path}");
                return;
            }
            Show(ForCrash(report, ReadCrashTimeout()));
        });
    }

    internal static unsafe TimeSpan ReadCrashTimeout()
    {
        uint seconds = 0;
        return Win32.SystemParametersInfoW(Win32.SPI_GETMESSAGEDURATION, 0, &seconds, 0) ? CrashTimeoutFor(seconds) : CrashTimeout;
    }
}
