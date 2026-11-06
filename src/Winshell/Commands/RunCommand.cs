using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Winshell.Commands;

internal static class RunCommand
{
    internal static int Run(CliOptions options)
    {
        var project = ConfigurationSelection.FindProject(options, out var error);
        if (error is not null)
        {
            // Before any instance log exists. An attached console prints it. Start and sign-in have no window.
            StartupError.Report(error, StartupError.FolderFor(options.Path, ConfigPaths.Root()));
            return 1;
        }

        // With no configuration there is nothing to run, so nothing starts and nothing is said.
        if (project is null)
            return 0;
        var originalArgs = options.Arguments.ToArray();
        // A staged update goes in place before any shell runs, and then the new winshell.exe runs the shell.
        // System.Diagnostics.Process is loaded first, from the files this one started with.
        // RunUpdated is the private static method below.
        RuntimeHelpers.PrepareMethod(
            typeof(RunCommand).GetMethod(nameof(RunUpdated), BindingFlags.NonPublic | BindingFlags.Static)!
                .MethodHandle);
        using (Process.GetCurrentProcess())
        {
        }

        var update = InstallUpdate();
        if (update is { Error: null })
        {
            Console.Error.WriteLine($"Updated Winshell to {update.Version}.");
            return RunUpdated(originalArgs);
        }

        if (update is { Error: { } updateError })
            Console.Error.WriteLine($"Could not update Winshell to {update.Version}: {updateError}");

        // Before the build: a duplicate exits quietly, so it must not wait on one or fail on a broken edit.
        if (options.NoDuplicate &&
            ConfigurationInstances.Of(Instances.List(false).Select(i => i.Info), project).Count > 0)
            return 0;
        var reason = project.Prepare(options.NoBuild, ConfigurationProject.Run);
        if (reason is not null)
        {
            StartupError.Report(reason, project.Folder);
            return 1;
        }

        // -d leaves the program to a child that supervises it. The build is done, and its errors were in this console.
        // The child runs the tokens Parse kept, so a -d that is the value of -c or -p is not dropped.
        if (options.Daemonize)
            return Detach(options.ChildArguments.ToList());

        var program = new ProcessStartInfo(project.Program) { UseShellExecute = false };
        foreach (var a in ProgramArguments(options))
            program.ArgumentList.Add(a);
        return Supervise(program, project);
    }

    /// <summary>Starts <c>winshell.exe</c> again with <paramref name="childArgs"/>, and returns at once. The child supervises the shell.</summary>
    private static int Detach(List<string> childArgs)
    {
        var exe = Environment.ProcessPath!; // The CLI runs from its apphost.
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in childArgs)
            psi.ArgumentList.Add(a);
        try
        {
            StandardHandles.KeepFromChildren();
            using var child = Process.Start(psi);
            return 0;
        }
        catch (Win32Exception e)
        {
            StartupError.Report($"Could not start \"{exe}\": {e.Message}", null);
            return 1;
        }
    }

    /// <summary>
    /// Runs the shell <paramref name="shell"/> starts under a <see cref="ShellSupervisor"/>, and returns its last exit code.
    /// <paramref name="project"/> is the configuration project whose program that is.
    /// </summary>
    private static int Supervise(ProcessStartInfo shell, ConfigurationProject project)
    {
        IShellChild? Start()
        {
            // A reload builds beside a program that still runs, in bin\shell.next, so each start runs the last build. A
            // dialog about a start that failed offers its folder.
            shell.FileName = project.Program;
            return ShellProcess.Start(shell, Path.GetDirectoryName(shell.FileName));
        }

        var supervisor = new ShellSupervisor(Start, ExplorerTaskbarRecovery.AnyHidden, ExplorerTaskbarRecovery.Restore,
            TimeProvider.System);
        using var stop = new CancellationTokenSource();
        Task<int>? supervision = null;

        // Ctrl+C and Ctrl+Break stop the shell and supervision. Closing the console and the end of the session end this
        // process once the handler returns, so it waits for the shell to stop and the taskbar to come back. The session's
        // end is a window message: Windows sends no console signal for it to a process that has loaded user32. SystemEvents
        // makes its window on the thread that first subscribes when that thread is STA, and this one pumps no messages, so a
        // pool thread subscribes and SystemEvents makes a thread of its own.
        void Stop()
        {
            stop.Cancel();
            try
            {
#pragma warning disable VSTHRD002 // A handler must finish before Windows ends this process. Supervision runs on the pool.
                supervision?.Wait(TimeSpan.FromSeconds(4));
            }
            catch (AggregateException)
            {
                // The wait below reports it.
            }
        }

        void Signal(PosixSignalContext context)
        {
            context.Cancel = true;
            Stop();
        }

        void SessionEnded(object? sender, SessionEndedEventArgs e)
        {
            Stop();
        }

        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Signal);
        using var breaks = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Signal);
        using var close = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Signal);
        Task.Run(() => SystemEvents.SessionEnded += SessionEnded).GetAwaiter().GetResult();
        try
        {
            supervision = supervisor.RunAsync(stop.Token);
            return supervision.GetAwaiter().GetResult();
        }
        finally
        {
            SystemEvents.SessionEnded -= SessionEnded;
        }
#pragma warning restore VSTHRD002
    }

    /// <summary>What the configuration's program should see. Winshell's own selectors stay here.</summary>
    private static List<string> ProgramArguments(CliOptions options)
    {
        var args = new List<string>();
        if (options.NoDuplicate)
            args.Add("--no-duplicate");
        if (options.Verbose)
            args.Add("--verbose");
        args.AddRange(options.Rest);
        return args;
    }

    /// <summary>
    /// Puts a staged update in place, unless another shell runs: it would keep loading files from this folder
    /// as it needs them and mix versions, so the update stays ready until a start finds none. A named mutex keeps
    /// two starts at the same moment, before either is listed, from installing over each other: the second skips.
    /// </summary>
    private static InstalledUpdate? InstallUpdate()
    {
        using var installing = new Mutex(false, @"Local\winshell-update");
        bool mine;
        try
        {
            mine = installing.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            mine = true;
        }

        if (!mine)
            return null;
        try
        {
            return Instances.List(false).Count > 0
                ? null
                : Updater.InstallStaged(CliMetadata.ReleaseRepository, CliMetadata.Version);
        }
        finally
        {
            installing.ReleaseMutex();
        }
    }

    /// <summary>
    /// Runs the updated winshell.exe with this one's arguments, in this console, and ends when it does. This process
    /// stays as its parent, so a terminal or the sign-in entry's console host keeps waiting on the shell.
    /// </summary>
    private static int RunUpdated(string[] args)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false }; // The CLI runs from its apphost.
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        Process shell;
        try
        {
            // UseShellExecute=false starts a process rather than opening a document.
            shell = Process.Start(psi)!;
        }
        catch (Win32Exception e)
        {
            StartupError.Report(
                $"Winshell is updated, but the new winshell.exe did not start. Start winshell.exe again. {e.Message}",
                null);
            return 1;
        }

        using (shell)
        {
            shell.WaitForExit();
            return shell.ExitCode;
        }
    }
}
