using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Winshell.Qml;

namespace Winshell.Runtime;

/// <summary>What a type name in a document refers to.</summary>
public abstract record TypeRef(string Name);

public sealed record NativeTypeRef(TypeEntry Entry) : TypeRef(Entry.Name);

public sealed record FileTypeRef(string Name, string Path) : TypeRef(Name);

public sealed record InlineTypeRef(string Name, QmlInlineComponent Component, QmlDocument Document) : TypeRef(Name);

/// <summary>The imports of one document, and the lookup of type names through them.</summary>
public sealed class ImportScope
{
    private readonly QmlEngine _engine;
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, QmlInlineComponent> _inline = new(StringComparer.Ordinal);

    private abstract record Entry(string? Qualifier);

    private sealed record ModuleEntry(string? Qualifier, QmlModule Module) : Entry(Qualifier);

    private sealed record DirectoryEntry(string? Qualifier, string Path) : Entry(Qualifier);

    private sealed record ScriptEntry(string Qualifier, string Path) : Entry(Qualifier);

    public ImportScope(QmlEngine engine, QmlDocument document)
    {
        _engine = engine;
        Document = document;
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(document.File)) ?? ".";
        Directory = dir;
        foreach (var import in document.Imports)
        {
            switch (import.Kind)
            {
                case ImportKind.Module when import.Uri == "config" || import.Uri.StartsWith("config.", StringComparison.Ordinal):
                    {
                        var shellDir = engine.ShellDir ?? dir;
                        var relative = import.Uri.Length > 6 ? import.Uri[7..].Replace('.', System.IO.Path.DirectorySeparatorChar) : "";
                        _entries.Add(new DirectoryEntry(import.Qualifier, System.IO.Path.Combine(shellDir, relative)));
                        break;
                    }
                case ImportKind.Module:
                    if (!engine.Modules.TryGetModule(import.Uri, out var module))
                    {
                        // A directory on the import path can stand in for a module.
                        var asDir = engine.ResolveModuleDirectory(import.Uri);
                        if (asDir is null)
                            throw new QmlCompileException($"module \"{import.Uri}\" is not installed", import.Location);
                        _entries.Add(new DirectoryEntry(import.Qualifier, asDir));
                        break;
                    }
                    _entries.Add(new ModuleEntry(import.Qualifier, module));
                    break;
                case ImportKind.Directory:
                    {
                        var path = import.Uri.StartsWith("root:", StringComparison.Ordinal)
                            ? System.IO.Path.Combine(engine.ShellDir ?? dir, import.Uri[5..].TrimStart('/'))
                            : System.IO.Path.Combine(dir, import.Uri);
                        _entries.Add(new DirectoryEntry(import.Qualifier, System.IO.Path.GetFullPath(path)));
                        break;
                    }
                case ImportKind.Script:
                    {
                        var path = import.Uri.StartsWith("root:", StringComparison.Ordinal)
                            ? System.IO.Path.Combine(engine.ShellDir ?? dir, import.Uri[5..].TrimStart('/'))
                            : System.IO.Path.Combine(dir, import.Uri);
                        _entries.Add(new ScriptEntry(import.Qualifier!, System.IO.Path.GetFullPath(path)));
                        break;
                    }
            }
        }
        foreach (var inline in document.Root.Members.OfType<QmlInlineComponent>())
            _inline[inline.Name] = inline;
        CollectInline(document.Root);
    }

    private void CollectInline(QmlObjectNode node)
    {
        foreach (var member in node.Members)
        {
            switch (member)
            {
                case QmlInlineComponent inline:
                    _inline.TryAdd(inline.Name, inline);
                    CollectInline(inline.Object);
                    break;
                case QmlChildObject child:
                    CollectInline(child.Object);
                    break;
                case QmlAssignment { Value: QmlObjectValue ov }:
                    CollectInline(ov.Object);
                    break;
            }
        }
    }

    public QmlDocument Document { get; }

    public string Directory { get; }

    public TypeRef? ResolveType(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            var qualifier = name[..dot];
            var typeName = name[(dot + 1)..];
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Qualifier == qualifier && Find(_entries[i], typeName) is { } found)
                    return found;
            }
            // FileType.InlineType from another file.
            if (ResolveType(qualifier) is FileTypeRef file)
            {
                var doc = _engine.LoadDocument(file.Path);
                var inline = doc.Root.Members.OfType<QmlInlineComponent>().FirstOrDefault(c => c.Name == typeName);
                if (inline is not null)
                    return new InlineTypeRef(typeName, inline, doc);
            }
            return null;
        }
        if (_inline.TryGetValue(name, out var local))
            return new InlineTypeRef(name, local, Document);
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Qualifier is null && Find(_entries[i], name) is { } found)
                return found;
        }
        if (_engine.FindDirectoryType(Directory, name) is { } implicitType)
            return new FileTypeRef(name, implicitType);
        return null;
    }

    private TypeRef? Find(Entry entry, string name) => entry switch
    {
        ModuleEntry m when m.Module.Types.TryGetValue(name, out var t) => new NativeTypeRef(t),
        DirectoryEntry d when _engine.FindDirectoryType(d.Path, name) is { } path => new FileTypeRef(name, path),
        _ => null,
    };

    /// <summary>
    /// Resolves an uppercase name used in a script: a type, singleton, namespace or script import. A script import
    /// that is not a library is the copy of the component instance <paramref name="context"/> belongs to.
    /// </summary>
    public bool TryResolveName(string name, QObject? scopeObject, ComponentContext? context, out JsValue value)
    {
        var js = _engine.Js;
        foreach (var entry in _entries)
        {
            if (entry.Qualifier != name)
                continue;
            value = entry is ScriptEntry script
                ? _engine.LoadScript(script.Path, context)
                : new NamespaceObject(js, this, name, scopeObject);
            return true;
        }
        var type = ResolveType(name);
        if (type is null)
        {
            value = JsValue.Undefined;
            return false;
        }
        value = TypeValue(type, scopeObject);
        return true;
    }

    /// <summary>
    /// Whether a script name is a type, a namespace, or a script import. Unlike <see cref="TryResolveName"/>, this
    /// does not load the script or the file.
    /// </summary>
    internal bool DeclaresName(string name)
    {
        if (name.Length == 0)
            return false;
        foreach (var entry in _entries)
        {
            if (entry.Qualifier == name)
                return true;
        }
        if (_inline.ContainsKey(name))
            return true;
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Qualifier is null && DeclaresType(_entries[i], name))
                return true;
        }
        return _engine.FindDirectoryType(Directory, name) is not null;
    }

    private bool DeclaresType(Entry entry, string name) => entry switch
    {
        ModuleEntry module => module.Module.Types.ContainsKey(name),
        DirectoryEntry directory => _engine.FindDirectoryType(directory.Path, name) is not null,
        _ => false,
    };

    internal JsValue TypeValue(TypeRef type, QObject? scopeObject)
    {
        var js = _engine.Js;
        switch (type)
        {
            case NativeTypeRef { Entry.Singleton: not null } singleton:
                return js.Wrap(_engine.GetNativeSingleton(singleton.Entry));
            case NativeTypeRef native:
                return new TypeObject(js, native.Entry, scopeObject);
            case FileTypeRef file when _engine.IsSingletonFile(file.Path):
                return js.Wrap(_engine.GetFileSingleton(file.Path));
            case FileTypeRef file:
                return new FileTypeObject(js, _engine.LoadDocument(file.Path));
            default:
                return JsValue.Undefined;
        }
    }
}

/// <summary>A type name in a script: its enums, its attached object, or nothing.</summary>
public sealed class TypeObject : ObjectInstance
{
    private readonly JsRuntime _runtime;
    private readonly QObject? _scopeObject;

    internal TypeObject(JsRuntime runtime, TypeEntry entry, QObject? scopeObject)
        : base(runtime.Engine)
    {
        _runtime = runtime;
        Entry = entry;
        _scopeObject = scopeObject;
    }

    public TypeEntry Entry { get; }

    private QObject? Attached =>
        Entry.Attached is not null && _scopeObject is not null
            ? _scopeObject.GetAttached(Entry.AttachedType ?? Entry.ClrType, Entry.Attached)
            : null;

    public override JsValue Get(JsValue property, JsValue receiver)
    {
        if (!property.IsString())
            return base.Get(property, receiver);
        var name = property.AsString();
        if (Entry.Enums.TryGetValue(name, out var value))
            return value;
        if (name == "toString" && Entry.EnumToString is { } toString)
            return new Jint.Runtime.Interop.ClrFunction(Engine, name, (_, args) => toString(args.Length > 0 ? (int)TypeConverter.ToNumber(args[0]) : 0));
        if (Attached is { } attached)
        {
            var wrapper = (QObjectWrapper)_runtime.Wrap(attached);
            if (wrapper.TryGetMember(name, out var member))
                return member;
        }
        return base.Get(property, receiver);
    }

    public override bool Set(JsValue property, JsValue value, JsValue receiver)
    {
        if (property.IsString() && Attached is { } attached && attached.HasProperty(property.AsString()))
        {
            _runtime.AssignProperty(attached, property.AsString(), value);
            return true;
        }
        return false;
    }

    public override bool HasProperty(JsValue property) =>
        property.IsString() && (Entry.Enums.ContainsKey(property.AsString()) || (Attached?.HasMember(property.AsString()) ?? false));

    public override string ToString() => Entry.Name;
}

/// <summary>A QML file type in a script, for the enums it declares: <c>Type.Value</c> or <c>Type.Enum.Value</c>.</summary>
public sealed class FileTypeObject : ObjectInstance
{
    private readonly Dictionary<string, JsValue> _members = new(StringComparer.Ordinal);

    internal FileTypeObject(JsRuntime runtime, QmlDocument document)
        : base(runtime.Engine)
    {
        foreach (var e in document.Root.Members.OfType<QmlEnumDeclaration>())
        {
            var scoped = new JsObject(runtime.Engine);
            foreach (var (k, v) in e.Values)
            {
                _members[k] = v;
                scoped.Set(k, v);
            }
            _members[e.Name] = scoped;
        }
    }

    public override JsValue Get(JsValue property, JsValue receiver) =>
        property.IsString() && _members.TryGetValue(property.AsString(), out var v) ? v : base.Get(property, receiver);
}

/// <summary>An import qualifier in a script: <c>WS.Shell</c>.</summary>
public sealed class NamespaceObject : ObjectInstance
{
    private readonly ImportScope _scope;
    private readonly string _qualifier;
    private readonly QObject? _scopeObject;

    internal NamespaceObject(JsRuntime runtime, ImportScope scope, string qualifier, QObject? scopeObject)
        : base(runtime.Engine)
    {
        _scope = scope;
        _qualifier = qualifier;
        _scopeObject = scopeObject;
    }

    public override JsValue Get(JsValue property, JsValue receiver)
    {
        if (property.IsString() && _scope.ResolveType(_qualifier + "." + property.AsString()) is { } type)
            return _scope.TypeValue(type, _scopeObject);
        return base.Get(property, receiver);
    }
}

public sealed class QmlCompileException(string message, SourceLocation location) : Exception($"{location}: {message}")
{
    public SourceLocation Location { get; } = location;
}
