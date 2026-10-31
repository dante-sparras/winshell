using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows;

public enum NoticeKind
{
    /// <summary>The configuration failed to load or reload. It goes away when a reload succeeds.</summary>
    ConfigError,

    /// <summary>A window failed and Winshell wrote a crash report. It goes away by itself.</summary>
    Crash,
}

/// <summary>What a notice says, and the file its button opens.</summary>
public sealed record Notice(NoticeKind Kind, string Title, string Detail, string Body, string Note, string ActionText, string Target, TimeSpan Timeout);

/// <summary>
/// Winshell's own notices: a configuration error, and a window that failed. Each is a window drawn like a Windows 11
/// notification by <c>ShellNotice.qml</c>, in an engine of its own, so it shows whatever the configuration is doing,
/// including nothing. One notice shows at a time: a new one replaces it, except that a window that failed does not
/// replace a configuration error, which stays until it is fixed or dismissed. Nothing is sent anywhere: the button
/// opens a file on this PC, and never runs one. <c>WINSHELL_NO_RELOAD_POPUP=1</c> and <c>Shell.inhibitReloadPopup()</c>
/// hide configuration errors.
/// </summary>
public sealed partial class ShellNotices : IDisposable
{
    /// <summary>How long a crash notice stays, as a long Windows notification does.</summary>
    public static readonly TimeSpan CrashTimeout = TimeSpan.FromSeconds(25);

    /// <summary>
    /// How long a crash notice stays for someone who asked Windows to keep notifications up for
    /// <paramref name="messageSeconds"/>. A shorter setting doesn't shorten a notice about a crash.
    /// </summary>
    internal static TimeSpan CrashTimeoutFor(uint messageSeconds) =>
        TimeSpan.FromSeconds(Math.Max(CrashTimeout.TotalSeconds, messageSeconds));

    private static unsafe TimeSpan ReadCrashTimeout()
    {
        uint seconds = 0;
        return Win32.SystemParametersInfoW(Win32.SPI_GETMESSAGEDURATION, 0, &seconds, 0) ? CrashTimeoutFor(seconds) : CrashTimeout;
    }

    private const string Resource = "Winshell.Platform.Windows.ShellNotice.qml";
    private readonly ShellHost _host;
    private readonly IEventLoop _loop;
    private readonly IShellPlatform _platform;
    private readonly Log _log;
    private readonly Action<string> _open;
    private readonly Func<ModuleRegistry> _modules;
    private QmlEngine? _engine;
    private ScreenRegistry? _screens;
    private bool _disposed;
    private bool _fileNotice;

    /// <param name="open">Opens a file: the configuration file that has the error, or a crash report.</param>
    /// <param name="modules">The modules the notice is made with; it needs <c>Winshell.Theme</c> and <c>WindowEffects</c>.</param>
    public ShellNotices(ShellHost host, IEventLoop loop, IShellPlatform platform, Log log, Action<string> open, Func<ModuleRegistry>? modules = null)
    {
        _host = host;
        _loop = loop;
        _platform = platform;
        _log = log;
        _open = open;
        _modules = modules ?? StandardModules.Create;
        host.Reloaded += OnReloaded;
        host.FileError += OnFileError;
        if (host.Crashes is { } crashes)
            crashes.Written += OnReport;
    }

    /// <summary>Whether Windows is holding notifications back. A test replaces this.</summary>
    internal Func<bool> IsQuiet { get; set; } = () => Win32.SHQueryUserNotificationState(out var state) == 0 && QuietState(state);

    /// <summary>
    /// A full-screen app, a presentation, a busy state or quiet hours, the states in which Windows holds a toast back.
    /// </summary>
    internal static bool QuietState(int state) => state is QunsBusy or QunsFullScreen or QunsPresentation or QunsQuietTime;

    private const int QunsBusy = 2, QunsFullScreen = 3, QunsPresentation = 4, QunsQuietTime = 6;

    /// <summary>The notice showing now, or null.</summary>
    public Notice? Current { get; private set; }

    /// <summary>The notice's window while one shows.</summary>
    public ShellWindow? Window { get; private set; }

    // Everything a reload decides is posted, so two reloads in one turn of the loop show what the last one found.
    private void OnReloaded(string? error)
    {
        if (error is not null && _host.ShowReloadPopup)
        {
            _fileNotice = false;
            var notice = ForConfigError(error, _host.RootFile, running: _host.Root is not null);
            _loop.Post(() => Show(notice));
            return;
        }
        // Fixed, or a failure the shell hid: an older error no longer says what is wrong.
        // A config file that still does not parse keeps its own notice.
        _loop.Post(() =>
        {
            if (!_fileNotice && Current?.Kind == NoticeKind.ConfigError)
                Hide();
        });
    }

    private void OnFileError(string? error)
    {
        if (error is not null && _host.ShowReloadPopup)
        {
            var notice = ForConfigError(error, _host.RootFile, running: _host.Root is not null);
            _loop.Post(() =>
            {
                _fileNotice = true;
                Show(notice);
            });
            return;
        }
        _loop.Post(() =>
        {
            if (_fileNotice && Current?.Kind == NoticeKind.ConfigError)
            {
                _fileNotice = false;
                Hide();
            }
        });
    }

    // A report can be written on any thread; a process that is ending has no time to show one. A configuration
    // error is what the user can fix, so a window that failed leaves it showing; its report is on disk and in the log.
    private void OnReport(CrashReport report)
    {
        if (report.Fatal || report.Recovery.Length == 0)
            return;
        _loop.Post(() =>
        {
            if (Current?.Kind == NoticeKind.ConfigError)
                return;
            if (IsQuiet())
            {
                _log.Info("shell", $"A crash notice was held back while Windows is quiet. See {report.Path}");
                return;
            }
            Show(ForCrash(report));
        });
    }

    // "C:\config\Bar.qml[7:5]: Cannot assign …", as Log.Describe writes an error, or "C:\config\shell.qml: …".
    [GeneratedRegex(@"^(?<file>.+?\.(?:qml|js|mjs|toml))(?:\[(?<line>\d+):(?<column>\d+)\])?: (?<message>.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ErrorAt();

    /// <summary>The notice for a configuration that failed to load: where the error is, what it is, and what runs meanwhile.</summary>
    public static Notice ForConfigError(string error, string rootFile, bool running)
    {
        var file = rootFile;
        var detail = Path.GetFileName(rootFile);
        var message = error;
        if (ErrorAt().Match(error) is { Success: true } m)
        {
            file = m.Groups["file"].Value;
            message = m.Groups["message"].Value;
            var dir = Path.GetDirectoryName(rootFile) ?? "";
            var shown = Path.GetFullPath(file).StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(dir, file)
                : file;
            detail = m.Groups["line"].Success
                ? string.Create(CultureInfo.InvariantCulture, $"{shown}, line {m.Groups["line"].Value}, column {m.Groups["column"].Value}")
                : shown;
        }
        var note = running
            ? "Winshell keeps running the last version that loaded."
            : "Nothing is running until it's fixed. Winshell loads it again when you save.";
        return new Notice(NoticeKind.ConfigError, "Your configuration has an error", detail, message, note, "Open file", file, TimeSpan.Zero);
    }

    /// <summary>The notice for a window that failed: what failed, what Winshell did, and the report.</summary>
    public static Notice ForCrash(CrashReport report)
    {
        var subject = report.Subject.Length > 0 ? report.Subject : "a window";
        var title = char.ToUpperInvariant(subject[0]) + subject[1..] + " stopped working";
        return new Notice(NoticeKind.Crash, title, "", report.Recovery, "A crash report was saved on this PC.", "Open report", report.Path, ReadCrashTimeout());
    }

    /// <summary>Shows a notice in place of the one showing.</summary>
    public void Show(Notice notice)
    {
        if (_disposed)
            return;
        Hide();
        var engine = new QmlEngine(_modules(), _loop, _log);
        engine.AddService(_platform);
        _screens = new ScreenRegistry(_platform);
        engine.AddService(_screens);
        try
        {
            var doc = engine.LoadDocumentFromText(ReadResource(), Path.Combine(AppContext.BaseDirectory, "ShellNotice.qml"));
            var root = (ShellWindow)engine.CreateFromDocument(doc, null, new Dictionary<string, object?>
            {
                ["title"] = notice.Title,
                ["detail"] = notice.Detail,
                ["body"] = notice.Body,
                ["note"] = notice.Note,
                ["actionText"] = notice.ActionText,
                ["timeout"] = (int)notice.Timeout.TotalMilliseconds,
            });
            root.Connect("actionRequested", _ => Act());
            root.Connect("dismissRequested", _ => _loop.Post(Hide));
            _engine = engine;
            Current = notice;
            Window = root;
            root.Reload(null);
            engine.FinishReload();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The notice is what reports problems; one it has itself goes to the log.
            _log.Error("notice", $"Could not show a notice: {Log.Describe(e, "ShellNotice.qml")}");
            Hide();
            engine.Dispose();
        }
    }

    private void Act()
    {
        if (Current is not { } notice)
            return;
        _open(notice.Kind == NoticeKind.ConfigError ? ConfigFileOrRoot(notice.Target, _host.RootFile) : notice.Target);
        _loop.Post(Hide);
    }

    /// <summary>
    /// The file a configuration error names, when it is a QML or script file that is there; otherwise the
    /// configuration's <c>shell.qml</c>. The name comes from the error's text, so it is checked before it is opened.
    /// </summary>
    internal static string ConfigFileOrRoot(string target, string rootFile)
    {
        var extension = Path.GetExtension(target);
        var configFile = extension.Equals(".qml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".toml", StringComparison.OrdinalIgnoreCase);
        return configFile && Path.IsPathFullyQualified(target) && File.Exists(target) ? target : rootFile;
    }

    /// <summary>Closes the notice showing, if any.</summary>
    public void Hide()
    {
        Current = null;
        // Made from a document rather than as an engine's root, so the engine does not destroy it.
        Window?.Destroy();
        Window = null;
        var engine = _engine;
        _engine = null;
        engine?.Dispose();
        _screens?.Destroy();
        _screens = null;
    }

    /// <summary>
    /// Opens a file to read it, off the loop so a slow app does not hold up the shell. A crash report and a
    /// <c>.qml</c> file open with what Windows opens them with; any other file opens with its edit verb, since opening
    /// a <c>.js</c> file runs it in Windows Script Host. A file no app opens that way is shown selected in File Explorer.
    /// </summary>
    public static void Open(string path, Log log) => _ = Task.Run(() => OpenNow(path, log));

    /// <summary>The verb a file is opened with: null for the default, which only a report or a <c>.qml</c> file gets.</summary>
    internal static string? VerbFor(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) || extension.Equals(".qml", StringComparison.OrdinalIgnoreCase)
            ? null
            : "edit";
    }

    private static void OpenNow(string path, Log log)
    {
        try
        {
            var open = new ProcessStartInfo(path) { UseShellExecute = true };
            if (VerbFor(path) is { } verb)
                open.Verb = verb;
            Process.Start(open)?.Dispose();
        }
        catch (Win32Exception)
        {
            try
            {
                // Explorer by its full path, not the search path. It reads its own command line: the path is quoted
                // after the comma, not the whole argument.
                var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(new ProcessStartInfo(explorer, $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                log.Warn("notice", $"Could not open {path}: {e.Message}");
            }
        }
        catch (InvalidOperationException e)
        {
            log.Warn("notice", $"Could not open {path}: {e.Message}");
        }
    }

    private static string ReadResource()
    {
        using var stream = typeof(ShellNotices).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"{Resource} is not in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _host.Reloaded -= OnReloaded;
        _host.FileError -= OnFileError;
        if (_host.Crashes is { } crashes)
            crashes.Written -= OnReport;
        Hide();
    }
}
