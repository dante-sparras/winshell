using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace Winshell;

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

    /// <summary>
    /// Asks this project's shell to quit, then ends dotnet watch and what it started. The shell quits first, so the apps
    /// it opened are not part of the tree that ends, and its app bar and tray go as they do for <c>winshell kill</c>.
    /// </summary>
    /// <param name="closing">The console is closing, which leaves about five seconds for all of it.</param>
    private static void StopShellAndWatch(ConfigurationProject project, Process watch, bool closing)
    {
        if (ShellOf(project) is { } shell)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(closing ? 1 : 3));
#pragma warning disable VSTHRD002 // A signal handler must finish before Windows ends this process. Its thread runs no continuation.
                _ = InstanceCall.AskAsync(shell, new IpcRequest("kill"), deadline.Token).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            }
            catch (OperationCanceledException)
            {
                // The shell did not answer in time. It is ended below.
            }
            try
            {
                using var process = Process.GetProcessById(shell.Pid);
                // winshell kill waits five seconds too.
                if (Instances.IsThisInstance(shell, process) && !process.WaitForExit(TimeSpan.FromSeconds(closing ? 2 : 5)))
                {
                    process.Kill();
                    process.WaitForExit(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // The shell has exited already.
            }
        }
        try
        {
            watch.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or AggregateException)
        {
            // dotnet watch has exited already, or part of what it started could not be ended.
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
        // dotnet watch has a console of its own, which Ctrl+C, Ctrl+Break and closing this one never reach. Each of
        // those ends this process once its handler returns, so the handler stops the shell and dotnet watch first.
        void Stop(PosixSignalContext context) => StopShellAndWatch(project, process, context.Signal == PosixSignal.SIGHUP);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
        using var breaks = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Stop);
        using var close = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Stop);
        var lines = Channel.CreateUnbounded<string>();
        var pumps = Task.WhenAll(Pump(process.StandardOutput, lines.Writer), Pump(process.StandardError, lines.Writer));
        _ = pumps.ContinueWith(_ => lines.Writer.Complete(), TaskScheduler.Default);
        var output = new DevOutput(project.Folder);
        await foreach (var line in lines.Reader.ReadAllAsync())
        {
            // No key reaches dotnet watch, so its Ctrl+R hint would point at a key that does nothing.
            if (line != "dotnet watch : Press Ctrl+R to restart.")
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

    /// <summary>
    /// The running shell dotnet watch started for this project, or null before its first start. A shell <c>winshell run</c>
    /// started runs from <c>bin\shell</c> or <c>bin\shell.next</c>, and is not this one.
    /// </summary>
    private static InstanceInfo? ShellOf(ConfigurationProject project)
    {
        var folder = project.Folder + Path.DirectorySeparatorChar;
        return Instances.List(false).Select(i => i.Info).FirstOrDefault(i =>
            i.ConfigPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            && !i.ConfigPath.StartsWith(project.OutputFolder, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tells the shell of this project. Before its first start there is none, and the console has the error.</summary>
    private static async Task Show(ConfigurationProject project, string notice)
    {
        if (ShellOf(project) is not { } shell)
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
