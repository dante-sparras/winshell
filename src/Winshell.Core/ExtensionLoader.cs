using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace Winshell.Core;

/// <summary>Loads the extensions named in <see cref="ExtensionList"/> and constructs each public one.</summary>
public static class ExtensionLoader
{
    /// <summary>The extensions that constructed, and a note for each path or type that did not.</summary>
    /// <param name="Extensions">One instance per constructed type, in path order.</param>
    /// <param name="Notes">One line for each path or type that was skipped.</param>
    /// <param name="Folders">The folder of each extension that constructed, once each, in path order.</param>
    public readonly record struct LoadResult(IReadOnlyList<IShellExtension> Extensions, IReadOnlyList<string> Notes, IReadOnlyList<string> Folders);

    /// <summary>
    /// Loads each path and constructs every public, non-abstract <see cref="IShellExtension"/> that has a public parameterless constructor.
    /// A path that is not rooted is read beside <paramref name="baseDirectory"/>. A missing file is a note, not a failure.
    /// A file that is not an assembly, a type whose constructor throws, and a type with no public constructor are notes.
    /// A second extension that fills the surfaces is still returned, and noted: the shell opens the first.
    /// </summary>
    /// <param name="paths">DLL paths from <see cref="ExtensionList.Read"/>.</param>
    /// <param name="baseDirectory">The program's folder. Relative paths are read beside it.</param>
    /// <returns>The extensions that constructed, and the notes.</returns>
    [RequiresUnreferencedCode("Extensions are loaded from paths in settings.json. The shell host is not native code.")]
    public static LoadResult Load(IEnumerable<string> paths, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ShareLoadedAssemblies();
        var notes = new List<string>();
        var found = new List<IShellExtension>();
        var folders = new List<string>();
        var fillers = 0;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                notes.Add("extension path is empty");
                continue;
            }

            if (!TryFull(path, baseDirectory, out var full, out var problem))
            {
                notes.Add(problem);
                continue;
            }

            if (!File.Exists(full))
            {
                notes.Add("missing extension " + full);
                continue;
            }

            Assembly assembly;
            try
            {
                assembly = Open(full);
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                notes.Add("extension is not a shell extension " + full + ": " + ex.Message);
                continue;
            }

            foreach (var type in TypesOf(assembly, full, notes))
            {
                if (type is not { IsClass: true, IsAbstract: false, IsPublic: true })
                {
                    continue;
                }

                if (!typeof(IShellExtension).IsAssignableFrom(type))
                {
                    continue;
                }

                if (type.GetConstructor(Type.EmptyTypes) is null)
                {
                    notes.Add("extension " + type.FullName + " has no public constructor");
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(type) is not IShellExtension extension)
                    {
                        notes.Add("extension " + (type.FullName ?? type.Name) + " did not construct");
                        continue;
                    }

                    if (extension.FillsSurfaces)
                    {
                        fillers++;
                        if (fillers > 1)
                        {
                            notes.Add("second desktop " + extension.Name + "; the shell opens the first");
                        }
                    }

                    found.Add(extension);
                    if (Path.GetDirectoryName(full) is { Length: > 0 } folder && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    {
                        folders.Add(folder);
                    }
                }
                catch (Exception ex)
                {
                    var error = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
                    notes.Add("extension " + (type.FullName ?? type.Name) + ": " + error.Message);
                }
            }
        }

        return new LoadResult(found, notes, folders);
    }

    // Kept for the process life. A context that is collected takes its extension with it.
    private static readonly List<ExtensionLoadContext> Alive = [];
    private static int _shared;

    /// <summary>
    /// Teaches the default load context to use an assembly this process already loaded.
    /// An extension loads into its own context. A plugin the desktop then loads into the default context,
    /// such as dictation, still binds <c>Winoma.Core</c> and the other assemblies that extension brought in.
    /// A second call does nothing.
    /// </summary>
    public static void ShareLoadedAssemblies()
    {
        if (Interlocked.Exchange(ref _shared, 1) != 0)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (_, name) => ResolveLoaded(name);
    }

    /// <summary>An assembly already in the process with <paramref name="name"/>'s simple name, or null.</summary>
    /// <param name="name">The assembly the default context could not find on its own.</param>
    /// <returns>The loaded assembly, or null when the process has none by that name.</returns>
    public static Assembly? ResolveLoaded(AssemblyName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(loaded.GetName().Name, name.Name, StringComparison.Ordinal))
            {
                return loaded;
            }
        }

        return null;
    }
    private static readonly Dictionary<string, Assembly> Opened = new(StringComparer.OrdinalIgnoreCase);

    [RequiresUnreferencedCode("Extensions are loaded from paths in settings.json. The shell host is not native code.")]
    private static Assembly Open(string full)
    {
        var wanted = Path.GetFullPath(full);
        if (Opened.TryGetValue(wanted, out var prior))
        {
            return prior;
        }

        // An assembly the host already loaded is that assembly, matched by the file's name.
        // A single-file host has no assembly path to compare, so the name is the check.
        var name = Path.GetFileNameWithoutExtension(wanted);
        foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Opened[wanted] = loaded;
                return loaded;
            }
        }

        var context = new ExtensionLoadContext(wanted);
        Alive.Add(context);
        var assembly = context.LoadFromAssemblyPath(wanted);
        Opened[wanted] = assembly;
        return assembly;
    }

    private static bool TryFull(string path, string baseDirectory, out string full, out string problem)
    {
        try
        {
            full = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(baseDirectory, path));
            problem = "";
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            full = "";
            problem = "extension path " + path + ": " + ex.Message;
            return false;
        }
    }

    [RequiresUnreferencedCode("Extensions are loaded from paths in settings.json. The shell host is not native code.")]
    private static Type[] TypesOf(Assembly assembly, string full, List<string> notes)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            notes.Add("extension types " + full + ": " + ex.Message);
            return ex.Types.OfType<Type>().ToArray();
        }
    }

    /// <summary>
    /// Loads one extension and the assemblies beside it. An assembly the host already loaded is shared,
    /// so the extension's <see cref="IShellExtension"/> is the host's type and not a second copy.
    /// </summary>
    internal sealed class ExtensionLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _directory;

        public ExtensionLoadContext(string path)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(path);
            _directory = Path.GetDirectoryName(path) ?? "";
        }

        /// <summary>Loads <paramref name="assemblyName"/> the way an extension reference does.</summary>
        /// <param name="assemblyName">The assembly an extension reference named.</param>
        /// <returns>The shell's copy, the extension's copy, or null when neither has it.</returns>
        internal Assembly? LoadNamed(AssemblyName assemblyName) => Load(assemblyName);

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Extensions are loaded from paths in settings.json. The shell host is not native code.")]
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            foreach (var loaded in Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.Ordinal))
                {
                    return loaded;
                }
            }

            // WinUI events take TypedEventHandler from Microsoft.Windows.SDK.NET, and WinRT.Runtime
            // registers ComWrappers. A second copy of either makes the desktop fail to open:
            // the event method looks missing, or ComWrappers is registered again.
            // An assembly the shell cannot load, such as NAudio, stays in this context and is resolved below.
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
            }

            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is not null)
            {
                return LoadFromAssemblyPath(resolved);
            }

            if (assemblyName.Name is { Length: > 0 } name)
            {
                var beside = Path.Combine(_directory, name + ".dll");
                if (File.Exists(beside))
                {
                    return LoadFromAssemblyPath(beside);
                }
            }

            return null;
        }
    }
}
