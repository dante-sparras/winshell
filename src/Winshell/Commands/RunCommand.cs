using System.Diagnostics;
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
            StartupError.Report(error, StartupError.FolderFor(options.Path, ShellLocations.Current.ConfigRoot));
            return 1;
        }

        // With no configuration there is nothing to run, so nothing starts and nothing is said.
        if (project is null)
            return 0;
        // Before the build: a duplicate exits quietly, so it must not wait on one or fail on a broken edit.
        if (options.NoDuplicate &&
            project.InstancesOf(ConfigurationBuild.Run,
                ShellLocations.Current.Instances.List(false).Select(i => i.Info)).Any())
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
            return Detach(options.ChildArguments);

        var program = new ProcessStartInfo(project.Program) { UseShellExecute = false };
        foreach (var a in ShellArguments.Write(options.NoDuplicate, options.Verbose, options.Rest))
            program.ArgumentList.Add(a);
        return Supervise(program, project);
    }

    /// <summary>Starts <c>winshell.exe</c> again with <paramref name="childArgs"/>. The child supervises the shell.</summary>
    private static int Detach(IEnumerable<string> childArgs)
    {
        var exe = Environment.ProcessPath!; // The CLI runs from its apphost.
        if (InstanceStart.Detach(exe, childArgs) is not { } error)
            return 0;
        StartupError.Report(error, null);
        return 1;
    }

    /// <summary>
    /// Runs the shell <paramref name="shell"/> starts under a <see cref="ShellSupervisor"/>, and returns its last exit code.
    /// <paramref name="project"/> is the configuration project whose program that is.
    /// </summary>
    private static int Supervise(ProcessStartInfo shell, ConfigurationProject project)
    {
        IShellChild? Start()
        {
            // A reload can build beside a program that still runs, so each start asks for the last build. A dialog about
            // a start that failed offers its folder.
            shell.FileName = project.Program;
            return ShellProcess.Start(shell, Path.GetDirectoryName(shell.FileName));
        }

        var supervisor = new ShellSupervisor(Start, ExplorerTaskbarRecovery.AnyHidden,
            ExplorerTaskbarRecovery.RestoreHidden, TimeProvider.System);
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
}
