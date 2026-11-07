using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace Winshell;

/// <summary>What a configuration sets before its shell starts.</summary>
public sealed record ShellOptions
{
    /// <summary>
    /// The name that identifies this configuration across runs. It names its data, state and cache folders. Without
    /// it, a program in the config folder is named after its folder, and one anywhere else after its path.
    /// </summary>
    public string? ShellId { get; init; }

    /// <summary>
    /// Another folder for <see cref="Shell.DataDir"/>. A relative path is next to the program, <c>%NAME%</c> expands,
    /// and <c>$BASE</c> is the folder above <c>by-shell</c>, such as <c>%LOCALAPPDATA%\winshell\data</c>.
    /// </summary>
    public string? DataDir { get; init; }

    /// <summary>Another folder for <see cref="Shell.StateDir"/>, read as <see cref="DataDir"/> is.</summary>
    public string? StateDir { get; init; }

    /// <summary>Another folder for <see cref="Shell.CacheDir"/>, read as <see cref="DataDir"/> is.</summary>
    public string? CacheDir { get; init; }
}

/// <summary>A configuration: the program that defines a shell. <see cref="Start"/> makes its windows.</summary>
public abstract class ShellConfiguration
{
    /// <summary>Runs once, on the shell's thread, after WinUI has started.</summary>
    public abstract void Start(Shell shell);
}

/// <summary>
/// The running shell: its screens and services. There is one in a process. Each service is made the first time it is
/// read, so a service a configuration does not use costs nothing and asks Windows for nothing.
/// </summary>
public sealed partial class Shell : IDisposable
{
    private static Shell? _current;

    private readonly ShellIdentity _identity;
    private readonly ShellArguments _arguments;
    private bool _nextStarted;
    private bool _disposed;

    internal Shell(IEventLoop loop, Log log, ShellOptions options, ShellIdentity identity, ShellArguments arguments)
    {
        Loop = loop;
        Log = log;
        Options = options;
        _identity = identity;
        _arguments = arguments;
        Ipc = new ShellIpc(log);
        Platform = new ShellPlatform(loop, log);
    }

    /// <summary>The project folder <c>winshell dev</c> runs, where a shell follows its <c>.xaml</c> files.</summary>
    internal const string DevProjectVariable = "WINSHELL_DEV_PROJECT";

    /// <summary>The shell of this process, or null before <see cref="Run{T}"/> has started it.</summary>
    internal static Shell? Running => _current;

    /// <summary>The shell of this process. Reading it before <see cref="Run{T}"/> has started it throws.</summary>
    public static Shell Current =>
        _current ?? throw new InvalidOperationException("The shell has not started. Call Shell.Run first.");

    /// <summary>The shell's thread. Post work from other threads to it before touching windows or services.</summary>
    public IEventLoop Loop { get; }

    /// <summary>This instance's log, which <c>winshell log</c> shows.</summary>
    public Log Log { get; }

    /// <summary>The options the configuration passed to <see cref="Run{T}"/>.</summary>
    public ShellOptions Options { get; }

    internal ShellPlatform Platform { get; }

    /// <summary>The Winshell release this program is built on.</summary>
    public static string Version => ProductVersion.Current.Text;

    /// <summary>The name that identifies this configuration across runs.</summary>
    public string ShellId => _identity.ShellId;

    /// <summary>What <c>winshell ipc</c> reaches in this shell.</summary>
    public ShellIpc Ipc { get; }

    /// <summary>The folder of this configuration's program, where its own files are.</summary>
    public string ConfigDir => Path.GetDirectoryName(Environment.ProcessPath)!;

    /// <summary>The config folder: <c>%APPDATA%\winshell</c>, or <c>WINSHELL_CONFIG_DIR</c> when it is set.</summary>
    public string ConfigRoot => ConfigPaths.Root();

    /// <summary>This instance's log file, or empty when it writes none.</summary>
    public string LogPath { get; internal set; } = "";

    /// <summary>This shell's data folder, which exists once read.</summary>
    public string DataDir => Ensure(_identity.DataDir);

    /// <summary>This shell's state folder, which exists once read.</summary>
    public string StateDir => Ensure(_identity.StateDir);

    /// <summary>This shell's cache folder, which exists once read.</summary>
    public string CacheDir => Ensure(_identity.CacheDir);

    /// <summary>The connected screens. The primary screen comes first.</summary>
    public ObservableCollection<ScreenInfo> Screens => Platform.Screens;

    /// <summary>Whether windows are dark. <see cref="ColorScheme"/> overrides Windows mode, and <see cref="ColorScheme.System"/> follows it.</summary>
    public bool IsDark => Platform.IsDark;

    /// <summary>The color scheme every window follows.</summary>
    public ColorScheme ColorScheme
    {
        get => Platform.ColorScheme;
        set => Platform.ColorScheme = value;
    }

    /// <summary>The accent every window follows, as <c>#RRGGBB</c>, or <c>""</c> to follow Windows.</summary>
    public string AccentColor
    {
        get => Platform.AccentColor;
        set => Platform.AccentColor = value;
    }

    /// <summary>The accent Windows is using, as <c>#RRGGBB</c>.</summary>
    public string SystemAccent => Platform.SystemAccent;

    /// <summary>A path in <see cref="ConfigDir"/>.</summary>
    public string ConfigPath(string path)
    {
        return Path.Combine(ConfigDir, path);
    }

    /// <summary>A path in <see cref="DataDir"/>.</summary>
    public string DataPath(string path)
    {
        return Path.Combine(DataDir, path);
    }

    /// <summary>A path in <see cref="StateDir"/>.</summary>
    public string StatePath(string path)
    {
        return Path.Combine(StateDir, path);
    }

    /// <summary>A path in <see cref="CacheDir"/>.</summary>
    public string CachePath(string path)
    {
        return Path.Combine(CacheDir, path);
    }

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Runs a configuration until the shell quits, on this thread, as a Winshell instance: <c>winshell list</c>, <c>log</c>,
    /// <c>kill</c>, <c>reload</c> and <c>ipc</c> reach it. Call it from <c>Main</c>, marked <c>[STAThread]</c>. The program
    /// takes <c>--no-duplicate</c> and <c>-d</c> on its command line. <c>--no-duplicate</c> exits at once when its shell
    /// id is already running. <c>-d</c> starts this program again and returns, so the caller does not wait, and that
    /// start keeps <c>--after</c>. <typeparamref name="T"/> is a <see cref="ShellConfiguration"/> with a public
    /// parameterless constructor.
    /// </summary>
    public static void Run<T>(ShellOptions? options = null)
        where T : ShellConfiguration, new()
    {
        Run(new T(), options ?? new ShellOptions());
    }

    private static void Run(ShellConfiguration configuration, ShellOptions options)
    {
        InstanceStart.Run(new InstanceLaunch(
                Environment.ProcessPath!,
                [.. Environment.GetCommandLineArgs().Skip(1)],
                options.ShellId,
                options.DataDir,
                options.StateDir,
                options.CacheDir,
                Log.Default,
                instance => instance.Crashing = () => _current?.ReleaseForCrash()),
            session => RunHost(configuration, options, session));
    }

    /// <summary>The WinUI host. <see cref="InstanceStart"/> calls it once this process owns its pipe.</summary>
    private static void RunHost(ShellConfiguration configuration, ShellOptions options, InstanceSession session)
    {
        var instance = session.Instance;
        var log = session.Log;
        var identity = session.Identity;
        var arguments = session.Arguments;
        Exception? failed = null;
        ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            var application = new ShellApplication(() =>
            {
                // Thrown from here it would cross into WinUI, which ends the process with nothing reported.
                try
                {
                    var loop = new DispatcherLoop(queue, log);
                    loop.Failed += e => instance.Reported(instance.Crashes.WriteOnce("event loop", e, "Winshell",
                        "Something in Winshell failed. It is still running."));
                    var shell = new Shell(loop, log, options, identity, arguments);
                    shell.LogPath = instance.LogPath;
                    _current = shell;
                    var notices = new ShellNotices(
                        loop,
                        log,
                        instance.Crashes,
                        (notice, act, dismiss) => new NoticeWindow(notice, act, dismiss, loop),
                        path => OpenToRead.Open(path, log),
                        ShellNotices.WindowsIsQuiet);
                    shell.Notices = notices;
                    // Under winshell dev, a file that does not read says so on screen. Normally the log has it.
                    if (Environment.GetEnvironmentVariable("DOTNET_WATCH") == "1")
                        TomlFile.ErrorChanged += notices.ShowConfigError;
                    if (Environment.GetEnvironmentVariable(DevProjectVariable) is { Length: > 0 } project)
                        XamlReload.Watch(shell, project);
                    session.Serve(loop, new Control(shell));
                    // winshell.exe is a console program: Ctrl+C would end it with no Dispose, so it quits instead.
                    Console.CancelKeyPress += (_, e) =>
                    {
                        e.Cancel = true;
                        loop.Post(shell.Quit);
                    };
                    configuration.Start(shell);
                    ShellInstance.Quietly(() =>
                        notices.ShowPrevious(ShellInstance.ExitState, ShellNotices.ReadCrashTimeout()));
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    instance.ReportFatal(e);
                    failed = e;
                    // Windows go first: WinUI crashes on the way out with any still open.
                    queue.TryEnqueue(() =>
                    {
                        ShellInstance.Quietly(() => _current?.Dispose());
                        Application.Current.Exit();
                    });
                }
            });
            application.UnhandledException += (_, e) =>
            {
                ShellInstance.Quietly(() => instance.Crashing?.Invoke());
                instance.ReportFatal(e.Exception);
            };
            GC.KeepAlive(application);
        });
        if (failed is not null)
            session.Failure = failed;
    }

    /// <summary>
    /// Replaces this instance with a new process of the same program, which takes over once this one has gone.
    /// False when that process did not start: the shell then keeps running.
    /// </summary>
    public bool Reload()
    {
        if (StartNext() is not null)
            return false;
        Quit();
        return true;
    }

    /// <summary>
    /// Starts the process that takes over from this one, once. <paramref name="program"/> is that process when a build
    /// wrote it beside this one; null starts this program again. Null when it started, or else why not.
    /// </summary>
    private string? StartNext(string? program = null)
    {
        if (_nextStarted)
            return null;
        program ??= Environment.ProcessPath!;
        var info = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in _arguments.ForNext(Environment.ProcessId))
            info.ArgumentList.Add(argument);
        try
        {
            using var next = Process.Start(info);
            _nextStarted = true;
            Log.Info("shell", "Reloading: a new process takes over from this one.");
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            var error = $"Could not start \"{program}\": {e.Message}";
            Log.Error("shell", error);
            return error;
        }
    }

    /// <summary>Ends the shell. Its windows close and <see cref="Run{T}"/> returns.</summary>
    public void Quit()
    {
        Dispose();
        Application.Current.Exit();
    }

    /// <summary>
    /// Gives back what a crash would leave held: Explorer's taskbar and every panel's app bar. A crash skips
    /// <see cref="Dispose"/> and can be on any thread, so this uses only calls that are safe from any thread.
    /// </summary>
    private void ReleaseForCrash()
    {
        _explorerTaskbar?.Restore();
        PanelEdge.ReleaseAll();
    }

    /// <summary>
    /// Closes Winshell's notices, stops the services, and closes every window. <see cref="Quit"/> does this and also
    /// ends the shell.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Notices?.Dispose();
        DisposeServices();
        Platform.Dispose();
    }

    /// <summary>The control pipe's way into this shell. A reload ends the process, so that waits for the client's answer.</summary>
    private sealed class Control(Shell shell) : IInstanceControl
    {
        public ShellIpc Ipc => shell.Ipc;

        public string? Reload(string? program)
        {
            return shell.StartNext(program);
        }

        public void Reloaded()
        {
            shell.Quit();
        }

        public void Quit()
        {
            shell.Quit();
        }

        public void ShowBuildError(string error, string projectFile)
        {
            shell.Notices?.ShowBuildError(error, projectFile);
        }

        public void DismissNotice()
        {
            shell.Notices?.Hide();
        }

        public Task<string> Inspect(string command, IReadOnlyList<string> args)
        {
            return Inspector.Run(shell, command, args);
        }
    }
}
