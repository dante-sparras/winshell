using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Winshell;

/// <summary>What a command printed and how it ended.</summary>
internal sealed record CommandResult(int ExitCode, string Output);

/// <summary>Runs a program in a folder and waits for it. Tests replace this, so most of them build nothing.</summary>
internal delegate CommandResult RunProcess(string file, IReadOnlyList<string> arguments, string folder);

/// <summary>Which of a configuration's programs an instance runs.</summary>
internal enum ConfigurationBuild
{
    /// <summary>What <c>winshell run</c> and <c>reload</c> build: <see cref="ConfigurationProject.Program"/>.</summary>
    Run,

    /// <summary>Any other program under the project's folder: what <c>winshell dev</c>'s <c>dotnet watch</c> builds.</summary>
    Dev
}

/// <summary>
/// A configuration on disk: the .NET project that defines a shell, and the program it builds into. Nothing here runs
/// MSBuild outside a build, so <c>doctor</c> and a start with <c>--no-build</c> read the project as data. Building runs
/// the project's own code, so a downloaded project is refused first.
/// </summary>
internal sealed partial class ConfigurationProject
{
    private const int MostErrors = 10;
    internal const string NeedsSdk = "Building a configuration needs the .NET SDK, from https://dot.net/download.";

    private static readonly TimeSpan BuildLimit = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StreamLimit = TimeSpan.FromSeconds(1);

    private ConfigurationProject(string projectFile)
    {
        ProjectFile = projectFile;
        Folder = Path.GetDirectoryName(projectFile)!;
        Name = Path.GetFileNameWithoutExtension(projectFile);
    }

    public string ProjectFile { get; }

    public string Folder { get; }

    public string Name { get; }

    /// <summary>Where a build puts the program. It is fixed, so a start with no SDK finds it without asking MSBuild.</summary>
    public string OutputFolder => Path.Combine(Folder, "bin", Output);

    /// <summary>Where a reload builds when a program still running holds <see cref="OutputFolder"/> open.</summary>
    private string BesideFolder => Path.Combine(Folder, "bin", OutputBeside);

    private const string Output = "shell";
    private const string OutputBeside = "shell.next";

    /// <summary>
    /// The project folder of a program at <c>&lt;project&gt;\bin\shell\Name.exe</c>, or <c>bin\shell.next\Name.exe</c>
    /// when a reload had to build beside a program that still had its folder open. Nothing is read from disk, so a
    /// sign-in command can name a project that is not there. Anything else, including <c>winshell.exe</c>, is null.
    /// </summary>
    public static string? FolderOf(string program)
    {
        if (Full(program) is not { } full
            || Path.GetDirectoryName(full) is not { } output
            || Path.GetDirectoryName(output) is not { } bin
            || Path.GetDirectoryName(bin) is not { } folder
            || !Path.GetFileName(bin).Equals("bin", StringComparison.OrdinalIgnoreCase))
            return null;
        var name = Path.GetFileName(output);
        return name.Equals(Output, StringComparison.OrdinalIgnoreCase) ||
               name.Equals(OutputBeside, StringComparison.OrdinalIgnoreCase)
            ? folder
            : null;
    }

    /// <summary>The project whose <see cref="ConfigurationBuild.Run"/> program is <paramref name="program"/>, or null.</summary>
    public static ConfigurationProject? ForProgram(string program)
    {
        if (FolderOf(program) is not { } folder || In(folder, out _) is not { } project)
            return null;
        return project.IsProgram(Path.GetFullPath(program)) ? project : null;
    }

    /// <summary>
    /// The instances in <paramref name="instances"/> that run <paramref name="build"/> of this project, in their order.
    /// A program whose folder only shares a prefix with this project's is not one of them.
    /// </summary>
    public IEnumerable<InstanceInfo> InstancesOf(ConfigurationBuild build, IEnumerable<InstanceInfo> instances)
    {
        return instances.Where(instance => BuildOf(instance.ConfigPath) == build);
    }

    private ConfigurationBuild? BuildOf(string program)
    {
        if (Full(program) is not { } full || !full.StartsWith(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            return null;
        return IsProgram(full) ? ConfigurationBuild.Run : ConfigurationBuild.Dev;
    }

    private bool IsProgram(string full)
    {
        var folder = Path.GetDirectoryName(full);
        return (SameFolder(folder, OutputFolder) || SameFolder(folder, BesideFolder)) &&
               Path.GetFileName(full).Equals(AssemblyName() + ".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameFolder(string? full, string folder)
    {
        return full is not null && full.Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
    }

    private static string? Full(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException
                                      or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The program to run: the one in <see cref="OutputFolder"/>, or the one in <see cref="BesideFolder"/> when it was
    /// built later. A build stamps what it wrote with the time, so the newer of the two is always the last build.
    /// </summary>
    public string Program
    {
        get
        {
            var program = ProgramIn(OutputFolder);
            var beside = ProgramIn(BesideFolder);
            return File.Exists(beside) && (!File.Exists(program) ||
                                           File.GetLastWriteTimeUtc(beside) > File.GetLastWriteTimeUtc(program))
                ? beside
                : program;
        }
    }

    private string ProgramIn(string folder)
    {
        return Path.Combine(folder, AssemblyName() + ".exe");
    }

    /// <summary>
    /// The project to run: <paramref name="path"/>, a project or a folder holding one; or a named folder in
    /// <paramref name="root"/>; or else the one in <paramref name="root"/>, then its <c>default</c> folder, then its only
    /// folder with a project. Null with no error when there is none, so nothing runs. A folder Winshell cannot read is
    /// an error.
    /// </summary>
    public static ConfigurationProject? Find(string? path, string? name, string root, out string? error)
    {
        try
        {
            return Choose(path, name, root, out error);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var what = path ?? (name is not null ? $"config \"{name}\"" : "the config folder");
            error = $"Winshell cannot read {what}: {e.Message}";
            return null;
        }
    }

    private static ConfigurationProject? Choose(string? path, string? name, string root, out string? error)
    {
        error = null;
        if (path is not null)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                if (full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    return new ConfigurationProject(full);
                error = $"No .csproj at {full}.";
                return null;
            }

            var found = In(full, out error);
            if (found is null && error is null)
                error = $"No .csproj at {full}.";
            return found;
        }

        if (name is not null)
        {
            var folder = Path.Combine(root, name);
            var found = In(folder, out error);
            if (found is null && error is null)
                error = $"No config named \"{name}\" in {root}.";
            return found;
        }

        // Several projects in the root is a refusal, not a miss: neither default nor a lone folder may stand in for it.
        if (In(root, out error) is { } atRoot)
            return atRoot;
        if (error is not null)
            return null;
        if (In(Path.Combine(root, "default"), out error) is { } usual)
            return usual;
        if (error is not null || !Directory.Exists(root))
            return null;
        var folders = Directory.EnumerateDirectories(root).Where(folder => Projects(folder).Length > 0).ToList();
        if (folders.Count > 1)
        {
            error =
                $"Several configs in {root}; pick one with -c: {string.Join(", ", folders.Select(Path.GetFileName))}.";
            return null;
        }

        return folders.Count == 1 ? In(folders[0], out error) : null;
    }

    /// <summary>
    /// Gets the program ready to start, and says why it isn't, or null when it is. <paramref name="noBuild"/> runs what the
    /// last build left, even if a source is newer, so signing in never depends on the SDK or on a broken edit.
    /// </summary>
    public string? Prepare(bool noBuild, RunProcess run)
    {
        try
        {
            if (Downloaded() is { } file)
                return $"{file}: {ZoneIdentifier.Blocked(file)}";
            if (noBuild)
                return File.Exists(Program) ? null : $"{Name} has not been built. Run winshell run once to build it.";
            return NeedsBuild() ? Build(run) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    /// <summary>Whether a build would change the program: there is none, or a file of the project is newer.</summary>
    public bool NeedsBuild()
    {
        var program = Program;
        if (!File.Exists(program))
            return true;
        var built = File.GetLastWriteTimeUtc(program);
        return Sources(Folder).Any(source => File.GetLastWriteTimeUtc(source) > built);
    }

    /// <summary>
    /// The first file MSBuild would run that came from the internet, or null. Beside the project that means its props
    /// and targets as well, which run as much as the project does.
    /// </summary>
    public string? Downloaded()
    {
        return Directory.EnumerateFiles(Folder)
            .Where(file => Path.GetExtension(file).ToUpperInvariant() is ".CSPROJ" or ".PROPS" or ".TARGETS")
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(ZoneIdentifier.IsDownloaded);
    }

    /// <summary>Runs a program and collects what it prints, up to a limit. The streams get a short wait after it exits, because a build server that outlives it keeps them open. They have threads of their own, so a busy pool cannot lose the last lines.</summary>
    public static CommandResult Run(string file, IReadOnlyList<string> arguments, string folder)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        var lines = new List<string>();

        Thread Read(StreamReader stream)
        {
            var reader = new Thread(() =>
                {
                    while (stream.ReadLine() is { } line)
                        lock (lines)
                        {
                            lines.Add(line);
                        }
                })
                { IsBackground = true };
            reader.Start();
            return reader;
        }

        try
        {
            // Not disposed: a reader may still be waiting on a stream that a build server holds.
            var process = Process.Start(start)!;
            Thread[] readers = [Read(process.StandardOutput), Read(process.StandardError)];
            if (!process.WaitForExit(BuildLimit))
            {
                process.Kill(true);
                return new CommandResult(-1,
                    $"{Path.GetFileName(file)} was still running after {BuildLimit.TotalMinutes} minutes.");
            }

            foreach (var reader in readers)
                reader.Join(StreamLimit);
            lock (lines)
            {
                return new CommandResult(process.ExitCode, string.Join('\n', lines));
            }
        }
        catch (Win32Exception)
        {
            return new CommandResult(-1, NeedsSdk);
        }
    }

    private string? Build(RunProcess run)
    {
        // A running program keeps its files open. Windows will not replace those, and a build then waits on them
        // until it gives up. Renaming the folder lets the build write the new program. A WinUI program also keeps the
        // folder itself open, so the rename fails and the build goes in the folder beside it.
        var destination = OutputFolder;
        var parked = Park(destination);
        if (parked is null && Directory.Exists(destination))
        {
            destination = BesideFolder;
            parked = Park(destination);
            if (parked is null && Directory.Exists(destination))
                return $"{Name} is running, and the build could not write a new program beside it.";
        }

        var result = run("dotnet", ["build", ProjectFile, "-c", "Release", "-o", destination, "--nologo", "-v", "q"],
            Folder);
        if (result.ExitCode == 0)
        {
            QuietDelete(parked);
            var built = ProgramIn(destination);
            if (File.Exists(built))
                File.SetLastWriteTimeUtc(built, TimeProvider.System.GetUtcNow().UtcDateTime);
            return null;
        }

        if (parked is not null)
            Restore(destination, parked);
        return $"{Name} did not build.\n{Errors(result.Output)}";
    }

    /// <summary>Moves <paramref name="folder"/> aside, or null when there was nothing to move or the move failed.</summary>
    private static string? Park(string folder)
    {
        var parked = folder + ".previous";
        QuietDelete(parked);
        if (!Directory.Exists(folder))
            return null;
        try
        {
            Directory.Move(folder, parked);
            return parked;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Puts a parked program back when the build failed, so the last one that ran is still there.</summary>
    private static void Restore(string folder, string parked)
    {
        if (!Directory.Exists(parked))
            return;
        try
        {
            QuietDelete(folder);
            if (!Directory.Exists(folder))
                Directory.Move(parked, folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void QuietDelete(string? path)
    {
        if (path is null || !Directory.Exists(path))
            return;
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The compiler's own lines, once each, as <see cref="ErrorLine"/> shows them. Any other failure keeps its last lines.</summary>
    private string Errors(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var errors = lines.Where(IsErrorLine)
            .Select(line => ErrorLine(line, Folder)).Distinct().ToList();
        return string.Join('\n', errors.Count > 0 ? errors.Take(MostErrors) : lines.TakeLast(MostErrors));
    }

    /// <summary>
    /// Whether a line of build output is an error: <c>error</c> then its code, such as <c>error CS1002:</c> or
    /// <c>XamlCompiler error WMC0130:</c>, or <c>error :</c> for one with no code.
    /// </summary>
    internal static bool IsErrorLine(string line)
    {
        return CompilerError().IsMatch(line);
    }

    [GeneratedRegex(@"\berror(?: [A-Z]+\d+| ):")]
    private static partial Regex CompilerError();

    /// <summary>
    /// A compiler line as a build-error notice shows it: without the project MSBuild adds to it, and with a file under
    /// <paramref name="folder"/> named from there. A deep folder would wrap over the notice and push the error itself down.
    /// </summary>
    internal static string ErrorLine(string line, string folder)
    {
        var error = ProjectSuffix().Replace(line, "");
        var inFolder = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return error.StartsWith(inFolder, StringComparison.OrdinalIgnoreCase) ? error[inFolder.Length..] : error;
    }

    [GeneratedRegex(@"\s+\[[^\]]*\.csproj(::[^\]]*)?\]$", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectSuffix();

    /// <summary>The project's <c>AssemblyName</c> when it writes one out, or else its file name. A computed one is not read: that would take MSBuild.</summary>
    private string AssemblyName()
    {
        try
        {
            using var reader = XmlReader.Create(ProjectFile,
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var written = XDocument.Load(reader).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")?.Value.Trim();
            return string.IsNullOrEmpty(written) || written.Contains('$', StringComparison.Ordinal) ? Name : written;
        }
        catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException)
        {
            return Name;
        }
    }

    private static ConfigurationProject? In(string folder, out string? error)
    {
        error = null;
        var projects = Projects(folder);
        if (projects.Length > 1)
        {
            error =
                $"Several projects in {folder}; name one with -p: {string.Join(", ", projects.Select(Path.GetFileName))}.";
            return null;
        }

        return projects.Length == 1 ? new ConfigurationProject(projects[0]) : null;
    }

    private static string[] Projects(string folder)
    {
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.csproj") : [];
    }

    /// <summary>The project's files. <c>bin</c>, <c>obj</c> and dot folders are what a build and an editor write.</summary>
    private static IEnumerable<string> Sources(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
            yield return file;
        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(child);
            if (name is "bin" or "obj" || name.StartsWith('.'))
                continue;
            foreach (var file in Sources(child))
                yield return file;
        }
    }
}
