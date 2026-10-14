using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Winshell;

/// <summary>Process entry. <c>--watch</c> waits for this process. <c>--give-back</c> returns the taskbar.</summary>
public static class Program
{
    private static Mutex? _shell;

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            ConfigFolder.Use(ConfigFolder.Resolve(args));
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 2;
            return;
        }

        Log.Use(new HostLog(Path.Combine(ConfigFolder.Path, "winshell.log")));
        Log.Information("Startup", args.Length == 0 ? "process started" : "process started " + string.Join(' ', args));

        // No window, and no claim on the process. A shell that was killed could not put the taskbar back.
        if (args is ["--give-back"])
        {
            ShellWin32.GiveBack();
            GiveExtensionsBack();
            return;
        }

        if (ShellWatch.IsWatch(args))
        {
            ShellWatch.Run(args);
            return;
        }

        if (!TakeShell())
        {
            Log.Information("Startup", "another shell holds Local\\Winshell");
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => ShellWin32.Release();
        RunShell();
    }

    /// <summary>
    /// The shell's application. Never inlined: <see cref="Main"/> is prepared with every assembly it names,
    /// and the watcher and a give-back return before this runs, so neither maps WinUI.
    /// An extension may fill the surfaces. The application is this one either way.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunShell()
    {
        // Before the extension loads. Its context would otherwise load a second WinRT.Runtime, and that copy
        // registers ComWrappers again, which throws when the extension first touches the shell's application.
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var boot = LoadBoot();
        // The shell's word is not Winland's. A window manager that is already up is left as it is.
        WinlandStart.Ensure();
        LifetimeFile.Write(ShellLife.Running);
        ShellApplication.Run(boot.Config, boot.Extensions);
    }

    private static bool TakeShell()
    {
        try
        {
            // Abandoned is a shell that died holding the name. This one takes it.
            _shell = new Mutex(false, @"Local\Winshell");
            return _shell.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Startup", "could not take Local\\Winshell", ex);
            return false;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The shell host is not native code. Extensions are named in settings.json.")]
    private static Boot LoadBoot()
    {
        ShellConfig config;
        try
        {
            config = ShellConfig.Read(Path.Combine(ConfigFolder.Path, ConfigFolder.SettingsFile));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            Log.Error("Config", "settings could not be read", ex);
            config = ShellConfig.Parse("{}");
        }

        foreach (var note in config.Notes)
        {
            Log.Information("Config", note);
        }

        var loaded = ExtensionLoader.Load(config.Extensions, AppContext.BaseDirectory);
        foreach (var note in loaded.Notes)
        {
            Log.Warning("Extension", note);
        }

        foreach (var extension in loaded.Extensions)
        {
            Log.Information("Extension", "loaded " + extension.Name);
        }

        ShellPaths.PluginRoot = ProductFolders.BuiltInPlugins(AppContext.BaseDirectory, loaded.Folders);

        return new Boot(config, loaded.Extensions);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The shell host is not native code. Extensions are named in settings.json.")]
    internal static void GiveExtensionsBack()
    {
        foreach (var extension in LoadBoot().Extensions)
        {
            try
            {
                extension.GiveBack();
            }
            catch (Exception ex)
            {
                Log.Error("Extension", extension.Name + " did not give back", ex);
            }
        }
    }

    private readonly record struct Boot(ShellConfig Config, IReadOnlyList<IShellExtension> Extensions);
}
