using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Winshell;

/// <summary>
/// Reads what <c>dotnet watch</c> prints. <see cref="Read"/> gives the text of the notice a line calls for: the compile
/// errors so far, or empty once the code is applied. Null means the line changes nothing on screen.
/// </summary>
internal sealed partial class DevOutput
{
    private const string Prefix = "dotnet watch : ";
    private const int MostErrors = 5;
    private readonly List<string> _errors = [];

    public string? Read(string line)
    {
        var text = line.StartsWith(Prefix, StringComparison.Ordinal) ? line[Prefix.Length..] : line.Trim();
        if (CompileError().IsMatch(text))
        {
            var error = ProjectSuffix().Replace(text, "");
            if (_errors.Contains(error) || _errors.Count == MostErrors)
                return null;
            _errors.Add(error);
            return string.Join('\n', _errors);
        }
        if (text.StartsWith("Unable to apply changes", StringComparison.Ordinal))
            _errors.Clear();
        if (!Applied().IsMatch(text))
            return null;
        _errors.Clear();
        return "";
    }

    [GeneratedRegex(@"\berror [A-Z]+\d+:")]
    private static partial Regex CompileError();

    [GeneratedRegex(@"\s+\[[^\]]*\.csproj(::[^\]]*)?\]$")]
    private static partial Regex ProjectSuffix();

    [GeneratedRegex(@"changes applied in \d+ms|^Build succeeded")]
    private static partial Regex Applied();
}

/// <summary>
/// <c>winshell dev</c>: runs a configuration under <c>dotnet watch</c>, which applies a C# edit to the running program, and
/// shows what would not compile on that program's screen. It stays until Ctrl+C, so a failed build costs nothing.
/// </summary>
internal static class DevSession
{
    public static async Task<int> Run(ConfigurationProject project)
    {
        while (true)
        {
            if (await Watch(project) is { } exitCode)
                return exitCode;
            Console.WriteLine("Building, because the XAML you saved needs it.");
        }
    }

    /// <summary>How <c>dotnet watch</c> ended, or null when a shell asked for a build, which only a new start gives it.</summary>
    private static async Task<int?> Watch(ConfigurationProject project)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = project.Folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "watch", "--non-interactive", "--project", project.ProjectFile, "run", "--no-launch-profile" })
            start.ArgumentList.Add(argument);
        // Its lines are what DevOutput reads, in the one form it knows.
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
        start.Environment[Shell.DevProjectVariable] = project.Folder;
        using var process = Start(start);
        if (process is null)
        {
            Console.Error.WriteLine(ConfigurationProject.NeedsSdk);
            return 1;
        }
        var lines = Channel.CreateUnbounded<string>();
        var pumps = Task.WhenAll(Pump(process.StandardOutput, lines.Writer), Pump(process.StandardError, lines.Writer));
        _ = pumps.ContinueWith(_ => lines.Writer.Complete(), TaskScheduler.Default);
        var output = new DevOutput();
        await foreach (var line in lines.Reader.ReadAllAsync())
        {
            Console.WriteLine(line);
            if (output.Read(line) is { } notice)
                await Show(project, notice);
            if (line.Contains(XamlReload.NeedsBuild, StringComparison.Ordinal))
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                return null;
            }
        }
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static Process? Start(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static async Task Pump(StreamReader stream, ChannelWriter<string> lines)
    {
        while (await stream.ReadLineAsync() is { } line)
            await lines.WriteAsync(line);
    }

    /// <summary>Tells the shell of this project. Before its first start there is none, and the console has the error.</summary>
    private static async Task Show(ConfigurationProject project, string notice)
    {
        var folder = project.Folder + Path.DirectorySeparatorChar;
        var shell = Instances.List(false).Select(i => i.Info).FirstOrDefault(i => i.ConfigPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
        if (shell is null)
            return;
        var request = notice.Length > 0
            ? new IpcRequest("build-error", Args: [notice, project.ProjectFile])
            : new IpcRequest("call", Target: "notice", Name: "dismiss");
        try
        {
            await foreach (var response in IpcServer.Request(shell.Pipe, request))
            {
                if (!response.Ok)
                    Console.Error.WriteLine($"Could not update the notice: {response.Error}");
                return;
            }
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not update the notice: {e.Message}");
        }
    }
}
