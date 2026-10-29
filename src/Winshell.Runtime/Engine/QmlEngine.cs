using System.Security.Cryptography;
using System.Text;
using Jint.Native;
using Winshell.Qml;

namespace Winshell.Runtime;

/// <summary>
/// Loads QML files and creates objects from them. One engine is one generation of a shell: a reload makes a
/// new engine and destroys the old one.
/// </summary>
public sealed class QmlEngine : IDisposable
{
    private readonly Dictionary<string, QmlDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, QmlDocument> _inlineDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<QmlDocument, ImportScope> _imports = [];
    private readonly Dictionary<string, Dictionary<string, string>> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, QObject> _fileSingletons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TypeEntry, QObject> _nativeSingletons = [];
    private readonly Dictionary<string, JsValue> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _scriptSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadingScripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _scriptImportChain = [];
    private readonly HashSet<string> _creatingSingletons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _instantiatingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<QObject> _roots = [];
    private readonly Stack<CreationBatch> _batches = new();
    private int _creationDepth;

    /// <summary>
    /// How many <see cref="Create"/> calls may be on the stack at once, including completion. Past this, creation
    /// throws instead of overflowing the stack. File cycles are caught separately, by path.
    /// </summary>
    internal const int MaxCreationDepth = 64;
    private readonly Dictionary<Type, object> _services = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SignalSlot, object?> _connectedSlots = [];

    public QmlEngine(ModuleRegistry modules, IEventLoop loop, Log? log = null, string? shellDir = null)
    {
        Modules = modules;
        Loop = loop;
        Log = log ?? Log.Default;
        ShellDir = shellDir is null ? null : Path.GetFullPath(shellDir);
        Js = new JsRuntime(this);
    }

    public ModuleRegistry Modules { get; }

    public IEventLoop Loop { get; }

    public Log Log { get; }

    public JsRuntime Js { get; }

    /// <summary>The folder <c>shell.qml</c> is in. <c>import config.foo</c> is relative to it.</summary>
    public string? ShellDir { get; }

    /// <summary>Folders searched for a module that is not built in, such as <c>import Foo.Bar</c>.</summary>
    public List<string> ImportPaths { get; } = [];

    /// <summary>Every file this engine read, so a change to any of them can reload the shell.</summary>
    public IReadOnlyCollection<string> LoadedFiles => _documents.Keys;

    public bool IsCreating => _batches.Count > 0;

    /// <summary>Whether the shell finished its reload: objects made from now on have nothing old to match.</summary>
    public bool ReloadFinished { get; private set; }

    public event Action? ReloadFinishedEvent;

    /// <summary>Ends the reload stage, telling every Reloadable that has not reloaded yet.</summary>
    public void FinishReload()
    {
        if (ReloadFinished)
            return;
        ReloadFinished = true;
        ReloadFinishedEvent?.Invoke();
    }

    public bool IsDisposed { get; private set; }

    // Services shared by the objects of this engine, such as the platform.

    public void AddService<T>(T service) where T : class => _services[typeof(T)] = service;

    public T? GetService<T>() where T : class => _services.TryGetValue(typeof(T), out var s) ? (T)s : null;

    public T RequireService<T>() where T : class =>
        GetService<T>() ?? throw new QmlException($"{typeof(T).Name} is not available");

    // Documents

    public QmlDocument LoadDocument(string path)
    {
        path = Path.GetFullPath(path);
        if (_documents.TryGetValue(path, out var doc))
            return doc;
        var text = Preprocessor.Run(File.ReadAllText(path));
        doc = QmlParser.Parse(text, path);
        _documents[path] = doc;
        return doc;
    }

    /// <summary>Parses QML from a string, as if it were a file at <paramref name="path"/>.</summary>
    public QmlDocument LoadDocumentFromText(string text, string path)
    {
        path = Path.GetFullPath(path);
        var doc = QmlParser.Parse(Preprocessor.Run(text), path);
        _documents[path] = doc;
        return doc;
    }

    /// <summary>
    /// Parses QML text for <c>Qt.createQmlObject</c>. The same text is parsed once. It is not a loaded file, so
    /// the watcher does not follow a path that was never written.
    /// </summary>
    internal QmlDocument LoadInline(string text, string directory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var path = Path.GetFullPath(Path.Combine(directory, "__inline_" + hash + ".qml"));
        if (_inlineDocuments.TryGetValue(path, out var cached))
            return cached;
        var doc = QmlParser.Parse(Preprocessor.Run(text), path);
        _inlineDocuments[path] = doc;
        return doc;
    }

    public ImportScope ImportsOf(QmlDocument doc)
    {
        if (!_imports.TryGetValue(doc, out var scope))
            _imports[doc] = scope = new ImportScope(this, doc);
        return scope;
    }

    /// <summary>The file defining type <paramref name="name"/> in a directory, or null.</summary>
    public string? FindDirectoryType(string directory, string name)
    {
        if (!_directories.TryGetValue(directory, out var types))
        {
            types = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.qml"))
                {
                    var typeName = Path.GetFileNameWithoutExtension(file);
                    if (typeName.Length > 0 && char.IsUpper(typeName[0]))
                        types[typeName] = file;
                }
                var qmldir = Path.Combine(directory, "qmldir");
                if (File.Exists(qmldir))
                    ReadQmldir(qmldir, directory, types);
            }
            _directories[directory] = types;
        }
        return types.TryGetValue(name, out var path) ? path : null;
    }

    private void ReadQmldir(string qmldir, string directory, Dictionary<string, string> types)
    {
        var lines = File.ReadAllLines(qmldir);
        for (var i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0].StartsWith('#'))
                continue;
            // "singleton Name 1.0 File.qml", "internal Name File.qml" and "Name 1.0 File.qml" declare types.
            var named = parts[0] is "singleton" or "internal" ? parts[1..] : parts;
            if (named.Length >= 2 && char.IsUpper(named[0][0]) && named[^1].EndsWith(".qml", StringComparison.Ordinal))
                types[named[0]] = Path.Combine(directory, named[^1]);
            else if (!IsQmldirCommand(parts[0]))
                Log.Warn("scene", $"{qmldir}:{i + 1}: qmldir line is not understood and is ignored: {lines[i].Trim()}");
        }
    }

    /// <summary>qmldir commands that say nothing about the types in a folder.</summary>
    private static bool IsQmldirCommand(string word) => word is "module" or "depends" or "import" or "optional"
        or "default" or "plugin" or "classname" or "typeinfo" or "designersupported" or "static" or "system"
        or "prefer" or "linktarget";

    public string? ResolveModuleDirectory(string uri)
    {
        var relative = uri.Replace('.', Path.DirectorySeparatorChar);
        foreach (var root in ImportPaths)
        {
            var dir = Path.Combine(root, relative);
            if (Directory.Exists(dir))
                return dir;
        }
        return null;
    }

    public bool IsSingletonFile(string path) => LoadDocument(path).IsSingleton;

    // Singletons

    public QObject GetFileSingleton(string path)
    {
        path = Path.GetFullPath(path);
        if (_fileSingletons.TryGetValue(path, out var instance))
            return instance;
        if (!_creatingSingletons.Add(path))
            throw new QmlException($"Singleton {Path.GetFileNameWithoutExtension(path)} depends on itself while it is created");
        try
        {
            instance = CreateFromDocument(LoadDocument(path), null, null);
            _fileSingletons[path] = instance;
            return instance;
        }
        finally
        {
            _creatingSingletons.Remove(path);
        }
    }

    public QObject GetNativeSingleton(TypeEntry entry)
    {
        if (_nativeSingletons.TryGetValue(entry, out var instance))
            return instance;
        instance = entry.Singleton!(this);
        instance.Engine = this;
        _nativeSingletons[entry] = instance;
        return instance;
    }

    /// <summary>The singleton of a native type by its C# type, when one was created.</summary>
    public T? FindNativeSingleton<T>() where T : QObject => _nativeSingletons.Values.OfType<T>().FirstOrDefault();

    // Scripts

    /// <summary>
    /// Loads a JavaScript file imported with <c>import "file.js" as Name</c>. A <c>.pragma library</c> script is
    /// shared by the whole shell; any other script is copied for each component instance that imports it, the one
    /// <paramref name="context"/> belongs to.
    /// </summary>
    public JsValue LoadScript(string path, ComponentContext? context = null)
    {
        path = Path.GetFullPath(path);
        if (_scripts.TryGetValue(path, out var module))
            return module;
        var instance = context;
        while (instance?.Parent is { } parent && parent.Imports == instance.Imports)
            instance = parent;
        if (instance?.Scripts is { } own && own.TryGetValue(path, out module))
            return module;
        // The module is stored only after its imports load. A file that imports itself, directly or through
        // another file, would otherwise recurse here until the process dies with a stack overflow.
        if (!_loadingScripts.Add(path))
        {
            var chain = string.Join(" -> ", _scriptImportChain.Append(path));
            throw new QmlException($"{path} imports itself through {chain}");
        }
        _scriptImportChain.Add(path);
        try
        {
            if (!_scriptSources.TryGetValue(path, out var code))
                _scriptSources[path] = code = File.ReadAllText(path);
            // A library is shared and has no component scope. Any other script is evaluated in this instance.
            var scriptContext = ScriptModule.IsLibrary(code) ? null : context;
            module = ScriptModule.Load(this, code, path, scriptContext);
            if (instance is null || scriptContext is null)
                _scripts[path] = module;
            else
                (instance.Scripts ??= new(StringComparer.OrdinalIgnoreCase))[path] = module;
            return module;
        }
        finally
        {
            _loadingScripts.Remove(path);
            _scriptImportChain.RemoveAt(_scriptImportChain.Count - 1);
        }
    }

    /// <summary>Remembers a signal this engine's scripts connected to, to disconnect them when it is disposed.</summary>
    internal void ConnectedTo(SignalSlot slot) => _connectedSlots.TryAdd(slot, null);

    // Creation

    /// <summary>Creates the root object of a file, such as <c>shell.qml</c>.</summary>
    public QObject CreateRoot(string path) => CreateFromDocument(LoadDocument(path), null, null);

    public QObject CreateFromDocument(QmlDocument doc, QObject? owner, IReadOnlyDictionary<string, object?>? initialProperties)
    {
        var context = new ComponentContext(null, ImportsOf(doc));
        return Create(doc.Root, context, doc, owner, initialProperties, isFileRoot: true);
    }

    /// <summary>
    /// Marks a QML file as being instantiated. A file already in the set is a cycle
    /// (<c>Foo.qml</c> containing <c>Foo {}</c>, or <c>A</c> creating <c>B</c> creating <c>A</c>).
    /// </summary>
    internal void BeginFileInstance(string path, SourceLocation location)
    {
        if (!_instantiatingFiles.Add(Path.GetFullPath(path)))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            throw new QmlCompileException($"{name} is instantiated recursively", location);
        }
    }

    internal void EndFileInstance(string path) => _instantiatingFiles.Remove(Path.GetFullPath(path));

    internal QObject Create(
        QmlObjectNode node,
        ComponentContext context,
        QmlDocument doc,
        QObject? owner,
        IReadOnlyDictionary<string, object?>? initialProperties,
        bool isFileRoot,
        Action<QObject>? beforeCompletion = null)
    {
        // Completion runs after the batch is popped, and Loader, Repeater and createObject nest there.
        // Counting batches alone would miss that recursion.
        if (_creationDepth >= MaxCreationDepth)
            throw new QmlException("Object creation is nested too deeply");
        _creationDepth++;
        try
        {
            var batch = new CreationBatch();
            _batches.Push(batch);
            QObject root;
            try
            {
                var builder = new ObjectBuilder(this, batch);
                root = builder.Build(node, context, doc, owner);
                if (initialProperties is not null)
                    builder.SetInitialProperties(root, context, initialProperties);
                beforeCompletion?.Invoke(root);
            }
#pragma warning disable CA1031 // A failed creation must not leave the objects it already built.
            catch (Exception)
#pragma warning restore CA1031
            {
                for (var i = batch.Objects.Count - 1; i >= 0; i--)
                {
                    if (!batch.Objects[i].IsDestroyed)
                        batch.Objects[i].Destroy();
                }
                throw;
            }
            finally
            {
                _batches.Pop();
            }
            batch.Complete(this);
            // A parent destroys what it owns. An object created with no parent would otherwise survive reload.
            if (owner is null && !_creatingSingletons.Contains(doc.File))
                _roots.Add(root);
            return root;
        }
        finally
        {
            _creationDepth--;
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        foreach (var root in _roots.ToArray())
            root.Destroy();
        foreach (var singleton in _fileSingletons.Values)
            singleton.Destroy();
        foreach (var singleton in _nativeSingletons.Values)
            singleton.Destroy();
        _roots.Clear();
        _fileSingletons.Clear();
        _nativeSingletons.Clear();
        // Objects that outlive this generation must not keep its functions, nor call them.
        foreach (var (slot, _) in _connectedSlots)
            slot.DisconnectRuntime(Js);
        _connectedSlots.Clear();
    }
}

/// <summary>The bindings and completions of one component being created, run once every object exists.</summary>
internal sealed class CreationBatch
{
    public List<Binding> Bindings { get; } = [];

    public List<QObject> Objects { get; } = [];

    public List<Action> AfterBindings { get; } = [];

    public List<DynamicProperty> Required { get; } = [];

    public void Complete(QmlEngine engine)
    {
        foreach (var binding in Bindings)
        {
            if (!binding.HasEvaluated)
                binding.Evaluate();
        }
        foreach (var required in Required)
        {
            if (!required.WasAssigned && !required.Owner.IsDestroyed)
                engine.Log.Warn("scene", $"{required.Owner.TypeName}: Required property {required.Name} was not initialized");
        }
        foreach (var action in AfterBindings)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                engine.Log.ScriptError(e, "component");
            }
        }
        foreach (var obj in Objects)
        {
            if (obj.IsDestroyed)
                continue;
            try
            {
                obj.MarkComplete();
            }
            catch (Exception e)
            {
                engine.Log.Error("scene", Log.Describe(e, obj.TypeName));
                engine.Log.Debug("scene", e.ToString());
            }
        }
        foreach (var obj in Objects)
        {
            if (!obj.IsDestroyed && obj.FindAttached(typeof(ComponentAttached)) is ComponentAttached attached)
                attached.Emit("completed");
        }
    }
}
