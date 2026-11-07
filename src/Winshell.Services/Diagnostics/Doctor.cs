using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Winshell.Native;
using Winshell.Notifications;
using Winshell.Session;

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
/// What <c>winshell doctor</c> was asked to check, and the folders it reads. <see cref="Doctor"/> decides which
/// project that is, and which folder the Folders check writes to, from <paramref name="Path"/> and
/// <paramref name="Config"/>.
/// </summary>
/// <param name="Locations">The config and local folders, <see cref="ShellLocations.Current"/> for <c>winshell doctor</c>.</param>
/// <param name="Path">The <c>-p</c> folder or project, as the user gave it. Null when they gave none.</param>
/// <param name="Config">The <c>-c</c> name, as the user gave it. Null when they gave none.</param>
internal sealed record DoctorContext(ShellLocations Locations, string? Path, string? Config);

/// <summary>
/// Checks this PC's setup for Winshell. Nothing is changed: no prompt, no registry or settings write, no instance
/// started or stopped, and no build. Telling whether a folder can be written makes and removes one empty file. The
/// user's own folders are written as <c>%LOCALAPPDATA%</c>, <c>%APPDATA%</c> and <c>%USERPROFILE%</c>, so what it
/// reports can go into a bug report without the Windows user name.
/// </summary>
public static class Doctor
{
    // Each check reads what it needs, mostly through DoctorProbe, and judges it in a function of its own, so the judging is tested without the PC.
    internal const string BugReports = "https://github.com/enpaze/winshell/issues/new/choose";

    internal static IReadOnlyList<DoctorCheck> Run(DoctorContext context)
    {
        return Report(context, ChooseUser(context.Locations.ConfigRoot, context.Path, context.Config));
    }

    /// <summary>
    /// The project one run checks, why there is none, where Folders writes, and the configuration sign-in is about.
    /// Sign-in is null when a shell is not a configuration program: the search of the config root is not a choice of one.
    /// </summary>
    private readonly record struct Chosen(
        ConfigurationProject? Project,
        string? Problem,
        string Folder,
        string? SignInFolder);

    /// <summary>
    /// The project <c>winshell doctor</c> checks for the <c>-p</c> and <c>-c</c> the user gave. Neither means the
    /// project <c>winshell run</c> would start. The Folders check writes to that project's folder, or, when there is
    /// none, to the <c>-p</c> path as given. Its parent could be a drive, the profile or another project.
    /// </summary>
    private static Chosen ChooseUser(string configRoot, string? path, string? config)
    {
        var found = Find(path, config, configRoot);
        var folder = found.Project?.Folder ?? AskedFolder(configRoot, path, config);
        return new Chosen(found.Project, found.Problem, folder, found.Project?.Folder);
    }

    /// <summary>
    /// The project a shell running in <paramref name="program"/> checks. A configuration program is checked in its own
    /// folder. Any other program is checked in <paramref name="configRoot"/>: the project search starts there, and the
    /// Folders check writes there even when the search finds a project. Sign-in is not about that project.
    /// </summary>
    private static Chosen ChooseProcess(string configRoot, string program)
    {
        try
        {
            if (ConfigurationProject.ForProgram(program) is { } own)
                return new Chosen(own, null, own.Folder, own.Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        var found = Find(null, null, configRoot);
        return new Chosen(found.Project, found.Problem, configRoot, null);
    }

    private readonly record struct Found(ConfigurationProject? Project, string? Problem);

    /// <summary>One read of the project the arguments name. A failure to read it is the doctor's own words.</summary>
    private static Found Find(string? path, string? config, string configRoot)
    {
        try
        {
            var project = ConfigurationProject.Find(path, config, configRoot, out var problem);
            return new Found(project, problem);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var what = path is not null ? path
                : config is not null ? $"config \"{config}\""
                : "the config folder";
            return new Found(null, $"Winshell cannot read {what}: {e.Message}");
        }
    }

    private static string AskedFolder(string configRoot, string? path, string? config)
    {
        if (path is not null)
            return Path.GetFullPath(path);
        if (config is not null)
            return Path.Combine(configRoot, config);
        return configRoot;
    }

    private static IReadOnlyList<DoctorCheck> Report(DoctorContext context, Chosen chosen)
    {
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        // The process running the doctor. The CLI is winshell.exe. A shell is its configuration program, and sign-in
        // compares the winshell.exe that started it.
        var process = Environment.ProcessPath ?? "";
        var configFolder = chosen.Folder;
        var configuration = DoctorProject.Configuration(chosen.Project, chosen.Problem).ToList();
        var blocked = configuration.Any(check => check.Id == "downloaded" && check.Status == CheckStatus.Fail);
        if (!blocked && configuration[0].Status == CheckStatus.Pass &&
            DoctorProbe.PreferencesProblem(context.Locations.ConfigRoot) is { } preferences)
            configuration[0] = WithPreferences(configuration[0], preferences);
        var instances = ListedInstances(context.Locations.Instances);
        DoctorCheck[] checks =
        [
            Guard("windows", "Windows",
                () => Windows(DoctorProbe.WindowsBuild(), DoctorProbe.WindowsUpdateRevision(),
                    DoctorProbe.WindowsDisplayVersion(), DoctorProbe.WindowsServer(),
                    DoctorProbe.TransparencyEffectsOff(), ContrastTheme.IsOn())),
            Guard("dotnet", ".NET",
                () => DotNet(Environment.Version, RuntimeInformation.FrameworkDescription,
                    DoctorProbe.SelfContained(process))),
            .. configuration,
            Guard("folders", "Folders", () => Folders(
                [
                    ("the config folder", configFolder, DoctorProbe.CanWrite(configFolder)),
                    ("its local folder", context.Locations.LocalRoot, DoctorProbe.CanWrite(context.Locations.LocalRoot))
                ],
                instances.Where(i => DoctorProbe.IsElevated(i.Pid)).Select(i => i.Id).ToList(),
                Environment.IsPrivilegedProcess)),
            Guard("appBars", "App bars",
                () => DoctorScreens.AppBars(DoctorProbe.Monitors(), DoctorProbe.EdgeWindows())),
            Guard("dpi", "DPI",
                () => DoctorScreens.Dpi(ThreadDpi.IsPerMonitorV2(), DoctorProbe.CompatibilityLayers(process),
                    DoctorProbe.Monitors())),
            Guard("signIn", "Sign-in", () =>
            {
                if (Packaging.IsPackaged)
                    return DoctorSignIn.SignIn(SignInEntry.OpenTask()?.State);
                // A sign-in command with no -p and no -c starts whichever configuration winshell run would, and that
                // is a different verdict from a configuration the user, or this process, picked.
                var project = chosen.SignInFolder;
                var supervisor = ConfigurationProject.FolderOf(process) is null
                    ? null
                    : WinshellHost.Supervising();
                return SignInForProcess(process, supervisor, SignInEntry.RegisteredCommand(),
                    SignInEntry.TurnedOffInWindows(), File.Exists, DoctorProbe.ResolveLinks, project,
                    project is null ? null : SignInEntry.BareFolder(context.Locations.ConfigRoot),
                    context.Locations.ConfigRoot);
            }),
            Guard("notifications", "Notifications", () => Notifications(NotificationListenerAccess.Read().ToString())),
            Guard("instances", "Running", () => Instances(instances)),
            Guard("crashes", "Crashes",
                () => DoctorCrashes.CrashesIn(CrashReports.List(context.Locations.CrashRoot), now))
        ];
        return WithoutUserFolders(checks,
        [
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%")
        ]);
    }

    /// <summary>
    /// Writes each of the user's folders as its variable in every message and fix, the longest folder first. A
    /// folder only matches whole: <c>C:\Users\ann</c> leaves <c>C:\Users\anna</c> alone.
    /// </summary>
    internal static IReadOnlyList<DoctorCheck> WithoutUserFolders(IReadOnlyList<DoctorCheck> checks,
        IReadOnlyList<(string Path, string Name)> folders)
    {
        var patterns = folders.Where(f => f.Path.Length > 0).OrderByDescending(f => f.Path.Length)
            .Select(f => (
                Pattern: new Regex(Regex.Escape(f.Path.TrimEnd('\\')) + @"(?![\w\-]|\.\w)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), f.Name))
            .ToList();

        string Replace(string text)
        {
            return patterns.Aggregate(text, (t, p) => p.Pattern.Replace(t, _ => p.Name));
        }

        return checks.Select(c => c with { Message = Replace(c.Message), Fix = c.Fix is null ? null : Replace(c.Fix) })
            .ToList();
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
            return new DoctorCheck(id, title, CheckStatus.Warn, $"Could not check: {e.GetType().Name}: {e.Message}",
                $"Report it at {BugReports}.");
        }
    }

    // Windows

    /// <param name="build">The Windows build number, such as 26100.</param>
    /// <param name="revision">The update revision, the number after the build's dot.</param>
    /// <param name="displayVersion">The version name, such as <c>24H2</c>; null or empty leaves it out.</param>
    /// <param name="server">Whether this is Windows Server.</param>
    /// <param name="transparencyOff">Whether Windows transparency effects are off. The message says so. The result does not change.</param>
    /// <param name="contrastTheme">Whether a contrast theme is on. The message says so. The result does not change.</param>
    internal static DoctorCheck Windows(int build, int revision, string? displayVersion, bool server = false,
        bool transparencyOff = false, bool contrastTheme = false)
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
            return new DoctorCheck("windows", "Windows", CheckStatus.Fail, message,
                "Winshell needs Windows 11. Upgrade this PC to Windows 11.");
        if (build < 22621)
            return new DoctorCheck("windows", "Windows", CheckStatus.Warn, message,
                "Update Windows in Settings > Windows Update: Mica, Acrylic and other parts Winshell uses need Windows 11 version 22H2 or later.");
        return new DoctorCheck("windows", "Windows", CheckStatus.Pass, message);
    }

    // .NET

    internal static DoctorCheck DotNet(Version version, string description, bool selfContained)
    {
        var how = selfContained ? "built into Winshell" : "installed on this PC";
        if (version.Major < 10)
            return new DoctorCheck("dotnet", ".NET", CheckStatus.Fail, $"{description}, {how}",
                "Winshell needs .NET 10. Install the .NET 10 runtime, or use a Winshell release, which brings its own.");
        return new DoctorCheck("dotnet", ".NET", CheckStatus.Pass, $"{description}, {how}");
    }

    /// <summary>
    /// A <c>preferences.toml</c> that does not parse is a warning on a project check that passed. A project that is not
    /// there or failed already says what to fix, and with no project nothing reads the file.
    /// </summary>
    private static DoctorCheck WithPreferences(DoctorCheck check, string problem)
    {
        return check with
        {
            Status = CheckStatus.Warn,
            Message = check.Message.EndsWith('.') ? $"{check.Message} {problem}" : $"{check.Message}. {problem}",
            Fix = "Correct that file and save. Winshell keeps the last values it could read until the file parses."
        };
    }

    /// <summary>
    /// The sign-in check for <paramref name="processPath"/>. A configuration program is compared as the
    /// <paramref name="supervisor"/> that started it, which is the <c>winshell.exe</c> the entry runs.
    /// </summary>
    internal static DoctorCheck SignInForProcess(string processPath, string? supervisor, string? command,
        bool turnedOff,
        Func<string, bool> exists, Func<string, string>? resolve = null, string? projectFolder = null,
        string? bareFolder = null, string? configRoot = null)
    {
        return DoctorSignIn.SignIn(command, turnedOff, SignInEntry.InstallExe(processPath, supervisor), exists, resolve,
            projectFolder, bareFolder, configRoot);
    }

    /// <summary>
    /// Every check, for the shell running in this process. A configuration project's program is checked in its own
    /// folder. Any other program is checked in <paramref name="configRoot"/>. Nothing is built.
    /// </summary>
    public static IReadOnlyList<DoctorCheck> ForThisProcess(string configRoot)
    {
        return Report(
            new DoctorContext(ShellLocations.Current with { ConfigRoot = configRoot }, null, null),
            ChooseProcess(configRoot, Environment.ProcessPath ?? ""));
    }

    /// <summary>The running instances, or none when their folder cannot be read. The doctor still runs its checks.</summary>
    private static List<DoctorInstance> ListedInstances(Instances instances)
    {
        try
        {
            return instances.List(false)
                .Select(i => new DoctorInstance(i.Info.Id, i.Info.Pid, i.Info.ConfigPath, i.Info.Log)).ToList();
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
    internal static DoctorCheck Folders(IReadOnlyList<(string Name, string Path, bool Writable)> folders,
        IReadOnlyList<string> elevatedInstances, bool asAdministrator = false)
    {
        var who = asAdministrator
            ? " This ran as administrator, so it says what an administrator can write. Run it without Run as administrator to check your account."
            : "";
        var locked = folders.Where(f => !f.Writable).ToList();
        if (locked.Count > 0)
        {
            var list = string.Join("; ", locked.Select(f => $"{f.Name}, {f.Path}"));
            return new DoctorCheck("folders", "Folders", CheckStatus.Fail, $"Winshell cannot write {list}.{who}",
                "Give your account write access to it in the folder's Properties > Security, or move it with WINSHELL_CONFIG_DIR.");
        }

        if (elevatedInstances.Count > 0)
            return new DoctorCheck("folders", "Folders", CheckStatus.Warn,
                $"Instance {string.Join(", ", elevatedInstances)} runs as administrator. Apps it starts run as administrator too, and other apps cannot drag onto it.{who}",
                "Close it and start Winshell without Run as administrator.");
        return new DoctorCheck("folders", "Folders", CheckStatus.Pass,
            $"Winshell can write {string.Join(" and ", folders.Select(f => f.Name))}{(who.Length > 0 ? "." + who : "")}");
    }

    // Notifications

    internal static DoctorCheck Notifications(string access)
    {
        return access switch
        {
            "Allowed" => new DoctorCheck("notifications", "Notifications", CheckStatus.Pass,
                "Windows lets Winshell read notifications"),
            "Denied" => new DoctorCheck("notifications", "Notifications", CheckStatus.Warn,
                "Windows does not let Winshell read notifications, so a shell sees none.",
                "In Settings > Privacy & security > Notifications, let apps access your notifications."),
            "Unspecified" => new DoctorCheck("notifications", "Notifications", CheckStatus.Warn,
                "Winshell has not asked to read notifications yet, so a shell sees none.",
                "Have the shell call Shell.Notifications.RequestAccess from a button."),
            _ => new DoctorCheck("notifications", "Notifications", CheckStatus.Warn,
                "Windows does not let this Winshell read notifications: reading them needs an installed package with that permission. A shell sees none.",
                "Install Winshell from its package.")
        };
    }

    // Running instances

    internal static DoctorCheck Instances(IReadOnlyList<DoctorInstance> instances)
    {
        if (instances.Count == 0)
            return new DoctorCheck("instances", "Running", CheckStatus.Pass, "No Winshell is running");
        var list = string.Join("; ",
            instances.Select(i => string.Create(CultureInfo.InvariantCulture,
                $"{i.Id} (process {i.Pid}) runs {i.ConfigPath}, log {i.LogPath}")));
        return new DoctorCheck("instances", "Running", CheckStatus.Pass, list);
    }

    /// <summary>The status as <c>winshell doctor</c> writes it: <c>pass</c>, <c>warn</c> or <c>fail</c>.</summary>
    public static string StatusName(CheckStatus status)
    {
        return status switch
        {
            CheckStatus.Pass => "pass",
            CheckStatus.Warn => "warn",
            _ => "fail"
        };
    }
}
