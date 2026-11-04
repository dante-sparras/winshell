using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Winshell.DefaultShell;

namespace Winshell.Commands;

internal static class RunCommand
{
    internal static int Run(CliOptions options)
    {
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
                // -d starts this program again. That child runs the shell, so this process must not also detach.
                if (options.Daemonize)
                    return DetachDefault(originalArgs);
            return RunDefault(update);
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
        return StartProgram(project.Program, ProgramArguments(options), options.Daemonize);
    }

    /// <summary>Starts <c>winshell.exe</c> again without <c>-d</c>, and returns at once. The child runs the default shell.</summary>
    private static int DetachDefault(string[] originalArgs)
    {
        var exe = Environment.ProcessPath!; // The CLI runs from its apphost.
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        var childArgs = originalArgs.Where(a => a is not ("-d" or "--daemonize")).ToList();
        // The child has no console. A bare child would print help and exit, so name the run command.
        if (CliParser.FirstCommand([.. childArgs]) is null)
            childArgs.Insert(0, "run");
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

    /// <summary>The default shell, in this process. <c>--no-duplicate</c> and <c>-v</c> are on the command line <see cref="Shell.Run{T}"/> reads.</summary>
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
        if (options.Daemonize)
            args.Add("-d");
        if (options.NoDuplicate)
            args.Add("--no-duplicate");
        if (options.Verbose)
            args.Add("--verbose");
        args.AddRange(options.Rest);
        return args;
    }

    /// <summary>Starts a configuration's program and returns its exit code. <c>-d</c> makes that program return at once.</summary>
    private static int StartProgram(string program, IReadOnlyList<string> args, bool daemonize)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = daemonize };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        try
        {
            // With -d the program starts the shell again and returns, and that shell must not hold this one's output. The
            // program's own -d can't do it: it inherits these pipes, but they are not its standard handles.
            if (daemonize)
                StandardHandles.KeepFromChildren();
            // UseShellExecute=false starts a process, so Start cannot return null for an opened document.
            using var process = Process.Start(psi)!;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception e)
        {
            StartupError.Report($"Could not start \"{program}\": {e.Message}", System.IO.Path.GetDirectoryName(program));
            return 1;
        }
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
