using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;
using Winshell.Updates;

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

    /// <summary>
    /// The updater <c>winshell.exe</c> passes when it runs this shell in-process. Installing a download stays in
    /// <c>winshell.exe</c>, before the shell starts. Restart then starts this program again with <c>--after</c>.
    /// A configuration's own program leaves this null.
    /// </summary>
    internal Updater? Updater { get; init; }
}

/// <summary>A configuration: the program that defines a shell. <see cref="Start"/> makes its windows.</summary>
public abstract class ShellConfiguration
{
    /// <summary>Runs once, on the shell's thread, after WinUI has started.</summary>
    public abstract void Start(Shell shell);
}

/// <summary>The running shell: its screens and services. There is one in a process.</summary>
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
    public static Shell Current => _current ?? throw new InvalidOperationException("The shell has not started. Call Shell.Run first.");

    public IEventLoop Loop { get; }

    public Log Log { get; }

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

    /// <summary>Whether <paramref name="scheme"/> is dark, with Windows in <paramref name="windowsDark"/>.</summary>
    internal static bool DarkFor(ColorScheme scheme, bool windowsDark) => scheme switch
    {
        ColorScheme.Dark => true,
        ColorScheme.Light => false,
        _ => windowsDark,
    };

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
    public string ConfigPath(string path) => Path.Combine(ConfigDir, path);

    /// <summary>A path in <see cref="DataDir"/>.</summary>
    public string DataPath(string path) => Path.Combine(DataDir, path);

    /// <summary>A path in <see cref="StateDir"/>.</summary>
    public string StatePath(string path) => Path.Combine(StateDir, path);

    /// <summary>A path in <see cref="CacheDir"/>.</summary>
    public string CachePath(string path) => Path.Combine(CacheDir, path);

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
    /// start keeps <c>--after</c>.
    /// </summary>
    public static void Run<T>(ShellOptions? options = null)
        where T : ShellConfiguration, new() =>
        Run(new T(), options ?? new ShellOptions());

    private static void Run(ShellConfiguration configuration, ShellOptions options)
    {
        var arguments = ShellArguments.Parse(Environment.GetCommandLineArgs().Skip(1));
        var program = Environment.ProcessPath!;
        // The parent of -d returns at once. It must not hold the mutex, or the child looks like a duplicate of it.
        // The child keeps --after and waits itself, so a reload's handoff still lets the old process go first.
        if (arguments.Daemonize)
        {
            try
            {
                using var child = Process.Start(arguments.DetachedStart(program));
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                StartupError.Report($"Could not start \"{program}\": {e.Message}", null);
                Environment.ExitCode = 1;
            }
            return;
        }
        // A reload: the shell before this one lets go of the taskbar, the hotkeys and the tray first.
        if (arguments.After is { } previous)
            Instances.WaitForExit(previous);
        var identity = ShellIdentity.Resolve(program, ConfigPaths.Root(), options.ShellId, options.DataDir, options.StateDir, options.CacheDir);
        // Held until this method returns, which is the life of the shell. A second --no-duplicate start then exits.
        using var duplicate = arguments.NoDuplicate
            ? DuplicateHold.TryClaim(DuplicateHold.ShellMutexName(identity.ShellId), i => string.Equals(i.ShellId, identity.ShellId, StringComparison.OrdinalIgnoreCase))
            : default;
        if (arguments.NoDuplicate && !duplicate.Acquired)
            return;

        var log = Log.Default;
        var instance = ShellInstance.Open(program, log, arguments.Verbose);
        instance.Crashes.ShellId = identity.ShellId;
        log.Info("shell", $"Winshell {ProductVersion.Current.Text} on Windows {Environment.OSVersion.Version}, program \"{program}\"");
        log.Info("shell", $"Shell ID: \"{identity.ShellId}\"");
        log.Info("shell", $"Saving logs to \"{instance.LogPath}\"");
        if (!instance.TryOpenPipe())
        {
            instance.Abandon();
            StartupError.Report("The control pipe is already taken.", null);
            Environment.ExitCode = 1;
            return;
        }

        Exception? failed = null;
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
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
                        loop.Failed += e => instance.Reported(instance.Crashes.WriteOnce("event loop", e, subject: "Winshell", recovery: "Something in Winshell failed. It is still running."));
                        var shell = new Shell(loop, log, options, identity, arguments);
                        shell.LogPath = instance.LogPath;
                        shell.Updater = options.Updater;
                        if (options.Updater is { } updater)
                            updater.Restart = shell.RestartForUpdate;
                        _current = shell;
                        var notices = new ShellNotices(
                            loop,
                            log,
                            instance.Crashes,
                            (notice, act, dismiss) => new NoticeWindow(notice, act, dismiss, loop),
                            path => OpenToRead.Open(path, log));
                        shell.Notices = notices;
                        // Under winshell dev, a file that does not read says so on screen. Normally the log has it.
                        if (Environment.GetEnvironmentVariable("DOTNET_WATCH") == "1")
                            TomlFile.ErrorChanged += notices.ShowConfigError;
                        if (Environment.GetEnvironmentVariable(DevProjectVariable) is { Length: > 0 } project)
                            XamlReload.Watch(shell, project);
                        instance.Listen(loop, new Control(shell), notices.Hide);
                        instance.Publish(identity.ShellId);
                        configuration.Start(shell);
                        ShellInstance.Quietly(() => notices.ShowPrevious(ShellInstance.ExitState, ShellNotices.ReadCrashTimeout()));
#if DEBUG
                        if (Environment.GetEnvironmentVariable(ShellInstance.TestCrashVariable) is { Length: > 0 })
                            throw new InvalidOperationException($"{ShellInstance.TestCrashVariable} asked Winshell to crash as it started.");
#endif
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
                application.UnhandledException += (_, e) => instance.ReportFatal(e.Exception);
                GC.KeepAlive(application);
            });
        }
        catch (Exception e) when (instance.ReportFatal(e))
        {
            throw;
        }
        finally
        {
            instance.MarkCleanExit();
            instance.Close();
        }
        if (failed is not null)
        {
            StartupError.Report(failed.Message, null);
            Environment.ExitCode = 1;
        }
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
    /// Starts this program again with <c>--after</c>, as <see cref="Reload"/> does, once no other Winshell is running.
    /// The start is posted, so a caller on the shell's thread is not torn down while it is still running.
    /// </summary>
    private string? RestartForUpdate()
    {
        var another = Instances.List(false).Any(i => i.Info.Pid != Environment.ProcessId);
        if (UpdateRestart.Decide(_arguments, Environment.ProcessId, another, out _) is { } refused)
            return refused;
        Loop.Post(() =>
        {
            if (StartNext() is null)
                Quit();
        });
        return null;
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
        public IIpcTargets Ipc => shell.Ipc;

        public string? Reload(bool hard, string? program) => shell.StartNext(program);

        public void Reloaded() => shell.Quit();

        public void Quit() => shell.Quit();

        public void ShowBuildError(string error, string projectFile) =>
            shell.Notices?.Show(ShellNotices.ForBuildError(error, projectFile));

        public Task<string> Inspect(string command, IReadOnlyList<string> args) => Inspector.Run(shell, command, args);
    }

    /// <summary>
    /// The WinUI application every shell runs in. A configuration has no App.xaml, so this finds the XAML type
    /// information its assemblies were compiled with, which a custom control in XAML is looked up in.
    /// </summary>
    private sealed class ShellApplication(Action started) : Application, IXamlMetadataProvider
    {
        private readonly List<IXamlMetadataProvider> _providers = [new XamlControlsXamlMetaDataProvider(), .. CompiledProviders()];

        public IXamlType GetXamlType(Type type) => _providers.Select(p => p.GetXamlType(type)).FirstOrDefault(t => t is not null)!;

        public IXamlType GetXamlType(string fullName) => _providers.Select(p => p.GetXamlType(fullName)).FirstOrDefault(t => t is not null)!;

        public XmlnsDefinition[] GetXmlnsDefinitions() => [.. _providers.SelectMany(p => p.GetXmlnsDefinitions())];

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Resources.MergedDictionaries.Add(new XamlControlsResources());
            started();
        }

        private static IEnumerable<IXamlMetadataProvider> CompiledProviders()
        {
            if (Assembly.GetEntryAssembly() is not { } entry)
                yield break;
            var assemblies = entry.GetReferencedAssemblies()
                .Where(a => a.Name is { } name && !name.StartsWith("Microsoft.", StringComparison.Ordinal) && !name.StartsWith("System", StringComparison.Ordinal))
                .Select(Assembly.Load)
                .Prepend(entry);
            foreach (var assembly in assemblies)
            {
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type.Name == "XamlMetaDataProvider" && typeof(IXamlMetadataProvider).IsAssignableFrom(type) && !type.IsAbstract)
                        yield return (IXamlMetadataProvider)Activator.CreateInstance(type)!;
                }
            }
        }
    }
}
