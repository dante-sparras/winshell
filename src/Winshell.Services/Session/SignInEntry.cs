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
            Path.GetFileName(args[0]).Equals("conhost.exe", StringComparison.OrdinalIgnoreCase) &&
            args[1] == "--headless")
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
            if (args[i] == "--")
                break;
            if (args[i] is "-p" or "--path" && i + 1 < args.Length)
                path = args[++i];
            else if (args[i] is "-c" or "--config" && i + 1 < args.Length)
                name = args[++i];
        }

        return new SignInLaunch(program, path, name);
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
        if (!Host(started) || !Host(here))
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
        return ProjectFolder(processPath) is not null && supervisor is not null ? supervisor : processPath;
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
            return ConfigName(name) &&
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
    /// The project folder of a program at <c>project\bin\shell\Name.exe</c>, or <c>bin\shell.next</c> when a reload
    /// built beside a program that still had its folder open. Anything else, including <c>winshell.exe</c>, is null.
    /// </summary>
    internal static string? ProjectFolder(string program)
    {
        if (Full(program) is not { } full)
            return null;
        var shell = Path.GetDirectoryName(full);
        var bin = shell is null ? null : Path.GetDirectoryName(shell);
        var folder = bin is null ? null : Path.GetDirectoryName(bin);
        if (folder is null || bin is null || shell is null)
            return null;
        if (!Path.GetFileName(bin).Equals("bin", StringComparison.OrdinalIgnoreCase))
            return null;
        var output = Path.GetFileName(shell);
        return output.Equals("shell", StringComparison.OrdinalIgnoreCase) ||
               output.Equals("shell.next", StringComparison.OrdinalIgnoreCase)
            ? folder
            : null;
    }

    /// <summary>Whether <paramref name="path"/> is <c>winshell.exe</c> or the windowed <c>winshellw.exe</c>.</summary>
    internal static bool IsHost(string path)
    {
        return Host(path);
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

        if (launch.Name is { } name && ConfigName(name))
            return Full(Path.Combine(configRoot ?? ConfigPaths.Root(), name));
        return null;
    }

    private static bool ConfigName(string name)
    {
        return name.Length > 0 && name is not ("." or "..") && name.IndexOfAny(['/', '\\', ':']) < 0;
    }

    private static bool Host(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("winshell.exe", StringComparison.OrdinalIgnoreCase)
               || name.Equals("winshellw.exe", StringComparison.OrdinalIgnoreCase);
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
