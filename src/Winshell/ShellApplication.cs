using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Winshell;

/// <summary>Starts the shell's WinUI loop. An extension fills the surfaces inside this application, or the shell opens its own desk.</summary>
internal static class ShellApplication
{
    public static void Run(ShellConfig config, IReadOnlyList<IShellExtension> extensions)
    {
        ShellApp.Prepare(config, extensions);
        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new ShellApp();
        });
    }
}

/// <summary>The shell's application. It has no XAML of its own beyond the resources a WinUI control needs.</summary>
public sealed partial class ShellApp : Application
{
    private static ShellConfig? _config;
    private static IReadOnlyList<IShellExtension>? _extensions;

    public ShellApp()
    {
        InitializeComponent();
    }

    internal static void Prepare(ShellConfig config, IReadOnlyList<IShellExtension> extensions)
    {
        _config = config;
        _extensions = extensions;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var config = _config ?? ShellConfig.Parse("{}");
        var extensions = _extensions ?? [];
        var filler = ShellExtensions.Filler(extensions);
        if (filler is not null)
        {
            OpenFilled(config, extensions, filler);
            return;
        }

        try
        {
            _ = new ShellDesk(config, extensions);
        }
        catch (Exception ex)
        {
            Log.Error("Startup", "the shell did not open", ex);
            ShellWin32.Release();
            Current.Exit();
        }
    }

    private static void OpenFilled(ShellConfig config, IReadOnlyList<IShellExtension> extensions, IShellExtension filler)
    {
        var host = new QuietHost(config);
        foreach (var extension in extensions)
        {
            if (ReferenceEquals(extension, filler))
            {
                continue;
            }

            try
            {
                extension.Start(host);
            }
            catch (Exception ex)
            {
                Log.Error("Extension", extension.Name + " did not start", ex);
            }
        }

        try
        {
            filler.OpenDesktop(host);
            Log.Information("Startup", "desktop " + filler.Name);
        }
        catch (Exception ex)
        {
            Log.Error("Startup", filler.Name + " did not open", ex);
            ShellWin32.Release();
            Current.Exit();
        }
    }

    /// <summary>
    /// A host for an extension that fills the surfaces. The desktop answers summon and dismiss itself.
    /// </summary>
    private sealed class QuietHost(ShellConfig config) : IShellHost
    {
        public string BaseDirectory => AppContext.BaseDirectory;

        public string ConfigDirectory => ConfigFolder.Path;

        public ShellConfig Config { get; } = config;

        public void OnUi(Action work) => work();

        public string Summon(string pluginId, string? command) => "the extension fills the desktop";

        public string Dismiss() => "the extension fills the desktop";

        public void RunPlugin(string pluginId, string command, bool down)
        {
        }

        public void UseServices(IPluginServiceSource? source)
        {
        }

        public void SetIndicator(string owner, PluginIndicator? indicator)
        {
        }

        public void ShowNotice(ToastFace notice)
        {
        }

        public void SetFacts(string extensionName, IReadOnlyDictionary<string, object?>? facts)
        {
        }

        public string? OpenPanelId => null;
    }
}
