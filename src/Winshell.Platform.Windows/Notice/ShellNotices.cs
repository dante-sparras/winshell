using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
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
/// including nothing. One notice shows at a time; a new one replaces it. Nothing is sent anywhere: the button opens a
/// file on this PC. <c>WINSHELL_NO_RELOAD_POPUP=1</c> and <c>Shell.inhibitReloadPopup()</c> hide configuration errors.
/// </summary>
public sealed partial class ShellNotices : IDisposable
{
    /// <summary>How long a crash notice stays, as a long Windows notification does.</summary>
    public static readonly TimeSpan CrashTimeout = TimeSpan.FromSeconds(25);

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
        if (host.Crashes is { } crashes)
            crashes.Written += OnReport;
    }

    /// <summary>The notice showing now, or null.</summary>
    public Notice? Current { get; private set; }

    /// <summary>The notice's window while one shows.</summary>
    public ShellWindow? Window { get; private set; }

    private void OnReloaded(string? error)
    {
        if (error is null)
        {
            if (Current?.Kind == NoticeKind.ConfigError)
                Hide();
            return;
        }
        if (_host.ShowReloadPopup)
        {
            var notice = ForConfigError(error, _host.RootFile, running: _host.Root is not null);
            _loop.Post(() => Show(notice));
        }
    }

    // A report can be written on any thread; a process that is ending has no time to show one.
    private void OnReport(CrashReport report)
    {
        if (report.Fatal || report.Recovery.Length == 0)
            return;
        _loop.Post(() => Show(ForCrash(report)));
    }

    // "C:\config\Bar.qml[7:5]: Cannot assign …", as Log.Describe writes an error, or "C:\config\shell.qml: …".
    [GeneratedRegex(@"^(?<file>.+?\.(?:qml|js|mjs))(?:\[(?<line>\d+):(?<column>\d+)\])?: (?<message>.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
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
        return new Notice(NoticeKind.Crash, title, "", report.Recovery, "A crash report was saved on this PC.", "Open report", report.Path, CrashTimeout);
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
        _open(notice.Target);
        _loop.Post(Hide);
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
    /// Opens a file in the app Windows opens it with. A file no app is set to open, as a <c>.qml</c> file can be, is
    /// shown selected in File Explorer instead.
    /// </summary>
    public static void Open(string path, Log log)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            try
            {
                // Explorer reads its own command line: the path is quoted after the comma, not the whole argument.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
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
        if (_host.Crashes is { } crashes)
            crashes.Written -= OnReport;
        Hide();
    }
}
