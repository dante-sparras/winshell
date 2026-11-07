using System.Text;
using Windows.ApplicationModel;
using Microsoft.Win32;
using Winshell.Native;

namespace Winshell.Session;

/// <summary>
/// The sign-in entry as Windows has it, read without changing anything: an unpackaged Winshell's value under the
/// user's <c>Run</c> key, the on and off Startup apps keeps for it, and a packaged Winshell's startup task.
/// <see cref="WindowsStartupRegistry"/> and <see cref="WindowsStartupTasks"/> build on these reads, and the doctor
/// calls them as they are. A registry read can throw; the caller decides what that means.
/// </summary>
internal static class SignInEntry
{
    /// <summary>The name of the <c>Run</c> value, and the package's startup task id.</summary>
    public const string Name = "Winshell";

    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where the Startup apps page keeps its on and off, one binary value per Run value.</summary>
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ConsoleHost = "conhost.exe";

    private const string Headless = "--headless";

    /// <summary>The command the <c>Run</c> value starts, or null when there is none.</summary>
    public static string? RegisteredCommand(string runKey = RunKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey);
        return key?.GetValue(Name) as string;
    }

    /// <summary>Whether the Startup apps page turned the <c>Run</c> value off.</summary>
    public static bool TurnedOffInWindows(string approvedKey = ApprovedKey)
    {
        // The page writes 12 bytes: an odd first byte is off, with the time it was turned off after it.
        using var key = Registry.CurrentUser.OpenSubKey(approvedKey);
        return key?.GetValue(Name) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }

    /// <summary>
    /// The package's startup task, or null when the package has none or Windows does not hand it over. Only a
    /// packaged process calls this, and that call has no test: it would need an installed package.
    /// </summary>
    public static StartupTask? OpenTask()
    {
        try
        {
            // Sync because StartupService reads the task as it is made, and a UI binds its properties.
#pragma warning disable VSTHRD002 // Task.Run leaves the caller, so its context is not what it waits for.
            return Task.Run(static async () => await StartupTask.GetAsync(Name).AsTask().ConfigureAwait(false))
                .GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The program a sign-in command starts, past <c>conhost.exe --headless</c>, and the configuration it names.</summary>
    internal readonly record struct SignInLaunch(string? Program, string? Path, string? Name)
    {
        /// <summary>Whether the command names a configuration with <c>-p</c> or <c>-c</c>.</summary>
        public bool NamesConfiguration => Path is not null || Name is not null;
    }

    /// <summary>The file a path names in the end, past symbolic links, or the path itself when it is no link or cannot be read.</summary>
    internal static string Resolve(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, true)?.FullName ?? path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>What <paramref name="command"/> starts. A blank command starts nothing.</summary>
    internal static SignInLaunch Read(string command)
    {
        var args = CommandLine.Split(command);
        if (args.Length == 0)
            return default;
        int start;
        string program;
        if (args.Length >= 3 &&
            Path.GetFileName(args[0]).Equals(ConsoleHost, StringComparison.OrdinalIgnoreCase) &&
            args[1] == Headless)
        {
            program = args[2];
            start = 3;
        }
        else
        {
            program = args[0];
            start = 1;
        }

        string? path = null;
        string? name = null;
        for (var i = start; i < args.Length; i++)
        {
            if (args[i] == CommandWords.End)
                break;
            if (CommandWords.Path.Is(args[i]) && i + 1 < args.Length)
                path = args[++i];
            else if (CommandWords.Config.Is(args[i]) && i + 1 < args.Length)
                name = args[++i];
        }

        return new SignInLaunch(program, path, name);
    }

    /// <summary>
    /// The command a sign-in runs: <paramref name="exe"/> with <paramref name="args"/>, under a console host that has no
    /// window. <c>winshell.exe</c> is a console program, so started on its own it would open a console or Windows
    /// Terminal at every sign-in. Start launches <c>winshellw.exe</c>; that name is written as the <c>winshell.exe</c>
    /// beside it, so the Start menu and the installer describe one entry. <c>--no-duplicate</c> keeps a second sign-in entry from
    /// starting a second shell, and <c>--daemonize</c> is left out because the console host already runs it in the
    /// background. <c>--no-build</c> runs the configuration's last build, so signing in never depends on the SDK or on a
    /// broken edit. A <c>--path</c> is made absolute, since a sign-in does not start in this working directory. The
    /// <c>--</c> and the words after it are the program's, and stay last as they are.
    /// </summary>
    internal static string CommandFor(string exe, IEnumerable<string> args)
    {
        var all = args.ToList();
        var end = all.IndexOf(CommandWords.End);
        var words = end < 0 ? all : all.GetRange(0, end);
        var kept = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            if (CommandWords.Daemonize.Is(words[i]))
                continue;
            kept.Add(words[i]);
            if (i + 1 < words.Count && CommandWords.Path.Is(words[i]))
                kept.Add(Path.GetFullPath(words[++i]));
            else if (i + 1 < words.Count && CommandWords.Config.Is(words[i]))
                kept.Add(words[++i]);
        }

        if (!kept.Any(CommandWords.NoBuild.Is))
        {
            var duplicate = kept.FindIndex(CommandWords.NoDuplicate.Is);
            kept.Insert(duplicate >= 0 ? duplicate : kept.Count, CommandWords.NoBuild.Long);
        }

        if (!kept.Any(CommandWords.NoDuplicate.Is))
            kept.Add(CommandWords.NoDuplicate.Long);
        if (end >= 0)
            kept.AddRange(all.Skip(end));
        var conhost = Path.Combine(Environment.SystemDirectory, ConsoleHost);
        return string.Join(' ', new[] { conhost, Headless, WinshellHost.ConsoleOf(exe) }.Concat(kept).Select(Quote));
    }

    /// <summary>Quotes an argument the way <c>CommandLineToArgvW</c> reads it back.</summary>
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => c is ' ' or '\t' or '"'))
            return arg;
        var sb = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            sb.Append(c);
        }

        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }

    /// <summary>The program <see cref="Read"/> found, or null when the command is blank.</summary>
    internal static string? ProgramOf(string command)
    {
        return Read(command).Program;
    }

    /// <summary>
    /// Whether <paramref name="program"/> and <paramref name="thisExe"/> are one install, once <paramref name="resolve"/>
    /// has followed links. <c>winshell.exe</c> and <c>winshellw.exe</c> in the same folder are one install.
    /// </summary>
    internal static bool SameInstall(string program, string thisExe, Func<string, string> resolve)
    {
        // A Run value can name a program that is not a path. That is not this install, and it must not throw.
        if (Full(program) is not { } programPath || Full(thisExe) is not { } herePath)
            return false;
        string started;
        string here;
        try
        {
            started = resolve(programPath);
            here = resolve(herePath);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException
                                      or UnauthorizedAccessException)
        {
            return false;
        }

        if (string.Equals(started, here, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!WinshellHost.Is(started) || !WinshellHost.Is(here))
            return false;
        if (Full(started) is not { } startedFull || Full(here) is not { } hereFull)
            return false;
        return string.Equals(Path.GetDirectoryName(startedFull), Path.GetDirectoryName(hereFull),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The <c>winshell.exe</c> a sign-in entry is compared with. A configuration program is not that file: the host
    /// that started it is. With no such host, the process path stands.
    /// </summary>
    internal static string InstallExe(string processPath, string? supervisor)
    {
        return ConfigurationProject.FolderOf(processPath) is not null && supervisor is not null
            ? supervisor
            : processPath;
    }

    /// <summary>
    /// Whether <paramref name="launch"/> starts <paramref name="projectFolder"/>. A command that names no configuration
    /// starts the folder <paramref name="bareFolder"/>, which is what <c>winshell run</c> with no <c>-p</c> or <c>-c</c>
    /// would start. <paramref name="configRoot"/> is the folder a <c>-c</c> name is inside.
    /// </summary>
    internal static bool RunsThis(SignInLaunch launch, string projectFolder, string? bareFolder, string? configRoot)
    {
        if (launch.Path is { } path)
        {
            if (SamePath(path, projectFolder))
                return true;
            var full = Full(path);
            return full is not null
                   && full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                   && SamePath(Path.GetDirectoryName(full), projectFolder);
        }

        if (launch.Name is { } name)
            return CommandWords.IsConfigName(name) &&
                   SamePath(Path.Combine(configRoot ?? ConfigPaths.Root(), name), projectFolder);
        return bareFolder is not null && SamePath(bareFolder, projectFolder);
    }

    /// <summary>
    /// Whether <paramref name="actual"/> starts the same install and configuration as <paramref name="expected"/>.
    /// <paramref name="expected"/> names a configuration. <paramref name="bareFolder"/> is what a command with no
    /// <c>-p</c> or <c>-c</c> starts, and <paramref name="configRoot"/> is where a <c>-c</c> name lives.
    /// </summary>
    internal static bool IsOurs(string actual, string expected, string? bareFolder, string? configRoot)
    {
        var wanted = Read(expected);
        var project = ProjectOf(wanted, configRoot);
        var seen = Read(actual);
        if (wanted.Program is null || project is null || seen.Program is null)
            return false;
        return SameInstall(seen.Program, wanted.Program, Resolve) &&
               RunsThis(seen, project, bareFolder, configRoot);
    }

    /// <summary>
    /// The project <c>winshell run</c> starts when it is given no <c>-p</c> and no <c>-c</c>, or null when none would
    /// start or the config folder cannot be read. <paramref name="configRoot"/> defaults to <see cref="Winshell.ConfigPaths.Root"/>.
    /// </summary>
    internal static string? BareFolder(string? configRoot = null)
    {
        try
        {
            return ConfigurationProject.Find(null, null, configRoot ?? ConfigPaths.Root(), out _)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ProjectOf(SignInLaunch launch, string? configRoot)
    {
        if (launch.Path is { } path)
        {
            var full = Full(path);
            if (full is null)
                return null;
            return full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(full)
                : full;
        }

        if (launch.Name is { } name && CommandWords.IsConfigName(name))
            return Full(Path.Combine(configRoot ?? ConfigPaths.Root(), name));
        return null;
    }

    private static bool SamePath(string? left, string? right)
    {
        var a = Full(left);
        var b = Full(right);
        return a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Full(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException
                                      or IOException)
        {
            return null;
        }
    }
}
