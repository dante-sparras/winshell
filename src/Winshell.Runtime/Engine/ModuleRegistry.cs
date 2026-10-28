namespace Winshell.Runtime;

/// <summary>One type a module exports to QML.</summary>
public sealed class TypeEntry
{
    public required string Name { get; init; }

    public required string Module { get; init; }

    /// <summary>The C# type. For an enum-only type, the enum.</summary>
    public required Type ClrType { get; init; }

    public bool Creatable { get; init; } = true;

    /// <summary>Why the type cannot be created, for the error message.</summary>
    public string? UncreatableReason { get; init; }

    /// <summary>Makes the one instance of a singleton type, once per engine.</summary>
    public Func<QmlEngine, QObject>? Singleton { get; init; }

    /// <summary>Makes the attached object of this type for an object: <c>Layout.fillWidth</c>.</summary>
    public Func<QObject, QObject>? Attached { get; init; }

    public Type? AttachedType { get; init; }

    public IReadOnlyDictionary<string, int> Enums { get; init; } = new Dictionary<string, int>();

    /// <summary>The <c>toString(value)</c> of an enum type, such as <c>UPowerDeviceState.toString(state)</c>.</summary>
    public Func<int, string>? EnumToString { get; init; }
}

public sealed class QmlModule(string name)
{
    private readonly Dictionary<string, TypeEntry> _types = new(StringComparer.Ordinal);

    public string Name { get; } = name;

    public IReadOnlyDictionary<string, TypeEntry> Types => _types;

    public QmlModule Add(TypeEntry entry)
    {
        _types[entry.Name] = entry;
        return this;
    }

    /// <summary>A creatable type: <c>Rectangle {}</c>.</summary>
    public QmlModule Type<T>(string? name = null, Func<QObject, QObject>? attached = null, Type? attachedType = null)
        where T : QObject, new()
    {
        var qmlName = name ?? typeof(T).Name;
        MetaType.Of(typeof(T)).QmlName = qmlName;
        return Add(new TypeEntry
        {
            Name = qmlName,
            Module = Name,
            ClrType = typeof(T),
            Attached = attached,
            AttachedType = attachedType,
            Enums = MetaType.Of(typeof(T)).Enums,
        });
    }

    /// <summary>A type that only provides enums, attached objects or a base type, and cannot be created.</summary>
    public QmlModule Uncreatable<T>(string? name = null, string? reason = null, Func<QObject, QObject>? attached = null, Type? attachedType = null)
    {
        var qmlName = name ?? typeof(T).Name;
        if (typeof(QObject).IsAssignableFrom(typeof(T)))
            MetaType.Of(typeof(T)).QmlName = qmlName;
        var enums = typeof(T).IsEnum
            ? Enum.GetValues(typeof(T)).Cast<object>().ToDictionary(v => Enum.GetName(typeof(T), v)!, v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture))
            : typeof(QObject).IsAssignableFrom(typeof(T)) ? MetaType.Of(typeof(T)).Enums : new Dictionary<string, int>();
        return Add(new TypeEntry
        {
            Name = qmlName,
            Module = Name,
            ClrType = typeof(T),
            Creatable = false,
            UncreatableReason = reason,
            Attached = attached,
            AttachedType = attachedType,
            Enums = enums,
            EnumToString = typeof(T).IsEnum ? v => Enum.GetName(typeof(T), v) ?? "" : null,
        });
    }

    /// <summary>A singleton: <c>Quickshell.screens</c>.</summary>
    public QmlModule Singleton<T>(Func<QmlEngine, T> create, string? name = null)
        where T : QObject
    {
        var qmlName = name ?? typeof(T).Name;
        MetaType.Of(typeof(T)).QmlName = qmlName;
        return Add(new TypeEntry
        {
            Name = qmlName,
            Module = Name,
            ClrType = typeof(T),
            Creatable = false,
            Singleton = e => create(e),
            Enums = MetaType.Of(typeof(T)).Enums,
        });
    }
}

public sealed class ModuleRegistry
{
    private readonly Dictionary<string, QmlModule> _modules = new(StringComparer.Ordinal);

    public QmlModule Module(string name)
    {
        if (!_modules.TryGetValue(name, out var module))
            _modules[name] = module = new QmlModule(name);
        return module;
    }

    public bool TryGetModule(string name, out QmlModule module) => _modules.TryGetValue(name, out module!);

    public IEnumerable<QmlModule> All => _modules.Values;
}
