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
    /// Asks this project's shell to quit, as <c>winshell kill</c> does, so its app bar and tray go and its instance is
    /// marked exited. Then ends dotnet watch and what it started.
    /// </summary>
    /// <param name="closing">The console is closing, which leaves about five seconds for all of it.</param>
    private static void StopShellAndWatch(ConfigurationProject project, Process watch, bool closing)
    {
        if (ShellOf(project) is { } shell)
        {
            var patience = closing ? ShellExit.Patience.Closing : ShellExit.Patience.Interactive;
#pragma warning disable VSTHRD002 // A signal handler must finish before Windows ends this process, and the build loop waits for this anyway. No thread here runs a continuation it waits for.
            _ = ShellExit.QuitAsync(shell, ExitIntent.None, patience, ShellExit.Ending.Always).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }

        try
        {
            watch.Kill(true);
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
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                     { "watch", "--non-interactive", "--project", project.ProjectFile, "run", "--no-launch-profile" })
            start.ArgumentList.Add(argument);
        // Its lines are what DevOutput reads, in the one form it knows.
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
        start.Environment[Shell.DevProjectVariable] = project.Folder;
        using var buildRequest =
            new EventWaitHandle(false, EventResetMode.ManualReset, XamlReload.BuildRequest(project.Folder));
        using var process = Start(start);
        if (process is null)
        {
            Console.Error.WriteLine(ConfigurationProject.NeedsSdk);
            return 1;
        }

        // dotnet watch has a console of its own, which Ctrl+C, Ctrl+Break and closing this one never reach. Each of
        // those ends this process once its handler returns, so the handler stops the shell and dotnet watch first.
        void Stop(PosixSignalContext context)
        {
            StopShellAndWatch(project, process, context.Signal == PosixSignal.SIGHUP);
        }

        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
        using var breaks = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Stop);
        using var close = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Stop);
        // A null line is the shell's build request.
        var lines = Channel.CreateUnbounded<string?>();
        var pumps = Task.WhenAll(Pump(process.StandardOutput, lines.Writer), Pump(process.StandardError, lines.Writer));
        _ = pumps.ContinueWith(_ => lines.Writer.Complete(), TaskScheduler.Default);
        var asked = ThreadPool.RegisterWaitForSingleObject(buildRequest, (_, _) => lines.Writer.TryWrite(null), null,
            Timeout.Infinite, true);
        try
        {
            var output = new DevOutput(project.Folder);
            await foreach (var line in lines.Reader.ReadAllAsync())
            {
                if (line is null)
                {
                    StopShellAndWatch(project, process, false);
                    await process.WaitForExitAsync();
                    return null;
                }

                // No key reaches dotnet watch, so its Ctrl+R hint would point at a key that does nothing.
                if (line != "dotnet watch : Press Ctrl+R to restart.")
                    Console.WriteLine(line);
                if (output.Read(line) is { } notice)
                    await Show(project, notice);
            }
        }
        finally
        {
            asked.Unregister(null);
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

    private static async Task Pump(StreamReader stream, ChannelWriter<string?> lines)
    {
        while (await stream.ReadLineAsync() is { } line)
            await lines.WriteAsync(line);
    }

    /// <summary>The running shell dotnet watch started for this project, or null before its first start.</summary>
    private static InstanceInfo? ShellOf(ConfigurationProject project)
    {
        return project
            .InstancesOf(ConfigurationBuild.Dev, ShellLocations.Current.Instances.List(false).Select(i => i.Info))
            .FirstOrDefault();
    }

    /// <summary>Tells the shell of this project. Before its first start there is none, and the console has the error.</summary>
    private static async Task Show(ConfigurationProject project, string notice)
    {
        if (ShellOf(project) is not { } shell)
            return;
        await BuildNotice.Show(shell, notice, project.ProjectFile);
    }
}
