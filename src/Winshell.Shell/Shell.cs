using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace Winshell;

/// <summary>What a configuration sets before its shell starts.</summary>
public sealed record ShellOptions
{
    /// <summary>
    /// The name that identifies this configuration across runs. It names its data, state and cache folders. Without
    /// it, a program in the config folder is named after its folder, and one anywhere else after its path.
    /// </summary>
    public string? ShellId { get; init; }

    /// <summary>The app id the shell shows Windows, as <c>WINSHELL_APP_ID</c> does.</summary>
    public string? AppId { get; init; }

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

/// <summary>The running shell: its screens and services. There is one in a process.</summary>
public sealed class Shell : IDisposable
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

    /// <summary>The shell of this process. Reading it before <see cref="Run{T}"/> has started it throws.</summary>
    public static Shell Current => _current ?? throw new InvalidOperationException("The shell has not started. Call Shell.Run first.");

    public IEventLoop Loop { get; }

    public Log Log { get; }

    public ShellOptions Options { get; }

    internal ShellPlatform Platform { get; }

    /// <summary>The name that identifies this configuration across runs.</summary>
    public string ShellId => _identity.ShellId;

    /// <summary>What <c>winshell ipc</c> reaches in this shell.</summary>
    public ShellIpc Ipc { get; }

    /// <summary>The folder of this configuration's program, where its own files are.</summary>
    public string ConfigDir => Path.GetDirectoryName(Environment.ProcessPath)!;

    /// <summary>This shell's data folder, which exists once read.</summary>
    public string DataDir => Ensure(_identity.DataDir);

    /// <summary>This shell's state folder, which exists once read.</summary>
    public string StateDir => Ensure(_identity.StateDir);

    /// <summary>This shell's cache folder, which exists once read.</summary>
    public string CacheDir => Ensure(_identity.CacheDir);

    /// <summary>The connected screens. The primary screen comes first.</summary>
    public ObservableCollection<ScreenInfo> Screens => Platform.Screens;

    /// <summary>Whether Windows mode is dark. Windows follow it on their own.</summary>
    public bool IsDark => Platform.IsDark;

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
    /// takes <c>--no-duplicate</c> on its command line, and exits at once when its shell id is already running.
    /// </summary>
    public static void Run<T>(ShellOptions? options = null)
        where T : ShellConfiguration, new() =>
        Run(new T(), options ?? new ShellOptions());

    private static void Run(ShellConfiguration configuration, ShellOptions options)
    {
        var arguments = ShellArguments.Parse(Environment.GetCommandLineArgs().Skip(1));
        // A reload: the shell before this one lets go of the taskbar, the hotkeys and the tray first.
        if (arguments.After is { } previous)
            Instances.WaitForExit(previous);
        var program = Environment.ProcessPath!;
        var identity = ShellIdentity.Resolve(program, ConfigPaths.Root(), options.ShellId, options.DataDir, options.StateDir, options.CacheDir);
        // Held until this method returns, which is the life of the shell. A second --no-duplicate start then exits.
        using var duplicate = arguments.NoDuplicate
            ? DuplicateHold.TryClaim(DuplicateHold.ShellMutexName(identity.ShellId), i => string.Equals(i.ShellId, identity.ShellId, StringComparison.OrdinalIgnoreCase))
            : default;
        if (arguments.NoDuplicate && !duplicate.Acquired)
            return;

        var log = Log.Default;
        var instance = ShellInstance.Open(program, log);
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
                        _current = shell;
                        instance.Listen(loop, new Control(shell));
                        instance.Publish(identity.ShellId);
                        configuration.Start(shell);
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
    /// Replaces this instance with a new process of the same program, which takes over once this one has gone. Until
    /// the CLI builds a reload, that is all it is. False when that process did not start: the shell then keeps running.
    /// </summary>
    public bool Reload()
    {
        if (StartNext() is not null)
            return false;
        Quit();
        return true;
    }

    /// <summary>Starts the process that takes over from this one, once. Null when it started, or else why not.</summary>
    private string? StartNext()
    {
        if (_nextStarted)
            return null;
        var program = Environment.ProcessPath!;
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
        Platform.Dispose();
    }

    /// <summary>The control pipe's way into this shell. A reload ends the process, so that waits for the client's answer.</summary>
    private sealed class Control(Shell shell) : IInstanceControl
    {
        public IIpcTargets Ipc => shell.Ipc;

        public string? Reload(bool hard) => shell.StartNext();

        public void Reloaded() => shell.Quit();

        public void Quit() => shell.Quit();
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
