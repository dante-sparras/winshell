namespace Winshell.Runtime;

/// <summary>
/// The names one instance of a component can see: the ids in it, context properties such as a delegate's
/// <c>index</c>, its root object, and the enclosing component's context.
/// </summary>
public sealed class ComponentContext
{
    private readonly Dictionary<string, QObject> _ids = new(StringComparer.Ordinal);
    private Dictionary<string, object?>? _properties;
    private readonly QObject _notifier = new();

    public ComponentContext(ComponentContext? parent, ImportScope? imports)
    {
        Parent = parent;
        Imports = imports ?? parent?.Imports;
    }

    public ComponentContext? Parent { get; }

    public ImportScope? Imports { get; }

    /// <summary>The root object of the component instance, whose members are in scope everywhere in it.</summary>
    public QObject? ContextObject { get; set; }

    public IReadOnlyDictionary<string, QObject> Ids => _ids;

    /// <summary>The copies of the scripts this component instance imports that are not libraries, by path.</summary>
    internal Dictionary<string, Jint.Native.JsValue>? Scripts { get; set; }

    public void SetId(string id, QObject obj) => _ids[id] = obj;

    public bool TryGetId(string id, out QObject obj) => _ids.TryGetValue(id, out obj!);

    /// <summary>Sets a name the instance can see. Returns whether its value changed.</summary>
    public bool SetContextProperty(string name, object? value)
    {
        _properties ??= new(StringComparer.Ordinal);
        if (_properties.TryGetValue(name, out var old) && ValueConverter.AreEqual(old, value))
            return false;
        _properties[name] = value;
        _notifier.NotifyChanged(name);
        return true;
    }

    /// <summary>Whether this context has the name. Unlike <see cref="TryGetContextProperty"/>, this does not read it.</summary>
    public bool HasContextProperty(string name) => _properties is not null && _properties.ContainsKey(name);

    public bool TryGetContextProperty(string name, out object? value)
    {
        if (_properties is not null && _properties.TryGetValue(name, out value))
        {
            DependencyTracker.Read(_notifier, name);
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Resolves an id here or in an enclosing context.</summary>
    public QObject? FindId(string id)
    {
        for (var ctx = this; ctx is not null; ctx = ctx.Parent)
        {
            if (ctx._ids.TryGetValue(id, out var obj))
                return obj;
        }
        return null;
    }
}
