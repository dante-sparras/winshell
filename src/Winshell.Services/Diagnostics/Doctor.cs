using System.Globalization;
using System.Text.RegularExpressions;
using Winshell.Updates;

namespace Winshell.Diagnostics;

/// <summary>One thing <c>winshell doctor</c> checked: what it found and, when something is wrong, how to fix it.</summary>
/// <param name="Id">A stable name for the check, such as <c>dotnet</c>.</param>
/// <param name="Title">The check's name as <c>winshell doctor</c> prints it.</param>
/// <param name="Status">How the check came out.</param>
/// <param name="Message">What the check found.</param>
/// <param name="Fix">How to fix what is wrong, or null when nothing is or there is nothing to do.</param>
public sealed record DoctorCheck(string Id, string Title, CheckStatus Status, string Message, string? Fix = null);

/// <summary>A running Winshell, as the instance list has it.</summary>
internal sealed record DoctorInstance(string Id, int Pid, string ConfigPath, string LogPath);

/// <summary>
/// What the doctor needs from <c>winshell.exe</c>: this executable, the config that would run (or why none would),
/// the running instances and Winshell's folders.
/// </summary>
/// <param name="ExePath">The path of the running <c>winshell.exe</c>.</param>
/// <param name="ConfigRoot">The config root: <c>%APPDATA%\winshell</c>, or <c>WINSHELL_CONFIG_DIR</c> if set.</param>
/// <param name="ConfigFile">The project file that runs, or null when Winshell would run its default shell, or cannot pick one.</param>
/// <param name="ConfigProblem">Why no config can be picked, such as several configs and no <c>-c</c>; null when one can.</param>
/// <param name="Instances">The running Winshell instances.</param>
/// <param name="LocalRoot">The local root: <c>%LOCALAPPDATA%\winshell</c>, or <c>WINSHELL_LOCAL_DIR</c> if set.</param>
/// <param name="CrashRoot">The folder of crash reports, under <paramref name="LocalRoot"/>.</param>
/// <param name="ProjectRoot">
/// The folder the Folders check writes to, and the one searched for a project when the CLI picked none and gave no
/// problem. Null is <paramref name="ConfigRoot"/>.
/// </param>
internal sealed record DoctorContext(
    string ExePath,
    string ConfigRoot,
    string? ConfigFile,
    string? ConfigProblem,
    IReadOnlyList<DoctorInstance> Instances,
    string LocalRoot,
    string CrashRoot,
    string? ProjectRoot = null);

/// <summary>
/// Checks this PC's setup for Winshell. Nothing is changed: no prompt, no registry or settings write, no instance
/// started or stopped, and no build. Telling whether a folder can be written makes and removes one empty file. The
/// user's own folders are written as <c>%LOCALAPPDATA%</c>, <c>%APPDATA%</c> and <c>%USERPROFILE%</c>, so what it
/// reports can go into a bug report without the Windows user name.
/// </summary>
public static class Doctor
{
    // Each check reads what it needs through IDoctorFacts and judges it in code of its own, so the judging is tested without the PC.
    internal const string BugReports = "https://github.com/enpaze/winshell/issues/new/choose";

    internal static IReadOnlyList<DoctorCheck> Run(DoctorContext context, IDoctorFacts facts)
    {
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var search = context.ProjectRoot
            ?? (context.ConfigFile is { } file && file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(file) ?? context.ConfigRoot
                : context.ConfigRoot);
        var configFolder = search;
        // The CLI has picked the project already, or said why it could not, and that is what is checked. Searching a
        // folder by the config folder's rules would find its default or a lone subfolder instead.
        List<DoctorCheck> configuration;
        if (context.ConfigProblem is { } problem)
        {
            configuration = [.. DoctorProject.Configuration(null, problem)];
        }
        else if (context.ConfigFile is { } picked && picked.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            configuration = [.. DoctorProject.Picked(picked, context.ConfigRoot)];
        }
        else
        {
            configuration = [.. DoctorProject.Configuration(search)];
        }
        var blocked = configuration.Any(check => check.Id == "downloaded" && check.Status == CheckStatus.Fail);
        if (!blocked && configuration[0].Status != CheckStatus.Fail)
            configuration[0] = WithUserFiles(configuration[0], facts.UserConfigProblems(context.ConfigFile, context.ConfigRoot, context.LocalRoot));
        DoctorCheck[] checks =
        [
            Guard("windows", "Windows", () => Windows(facts.WindowsBuild(), facts.WindowsUpdateRevision(), facts.WindowsDisplayVersion(), facts.WindowsServer(), facts.TransparencyEffectsOff(), facts.ContrastThemeOn())),
            Guard("dotnet", ".NET", () => DotNet(Environment.Version, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, facts.SelfContained(context.ExePath))),
            .. configuration,
            Guard("folders", "Folders", () => Folders(
                [("the config folder", configFolder, facts.CanWrite(configFolder)), ("its local folder", context.LocalRoot, facts.CanWrite(context.LocalRoot))],
                context.Instances.Where(i => facts.IsElevated(i.Pid)).Select(i => i.Id).ToList(),
                Environment.IsPrivilegedProcess)),
            Guard("appBars", "App bars", () => DoctorScreens.AppBars(facts.Monitors(), facts.EdgeWindows())),
            Guard("dpi", "DPI", () => DoctorScreens.Dpi(facts.PerMonitorV2(), facts.CompatibilityLayers(context.ExePath), facts.Monitors())),
            Guard("signIn", "Sign-in", () => Packaging.IsPackaged
                ? DoctorSignIn.SignIn(DoctorProbe.SignInTaskState())
                : DoctorSignIn.SignIn(facts.RunEntry(), facts.RunEntryTurnedOff(), context.ExePath, File.Exists, facts.ResolveLinks)),
            Guard("notifications", "Notifications", () => Notifications(facts.NotificationAccess())),
            Guard("instances", "Running", () => Instances(context.Instances)),
            Guard("crashes", "Crashes", () => DoctorCrashes.CrashesIn(facts.ReadCrashReports(context.CrashRoot), now, facts.PeekCrash)),
        ];
        return WithoutUserFolders(checks,
        [
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
        ]);
    }

    /// <summary>
    /// Writes each of the user's folders as its variable in every message and fix, the longest folder first. A
    /// folder only matches whole: <c>C:\Users\ann</c> leaves <c>C:\Users\anna</c> alone.
    /// </summary>
    internal static IReadOnlyList<DoctorCheck> WithoutUserFolders(IReadOnlyList<DoctorCheck> checks, IReadOnlyList<(string Path, string Name)> folders)
    {
        var patterns = folders.Where(f => f.Path.Length > 0).OrderByDescending(f => f.Path.Length)
            .Select(f => (Pattern: new Regex(Regex.Escape(f.Path.TrimEnd('\\')) + @"(?![\w\-]|\.\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), f.Name))
            .ToList();
        string Replace(string text) => patterns.Aggregate(text, (t, p) => p.Pattern.Replace(t, _ => p.Name));
        return checks.Select(c => c with { Message = Replace(c.Message), Fix = c.Fix is null ? null : Replace(c.Fix) }).ToList();
    }

    /// <summary>A check that could not run says so, rather than ending the whole run.</summary>
    internal static DoctorCheck Guard(string id, string title, Func<DoctorCheck> check)
    {
        try
        {
            return check();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new DoctorCheck(id, title, CheckStatus.Warn, $"Could not check: {e.GetType().Name}: {e.Message}", $"Report it at {BugReports}.");
        }
    }

    // Windows

    /// <param name="build">The Windows build number, such as 26100.</param>
    /// <param name="revision">The update revision, the number after the build's dot.</param>
    /// <param name="displayVersion">The version name, such as <c>24H2</c>; null or empty leaves it out.</param>
    /// <param name="server">Whether this is Windows Server.</param>
    /// <param name="transparencyOff">Whether Windows transparency effects are off. The message says so. The result does not change.</param>
    /// <param name="contrastTheme">Whether a contrast theme is on. The message says so. The result does not change.</param>
    internal static DoctorCheck Windows(int build, int revision, string? displayVersion, bool server = false, bool transparencyOff = false, bool contrastTheme = false)
    {
        var name = server ? "Windows Server" : build >= 22000 ? "Windows 11" : "Windows 10";
        var version = string.IsNullOrEmpty(displayVersion) ? "" : " " + displayVersion;
        var message = string.Create(CultureInfo.InvariantCulture, $"{name}{version}, build {build}.{revision}");
        if (transparencyOff)
            message += ". Transparency effects are off, so Mica and Acrylic look flat";
        if (contrastTheme)
            message += ". A contrast theme is on";
        if (transparencyOff || contrastTheme)
            message += ".";
        if (build < 22000)
            return new("windows", "Windows", CheckStatus.Fail, message, "Winshell needs Windows 11. Upgrade this PC to Windows 11.");
        if (build < 22621)
            return new("windows", "Windows", CheckStatus.Warn, message, "Update Windows in Settings > Windows Update: Mica, Acrylic and other parts Winshell uses need Windows 11 version 22H2 or later.");
        return new("windows", "Windows", CheckStatus.Pass, message);
    }

    // .NET

    internal static DoctorCheck DotNet(Version version, string description, bool selfContained)
    {
        var how = selfContained ? "built into Winshell" : "installed on this PC";
        if (version.Major < 10)
            return new("dotnet", ".NET", CheckStatus.Fail, $"{description}, {how}", "Winshell needs .NET 10. Install the .NET 10 runtime, or use a Winshell release, which brings its own.");
        return new("dotnet", ".NET", CheckStatus.Pass, $"{description}, {how}");
    }

    /// <summary>User TOML that does not parse is a warning on the project check. A failure already says what to fix.</summary>
    private static DoctorCheck WithUserFiles(DoctorCheck check, IReadOnlyList<string>? userFiles)
    {
        if (userFiles is not { Count: > 0 })
            return check;
        var note = string.Join(" ", userFiles);
        var message = check.Message.EndsWith('.') ? $"{check.Message} {note}" : $"{check.Message}. {note}";
        if (check.Status != CheckStatus.Pass)
            return check with { Message = message };
        return check with { Status = CheckStatus.Warn, Message = message, Fix = "Correct that file and save. Winshell keeps the last values it could read until the file parses." };
    }

    /// <summary>
    /// Every check, for the shell running in this process. A configuration project's program is checked in its own
    /// folder. The default shell is checked in <paramref name="configRoot"/>. Nothing is built.
    /// </summary>
    public static IReadOnlyList<DoctorCheck> ForThisProcess(string configRoot)
    {
        var exe = Environment.ProcessPath ?? "";
        ConfigurationProject? project = null;
        try
        {
            project = ConfigurationProject.ForProgram(exe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return Run(new DoctorContext(
            exe,
            configRoot,
            project?.ProjectFile,
            null,
            ListedInstances(),
            ShellLocations.LocalRoot,
            ShellLocations.CrashRoot,
            project?.Folder), DoctorFacts.Pc);
    }

    private static List<DoctorInstance> ListedInstances()
    {
        try
        {
            return global::Winshell.Instances.List(false).Select(i => new DoctorInstance(i.Info.Id, i.Info.Pid, i.Info.ConfigPath, i.Info.Log)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Folders

    /// <param name="folders">Each folder to write: its name in the message, its path, whether it is writable.</param>
    /// <param name="elevatedInstances">The ids of the running instances that run as administrator.</param>
    /// <param name="asAdministrator">Whether the doctor itself runs as administrator, and so says what an administrator can write.</param>
    internal static DoctorCheck Folders(IReadOnlyList<(string Name, string Path, bool Writable)> folders, IReadOnlyList<string> elevatedInstances, bool asAdministrator = false)
    {
        var who = asAdministrator ? " This ran as administrator, so it says what an administrator can write. Run it without Run as administrator to check your account." : "";
        var locked = folders.Where(f => !f.Writable).ToList();
        if (locked.Count > 0)
        {
            var list = string.Join("; ", locked.Select(f => $"{f.Name}, {f.Path}"));
            return new("folders", "Folders", CheckStatus.Fail, $"Winshell cannot write {list}.{who}", "Give your account write access to it in the folder's Properties > Security, or move it with WINSHELL_CONFIG_DIR.");
        }
        if (elevatedInstances.Count > 0)
            return new("folders", "Folders", CheckStatus.Warn, $"Instance {string.Join(", ", elevatedInstances)} runs as administrator. Apps it starts run as administrator too, and other apps cannot drag onto it.{who}", "Close it and start Winshell without Run as administrator.");
        return new("folders", "Folders", CheckStatus.Pass, $"Winshell can write {string.Join(" and ", folders.Select(f => f.Name))}{(who.Length > 0 ? "." + who : "")}");
    }

    // Notifications

    internal static DoctorCheck Notifications(string access) => access switch
    {
        "Allowed" => new("notifications", "Notifications", CheckStatus.Pass, "Windows lets Winshell read notifications"),
        "Denied" => new("notifications", "Notifications", CheckStatus.Warn, "Windows does not let Winshell read notifications, so the notification center stays empty.", "In Settings > Privacy & security > Notifications, let apps access your notifications."),
        "Unspecified" => new("notifications", "Notifications", CheckStatus.Warn, "Winshell has not asked to read notifications yet, so the notification center stays empty.", "Choose Allow notification access in Winshell Settings > Notifications."),
        _ => new("notifications", "Notifications", CheckStatus.Warn, "Windows does not let this Winshell read notifications: reading them needs an installed package with that permission. The notification center stays empty.", "Install Winshell from its package, or use Windows' notification center."),
    };

    // Running instances

    internal static DoctorCheck Instances(IReadOnlyList<DoctorInstance> instances)
    {
        if (instances.Count == 0)
            return new("instances", "Running", CheckStatus.Pass, "No Winshell is running");
        var list = string.Join("; ", instances.Select(i => string.Create(CultureInfo.InvariantCulture, $"{i.Id} (process {i.Pid}) runs {i.ConfigPath}, log {i.LogPath}")));
        return new("instances", "Running", CheckStatus.Pass, list);
    }

    /// <summary>The status as <c>winshell doctor</c> writes it: <c>pass</c>, <c>warn</c> or <c>fail</c>.</summary>
    public static string StatusName(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "pass",
        CheckStatus.Warn => "warn",
        _ => "fail",
    };
}
