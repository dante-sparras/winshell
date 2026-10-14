using System.Diagnostics.CodeAnalysis;
using ShellKit = Winshell.Kit.Kit;
using System.Reflection;
using System.Runtime.Loader;

namespace Winshell;

/// <summary>Creates a plugin from its manifest. A sheet is drawn here. An assembly is loaded from the plugin's folder.</summary>
internal static class PluginMaker
{
    [RequiresUnreferencedCode("Plugins are loaded from manifests on disk. The shell host is not native code.")]
    public static IWinomaPlugin? Create(PluginManifest manifest)
    {
        if (manifest.IsSheet)
        {
            return new SheetPanel(manifest);
        }

        if (string.IsNullOrWhiteSpace(manifest.Entry))
        {
            Log.Warning("Plugin", manifest.Id + " is missing entry");
            return null;
        }

        var type = Load(manifest);
        if (type is null)
        {
            return null;
        }

        if (!typeof(IWinomaPlugin).IsAssignableFrom(type))
        {
            Log.Warning("Plugin", manifest.Id + " entry " + manifest.Entry + " is not a plugin");
            return null;
        }

        try
        {
            return Activator.CreateInstance(type) as IWinomaPlugin;
        }
        catch (Exception ex)
        {
            Log.Warning("Plugin", manifest.Id + " did not construct", ex);
            return null;
        }
    }

    [RequiresUnreferencedCode("Plugins are loaded from manifests on disk. The shell host is not native code.")]
    private static Type? Load(PluginManifest manifest)
    {
        var path = AssemblyPath(manifest);
        if (path is null)
        {
            Log.Warning("Plugin", manifest.Id + " assembly is missing");
            return null;
        }

        try
        {
            var assembly = Open(path);
            var type = assembly.GetType(manifest.Entry);
            if (type is null)
            {
                Log.Warning("Plugin", manifest.Id + " is missing " + manifest.Entry);
            }

            return type;
        }
        catch (Exception ex)
        {
            Log.Warning("Plugin", manifest.Id + " did not load: " + ex.Message, ex);
            return null;
        }
    }

    private static string? AssemblyPath(PluginManifest manifest)
    {
        var file = string.IsNullOrWhiteSpace(manifest.Assembly) ? "" : Path.GetFileName(manifest.Assembly);
        if (file.Length == 0)
        {
            return null;
        }

        var beside = Path.Combine(Path.GetDirectoryName(manifest.Path) ?? "", file);
        if (File.Exists(beside))
        {
            return beside;
        }

        var shipped = Path.Combine(ShellPaths.PluginAssemblies, file);
        return File.Exists(shipped) ? shipped : null;
    }

    [RequiresUnreferencedCode("Plugins are loaded from manifests on disk. The shell host is not native code.")]
    private static Assembly Open(string path)
    {
        var wanted = Path.GetFullPath(path);
        foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (loaded.Location.Length > 0
                && string.Equals(Path.GetFullPath(loaded.Location), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return loaded;
            }
        }

        if (PluginAssembly.SharesShellContext(wanted, AppContext.BaseDirectory))
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(wanted);
        }

        var context = new PluginLoadContext(wanted);
        return context.LoadFromAssemblyPath(wanted);
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string path)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(path);
        }

        [RequiresUnreferencedCode("Plugins are loaded from manifests on disk. The shell host is not native code.")]
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is "Winshell.Core" or "Winshell.Kit" or "Winland.Ipc")
            {
                return Shared(assemblyName.Name);
            }

            foreach (var loaded in Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.Ordinal))
                {
                    return loaded;
                }
            }

            if (assemblyName.Name is { Length: > 0 } name && File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll")))
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }

            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolved is null ? null : LoadFromAssemblyPath(resolved);
        }

        private static Assembly Shared(string name) => name switch
        {
            "Winshell.Kit" => typeof(ShellKit).Assembly,
            "Winland.Ipc" => typeof(WinlandClient).Assembly,
            _ => typeof(IWinomaPlugin).Assembly,
        };
    }
}
