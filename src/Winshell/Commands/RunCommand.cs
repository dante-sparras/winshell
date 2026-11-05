using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Winshell.DefaultShell;

namespace Winshell.Commands;

internal static class RunCommand
{
    /// <summary>
    /// Set on the process that runs the default shell for its supervisor, which has already put any update in place. A
    /// failure to install it is in <see cref="UpdateFailureVariable"/>, as the version, a line break and the error. That
    /// process clears both, so what it starts, the process a reload hands over to included, starts as usual.
    /// </summary>
    private const string SupervisedVariable = "WINSHELL_SUPERVISED";

    private const string UpdateFailureVariable = "WINSHELL_UPDATE_FAILURE";

    internal static int Run(CliOptions options)
    {
        if (Environment.GetEnvironmentVariable(SupervisedVariable) is not null)
        {
            var failure = Environment.GetEnvironmentVariable(UpdateFailureVariable)?.Split('\n', 2);
            Environment.SetEnvironmentVariable(SupervisedVariable, null);
            Environment.SetEnvironmentVariable(UpdateFailureVariable, null);
            return RunDefault(failure is [{ } version, { } message] ? new InstalledUpdate(version, message) : null);
        }
        var originalArgs = options.Arguments.ToArray();
        // Restarted to update: the shell before this one lets go of the taskbar, the hotkeys and the tray first.
        if (options.After is { } previous)
            Instances.WaitForExit(previous);
        // A downloaded update goes in place before any shell runs, and then the new winshell.exe runs the shell.
        // System.Diagnostics.Process is loaded first, from the files this one started with.
        // RunUpdated is the private static method below.
        RuntimeHelpers.PrepareMethod(typeof(RunCommand).GetMethod(nameof(RunUpdated), BindingFlags.NonPublic | BindingFlags.Static)!.MethodHandle);
        using (Process.GetCurrentProcess()) { }
        var update = InstallUpdate(options.After);
        if (update is { Error: null })
        {
            Console.Error.WriteLine($"Updated Winshell to {update.Version}.");
            return RunUpdated(originalArgs);
        }
        if (update is { Error: { } updateError })
            Console.Error.WriteLine($"Could not update Winshell to {update.Version}: {updateError}");

        var project = ConfigurationSelection.FindProject(options, out var error);
        if (error is not null)
        {
            // Before any instance log exists. An attached console prints it. Start and sign-in have no window.
            StartupError.Report(error, StartupError.FolderFor(options.Path, ConfigPaths.Root()));
            return 1;
        }
        if (project is null)
        {
            if (options.Path is null)
                // -d starts this program again. That child supervises the shell, so this process must not also detach.
                if (options.Daemonize)
                    return Detach(ChildArguments(originalArgs));
            return Supervise(DefaultShellStart(originalArgs, update), null);
        }
        // Before the build: a duplicate exits quietly, so it must not wait on one or fail on a broken edit.
        if (options.NoDuplicate && Instances.List(false).Any(i => InstanceSelection.RunsProject(i.Info, project)))
            return 0;
        var reason = project.Prepare(options.NoBuild, ConfigurationProject.Run);
        if (reason is not null)
        {
            StartupError.Report(reason, project.Folder);
            return 1;
        }
        // -d leaves the program to a child that supervises it. The build is done, and its errors were in this console.
        if (options.Daemonize)
        {
            var childArgs = ChildArguments(originalArgs);
            if (!options.NoBuild)
                childArgs.Insert(0, "--no-build");
            return Detach(childArgs);
        }
        var program = new ProcessStartInfo(project.Program) { UseShellExecute = false };
        foreach (var a in ProgramArguments(options))
            program.ArgumentList.Add(a);
        return Supervise(program, System.IO.Path.GetDirectoryName(project.Program));
    }

    /// <summary>
    /// This program's arguments for another <c>winshell.exe</c> that runs the shell, without <c>-d</c>. A bare
    /// <c>winshellw.exe</c> runs a shell, but a bare child of <c>winshell.exe</c> would print help, so it gets the run command.
    /// </summary>
    private static List<string> ChildArguments(string[] originalArgs)
    {
        var childArgs = originalArgs.Where(a => a is not ("-d" or "--daemonize")).ToList();
        if (CliParser.FirstCommand([.. childArgs]) is null)
            childArgs.Insert(0, "run");
        return childArgs;
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

    /// <summary>The process that runs the default shell for this one, which supervises it.</summary>
    private static ProcessStartInfo DefaultShellStart(string[] originalArgs, InstalledUpdate? update)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }; // The CLI runs from its apphost.
        foreach (var a in ChildArguments(originalArgs))
            psi.ArgumentList.Add(a);
        psi.Environment[SupervisedVariable] = "1";
        if (update is { Error: { } failure })
            psi.Environment[UpdateFailureVariable] = $"{update.Version}\n{failure}";
        return psi;
    }

    /// <summary>
    /// Runs the shell <paramref name="shell"/> starts under a <see cref="ShellSupervisor"/>, and returns its last exit code.
    /// A dialog about a start that failed can offer <paramref name="folder"/>.
    /// </summary>
    private static int Supervise(ProcessStartInfo shell, string? folder)
    {
        var supervisor = new ShellSupervisor(() => ShellProcess.Start(shell, folder), Win32ExplorerTaskbar.Showing, Win32ExplorerTaskbar.RestoreHidden, TimeProvider.System);
        using var stop = new CancellationTokenSource();
        Task<int>? supervision = null;
        // Ctrl+C and Ctrl+Break stop the shell and supervision. Closing the console and the end of the session end this
        // process once the handler returns, so it waits for the shell to stop and the taskbar to come back. The session's
        // end is a window message: Windows sends no console signal for it to a process that has loaded user32.
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
        void SessionEnded(object? sender, SessionEndedEventArgs e) => Stop();
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Signal);
        using var breaks = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Signal);
        using var close = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Signal);
        SystemEvents.SessionEnded += SessionEnded;
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

    /// <summary>
    /// The default shell, in this process, which its supervisor started. <c>--no-duplicate</c> and <c>-v</c> are on the
    /// command line <see cref="Shell.Run{T}"/> reads.
    /// </summary>
    private static int RunDefault(InstalledUpdate? update)
    {
        using var updater = Updater.ForRelease(CliMetadata.ReleaseRepository, CliMetadata.Version, Log.Default);
        if (updater is not null && update is { Error: { } failure })
            updater.ReportApplyFailure(update.Version, failure);
        var options = new ShellOptions
        {
            ShellId = "winshell-default",
            Updater = updater,
        };
        Shell.Run<DefaultShellConfiguration>(options);
        return Environment.ExitCode;
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
    /// Puts a downloaded update in place, unless another shell runs: it would keep loading files from this folder
    /// as it needs them and mix versions, so the update stays ready until a start finds none. A named mutex keeps
    /// two starts at the same moment, before either is listed, from installing over each other: the second skips.
    /// </summary>
    private static InstalledUpdate? InstallUpdate(int? after)
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
            return Instances.List(false).Any(i => i.Info.Pid != after) ? null : Updater.InstallStaged(CliMetadata.ReleaseRepository, CliMetadata.Version);
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
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }; // The CLI runs from its apphost.
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        Process shell;
        try
        {
            // UseShellExecute=false starts a process rather than opening a document.
            shell = Process.Start(psi)!;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            StartupError.Report($"Winshell is updated, but the new winshell.exe did not start. Start winshell.exe again. {e.Message}", null);
            return 1;
        }
        using (shell)
        {
            shell.WaitForExit();
            return shell.ExitCode;
        }
    }
}
