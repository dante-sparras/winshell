using Winshell.Native;

namespace Winshell;

internal enum NoticeKind
{
    /// <summary>The configuration did not build. It stays until it is fixed or dismissed.</summary>
    BuildError,

    /// <summary>A TOML or XAML file does not read, under <c>winshell dev</c>. It stays until the file is fixed.</summary>
    ConfigError,

    /// <summary>Something failed and Winshell wrote a crash report. It goes away by itself.</summary>
    Crash
}

/// <summary>What a notice says, and the file its button opens. A zero <paramref name="Timeout"/> keeps it until it is dismissed.</summary>
internal sealed record Notice(
    NoticeKind Kind,
    string Title,
    string Detail,
    string Body,
    string Note,
    string ActionText,
    string Target,
    TimeSpan Timeout);

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
    private readonly Func<bool> _isQuiet;
    private IDisposable? _window;
    private bool _disposed;

    /// <param name="loop">The shell's loop. Notices show and close on it, whatever thread wrote the report.</param>
    /// <param name="log">Where each notice, and a notice that could not be shown, is written.</param>
    /// <param name="crashes">The crash reports. One the shell survives shows a notice.</param>
    /// <param name="show">Shows a notice, given what its button does and what closing it does. Disposing takes it away.</param>
    /// <param name="open">Opens a file: the project that did not build, or a crash report.</param>
    /// <param name="isQuiet">Whether Windows is holding notifications back. A failure waits for it, and goes to the log.</param>
    public ShellNotices(IEventLoop loop, Log log, CrashReports crashes, Func<Notice, Action, Action, IDisposable> show,
        Action<string> open, Func<bool> isQuiet)
    {
        _loop = loop;
        _log = log;
        _crashes = crashes;
        _show = show;
        _open = open;
        _isQuiet = isQuiet;
        crashes.Written += OnReport;
    }

    /// <summary>The notice showing now, or null.</summary>
    public Notice? Current { get; private set; }

    /// <summary>A full-screen app, a presentation, a busy state or quiet hours, the states in which Windows holds a toast back.</summary>
    internal static bool QuietState(int state)
    {
        return state is QunsBusy or QunsFullScreen or QunsPresentation or QunsQuietTime;
    }

    /// <summary>Whether Windows is holding notifications back, as it reports it now.</summary>
    internal static bool WindowsIsQuiet()
    {
        return Win32.SHQueryUserNotificationState(out var state) == 0 && QuietState(state);
    }

    /// <summary>How long a crash notice stays for someone who asked Windows to keep notifications up. A shorter setting doesn't shorten it.</summary>
    internal static TimeSpan CrashTimeoutFor(uint messageSeconds)
    {
        return TimeSpan.FromSeconds(Math.Max(CrashTimeout.TotalSeconds, messageSeconds));
    }

    /// <summary>The notice for a configuration that did not build, while the last one that did keeps running.</summary>
    public static Notice ForBuildError(string error, string projectFile)
    {
        return new Notice(
            NoticeKind.BuildError,
            "Your configuration did not build",
            Path.GetFileName(projectFile),
            error,
            "Winshell keeps running the last version that built.",
            "Open project",
            projectFile,
            TimeSpan.Zero);
    }

    /// <summary>The notice for a file that does not read, while what came from it before keeps working.</summary>
    public static Notice ForConfigError(string error, string file)
    {
        return new Notice(
            NoticeKind.ConfigError,
            "This file has an error",
            Path.GetFileName(file),
            error,
            "Winshell keeps what it had.",
            "Open file",
            file,
            TimeSpan.Zero);
    }

    /// <summary>Shows a file's error, or takes it away once the file reads again.</summary>
    public void ShowConfigError(string file, string error)
    {
        if (error.Length > 0)
            Show(ForConfigError(error, file));
        else if (Current is { Kind: NoticeKind.ConfigError } current && current.Target == file)
            Hide();
    }

    /// <summary>
    /// Shows why a project did not build, or, given no error, takes away the notice of that project's last failed build.
    /// A build that works leaves any other notice alone.
    /// </summary>
    public void ShowBuildError(string error, string projectFile)
    {
        if (error.Length > 0)
            Show(ForBuildError(error, projectFile));
        else if (Current is { Kind: NoticeKind.BuildError } current && current.Target == projectFile)
            Hide();
    }

    /// <summary>The notice for something that failed: what failed, what Winshell did, and the report.</summary>
    public static Notice ForCrash(CrashReport report, TimeSpan timeout)
    {
        return ForCrash(report.Subject.Length > 0 ? report.Subject : "a window", report.Recovery, report.Path, timeout);
    }

    private static Notice ForCrash(string subject, string body, string reportPath, TimeSpan timeout)
    {
        var title = char.ToUpperInvariant(subject[0]) + subject[1..] + " stopped working";
        return new Notice(NoticeKind.Crash, title, "", body, "A crash report was saved on this PC.", "Open report",
            reportPath, timeout);
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
    public void ShowPrevious(string stateFile)
    {
        if (CrashReports.Find(_crashes.Folder, stateFile) is not { } closed)
            return;
        CrashReports.MarkShown(stateFile, closed.Path);
        var body = closed.ExceptionLine.Length > 0 ? closed.ExceptionLine : "Winshell closed the last time it ran.";
        var notice = ForCrash("Winshell", body, closed.Path, ReadCrashTimeout());
        _loop.Post(() => Show(notice));
    }

    /// <summary>Shows each file written to the log that does not read, until it reads again.</summary>
    public void FollowFileErrors()
    {
        _log.FileErrorChanged += ShowConfigError;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _crashes.Written -= OnReport;
        _log.FileErrorChanged -= ShowConfigError;
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
            if (_isQuiet())
            {
                _log.Info("shell", $"A crash notice was held back while Windows is quiet. See {report.Path}");
                return;
            }

            Show(ForCrash(report, ReadCrashTimeout()));
        });
    }

    private static TimeSpan ReadCrashTimeout()
    {
        return MessageDuration.Seconds() is { } seconds ? CrashTimeoutFor(seconds) : CrashTimeout;
    }
}
