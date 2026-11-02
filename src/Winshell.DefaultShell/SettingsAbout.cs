using System.Globalization;
using Winshell.Platform.Windows.Diagnostics;
using Winshell.Updates;

namespace Winshell.DefaultShell;

/// <summary>One check <c>winshell doctor</c> would list. <see cref="Status"/> is <c>pass</c>, <c>warn</c> or <c>fail</c>.</summary>
public sealed record SettingsDiagnostic(string Id, string Status, string Title, string Message, string Fix);

/// <summary>What the About page says. The page fills these from the shell; the text does not ask Windows itself.</summary>
public static class SettingsAbout
{
    public const string NoUpdates = "This build of Winshell does not update itself";
    public const string WingetUpdates = "Updated by winget";
    public const string PackagedUpdates = "Updates come from where you installed Winshell, such as the Microsoft Store";
    public const string NoFolder = "This shell has no config folder";
    public const string NoLog = "This instance writes no log";
    public const string DiagnosticsUnavailable = "Only a shell started by winshell.exe can check its setup";
    public const string DiagnosticsRunning = "Checking…";

    /// <summary>The same address <c>winshell doctor</c> prints when a check cannot run.</summary>
    public const string BugReports = "https://github.com/dante-sparras/winshell/issues/new/choose";

    public static string CopyText(string version) => "Winshell " + version;

    public static string Updates(bool available, bool winget, bool packaged, UpdateStatus status, string version, string error)
    {
        if (!available)
            return winget ? WingetUpdates : packaged ? PackagedUpdates : NoUpdates;
        return status switch
        {
            UpdateStatus.Checking => "Checking for updates…",
            UpdateStatus.UpToDate => "You're up to date",
            UpdateStatus.Downloading => "Downloading Winshell " + version + "…",
            UpdateStatus.Ready => error.Length > 0 ? error : "Winshell " + version + " is ready. Restart Winshell to finish updating",
            UpdateStatus.Failed => error,
            _ => "Get the newest Winshell from GitHub",
        };
    }

    public static bool UpdatesBusy(UpdateStatus status) => status is UpdateStatus.Checking or UpdateStatus.Downloading;

    public static string UpdateButton(UpdateStatus status) => status == UpdateStatus.Ready ? "Restart now" : "Check for updates";

    public static string UpdateButtonName(UpdateStatus status) =>
        status == UpdateStatus.Ready ? "Restart Winshell to update" : "Check for Winshell updates";

    public static bool AutomaticEnabled(bool available) => available;

    public static bool AutomaticOn(bool available, bool autoUpdate) => available && autoUpdate;

    public static string Folder(string path) => path.Length > 0 ? path : NoFolder;

    public static string Log(string path) => path.Length > 0 ? path : NoLog;

    public static string Diagnostics(bool available, bool running, string summary, string idle) =>
        !available ? DiagnosticsUnavailable : running ? DiagnosticsRunning : summary.Length > 0 ? summary : idle;

    public static bool DiagnosticsEnabled(bool available, bool running) => available && !running;

    public static string Summary(IReadOnlyList<SettingsDiagnostic> checks)
    {
        var pass = 0;
        var warns = 0;
        foreach (var check in checks)
        {
            if (check.Status == "pass")
                pass++;
            else if (check.Status == "warn")
                warns++;
        }
        var fails = checks.Count - pass - warns;
        return string.Create(CultureInfo.InvariantCulture,
            $"{pass} passed, {warns} {(warns == 1 ? "warning" : "warnings")}, {fails} failed.");
    }

    /// <summary>
    /// The configuration project in <paramref name="configRoot"/>, as the About page lists it. The project file is
    /// read as data. Nothing is built.
    /// </summary>
    public static IReadOnlyList<SettingsDiagnostic> Checks(string configRoot) =>
        [.. Doctor.Configuration(configRoot).Select(check => new SettingsDiagnostic(
            check.Id, Doctor.StatusName(check.Status), check.Title, check.Message, check.Fix ?? ""))];

    /// <summary>What Narrator reads for one check: the result, what it found, and the fix when there is one.</summary>
    public static string Spoken(SettingsDiagnostic check)
    {
        var word = check.Status == "pass" ? "Passed" : check.Status == "warn" ? "Warning" : "Failed";
        var fix = check.Fix.Length > 0 ? " Fix: " + check.Fix : "";
        return $"{word}: {check.Title}. {check.Message}{fix}";
    }

    public static SettingsDiagnostic Failed(Exception error) =>
        new("doctor", "fail", "Diagnostics", $"Could not run: {error.GetType().Name}: {error.Message}", $"Report it at {BugReports}.");
}

/// <summary>
/// A diagnostics run for the About page. Without a probe there is nothing to run. The probe runs on its own thread,
/// and the result is posted back, because a check can be slow and a pooled continuation has been dropped before.
/// </summary>
public sealed class SettingsDiagnosticsSession
{
    private readonly Func<IReadOnlyList<SettingsDiagnostic>>? _probe;
    private bool _running;

    public SettingsDiagnosticsSession(Func<IReadOnlyList<SettingsDiagnostic>>? probe) => _probe = probe;

    public bool Available => _probe is not null;

    public bool Running => _running;

    public string Summary { get; private set; } = "";

    public IReadOnlyList<SettingsDiagnostic> Checks { get; private set; } = [];

    public event EventHandler? Updated;

    public void Run(Action<Action> post)
    {
        if (_probe is not { } probe || _running)
            return;
        _running = true;
        Updated?.Invoke(this, EventArgs.Empty);
        var thread = new Thread(() =>
        {
            IReadOnlyList<SettingsDiagnostic> checks;
            try
            {
                checks = probe();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                checks = [SettingsAbout.Failed(e)];
            }
            post(() => Apply(checks));
        })
        {
            IsBackground = true,
            Name = "Diagnostics",
        };
        thread.Start();
    }

    public void Apply(IReadOnlyList<SettingsDiagnostic> checks)
    {
        Checks = checks;
        Summary = SettingsAbout.Summary(checks);
        _running = false;
        Updated?.Invoke(this, EventArgs.Empty);
    }
}
