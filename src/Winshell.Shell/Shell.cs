using System.Collections.ObjectModel;
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
    /// <summary>The name that identifies this configuration across runs. It names its data, state and cache folders.</summary>
    public string? ShellId { get; init; }

    /// <summary>The app id the shell shows Windows, as <c>WINSHELL_APP_ID</c> does.</summary>
    public string? AppId { get; init; }

    public string? DataDir { get; init; }

    public string? StateDir { get; init; }

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

    private bool _disposed;

    internal Shell(IEventLoop loop, Log log, ShellOptions options)
    {
        Loop = loop;
        Log = log;
        Options = options;
        Platform = new ShellPlatform(loop, log);
    }

    /// <summary>The shell of this process. Reading it before <see cref="Run{T}"/> has started it throws.</summary>
    public static Shell Current => _current ?? throw new InvalidOperationException("The shell has not started. Call Shell.Run first.");

    public IEventLoop Loop { get; }

    public Log Log { get; }

    public ShellOptions Options { get; }

    internal ShellPlatform Platform { get; }

    /// <summary>The connected screens. The primary screen comes first.</summary>
    public ObservableCollection<ScreenInfo> Screens => Platform.Screens;

    /// <summary>Whether Windows mode is dark. Windows follow it on their own.</summary>
    public bool IsDark => Platform.IsDark;

    /// <summary>Runs a configuration until the shell quits, on this thread. Call it from <c>Main</c>, marked <c>[STAThread]</c>.</summary>
    public static void Run<T>(ShellOptions? options = null)
        where T : ShellConfiguration, new() =>
        Run(new T(), options ?? new ShellOptions());

    private static void Run(ShellConfiguration configuration, ShellOptions options)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            var log = Log.Default;
            var application = new ShellApplication(() =>
            {
                _current = new Shell(new DispatcherLoop(queue, log), log, options);
                configuration.Start(_current);
            });
            GC.KeepAlive(application);
        });
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
