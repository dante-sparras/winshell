using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Winshell.Runtime;

public static class Names
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>The QML name of a C# member: its first letter lowered.</summary>
    public static string ToQml(string csName) =>
        Cache.GetOrAdd(csName, static n => n.Length == 0 || char.IsLower(n[0]) ? n : char.ToLowerInvariant(n[0]) + n[1..]);

    /// <summary>The handler name of a signal: <c>clicked</c> becomes <c>onClicked</c>.</summary>
    public static string HandlerName(string signal) =>
        signal.Length == 0 ? "on" : "on" + char.ToUpperInvariant(signal[0]) + signal[1..];

    /// <summary>The signal a handler name refers to, or null when the name is not a handler name.</summary>
    public static string? SignalOfHandler(string handler)
    {
        if (handler.Length < 3 || !handler.StartsWith("on", StringComparison.Ordinal) || !char.IsUpper(handler[2]))
            return null;
        // onXChanged for a property "x" or "X": QML lowers the first letter of the signal.
        return char.ToLowerInvariant(handler[2]) + handler[3..];
    }
}

public sealed record SignalDefinition(string Name, IReadOnlyList<string> Parameters);

/// <summary>A C# property exposed to QML.</summary>
public sealed class MetaProperty
{
    private readonly Func<QObject, object?> _get;
    private readonly Action<QObject, object?>? _set;

    internal MetaProperty(string name, PropertyInfo info)
    {
        Name = name;
        Info = info;
        Type = info.PropertyType;
        var target = Expression.Parameter(typeof(QObject), "o");
        var cast = Expression.Convert(target, info.DeclaringType!);
        _get = Expression.Lambda<Func<QObject, object?>>(Expression.Convert(Expression.Property(cast, info), typeof(object)), target).Compile();
        if (info.SetMethod is { IsPublic: true })
        {
            var value = Expression.Parameter(typeof(object), "v");
            _set = Expression.Lambda<Action<QObject, object?>>(
                Expression.Assign(Expression.Property(cast, info), Expression.Convert(value, Type)), target, value).Compile();
        }
    }

    public string Name { get; }

    public PropertyInfo Info { get; }

    public Type Type { get; }

    public bool CanWrite => _set is not null;

    public bool IsList => typeof(IQmlList).IsAssignableFrom(Type);

    public object? Get(QObject target) => _get(target);

    public void Set(QObject target, object? value)
    {
        if (_set is null)
            throw new QmlException($"Cannot assign to read-only property \"{Name}\"");
        _set(target, ValueConverter.Convert(value, Type, target.Engine));
    }
}

public sealed class MetaMethod(string name, IReadOnlyList<MethodInfo> overloads)
{
    private readonly Overload[] _overloads = overloads.Select(static m =>
    {
        var parameters = m.GetParameters();
        var required = 0;
        foreach (var parameter in parameters)
        {
            if (!parameter.HasDefaultValue && !parameter.IsDefined(typeof(ParamArrayAttribute)))
                required++;
        }
        var variadic = parameters.Length > 0 && parameters[^1].IsDefined(typeof(ParamArrayAttribute));
        return new Overload(m, parameters, required, variadic);
    }).ToArray();

    public string Name { get; } = name;

    public IReadOnlyList<MethodInfo> Overloads { get; } = overloads;

    public object? Invoke(QObject target, object?[] args)
    {
        var overload = Pick(args.Length);
        var parameters = overload.Parameters;
        var converted = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (p.ParameterType == typeof(object?[]) && p.IsDefined(typeof(ParamArrayAttribute)))
            {
                converted[i] = args.Skip(i).ToArray();
                break;
            }
            converted[i] = i < args.Length
                ? ValueConverter.Convert(args[i], p.ParameterType, target.Engine)
                : p.HasDefaultValue ? p.DefaultValue : ValueConverter.DefaultOf(p.ParameterType);
        }
        try
        {
            return overload.Method.Invoke(target, converted);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            return null;
        }
    }

    private Overload Pick(int argCount)
    {
        Overload? best = null;
        foreach (var overload in _overloads)
        {
            if (argCount >= overload.Required && (overload.Variadic || argCount <= overload.Parameters.Length))
            {
                if (overload.Parameters.Length == argCount)
                    return overload;
                best ??= overload;
            }
        }
        return best ?? _overloads[0];
    }

    private readonly record struct Overload(MethodInfo Method, ParameterInfo[] Parameters, int Required, bool Variadic);
}

/// <summary>What QML can see of a C# type: its properties, methods, signals, enums and default property.</summary>
public sealed class MetaType
{
    private static readonly ConcurrentDictionary<Type, MetaType> Cache = new();

    private MetaType(Type type)
    {
        Type = type;
        var properties = new Dictionary<string, MetaProperty>(StringComparer.Ordinal);
        var methods = new Dictionary<string, List<MethodInfo>>(StringComparer.Ordinal);
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = p.GetCustomAttribute<QmlAttribute>(true);
            if (attr is null || p.GetIndexParameters().Length > 0)
                continue;
            var name = attr.Name ?? Names.ToQml(p.Name);
            // A derived type that re-declares a property (with "new") wins over the base.
            if (properties.TryGetValue(name, out var existing) && existing.Info.DeclaringType!.IsAssignableFrom(p.DeclaringType!) is false)
                continue;
            properties[name] = new MetaProperty(name, p);
        }
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = m.GetCustomAttribute<QmlAttribute>(true);
            if (attr is null)
                continue;
            var name = attr.Name ?? Names.ToQml(m.Name);
            if (!methods.TryGetValue(name, out var list))
                methods[name] = list = [];
            list.Add(m);
        }
        Properties = properties;
        Methods = methods.ToDictionary(kv => kv.Key, kv => new MetaMethod(kv.Key, kv.Value), StringComparer.Ordinal);

        var signals = new Dictionary<string, SignalDefinition>(StringComparer.Ordinal);
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var s in t.GetCustomAttributes<QmlSignalAttribute>(false))
                signals.TryAdd(s.Name, new SignalDefinition(s.Name, s.Parameters));
        }
        Signals = signals;

        DefaultProperty = type.GetCustomAttribute<QmlDefaultPropertyAttribute>(true)?.Name;

        var enums = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var nested in t.GetNestedTypes(BindingFlags.Public))
            {
                if (!nested.IsEnum || nested.GetCustomAttribute<QmlEnumAttribute>() is null)
                    continue;
                foreach (var value in Enum.GetValues(nested))
                    enums.TryAdd(Enum.GetName(nested, value)!, ValueConverter.EnumToInt((Enum)value));
            }
        }
        Enums = enums;
    }

    public static MetaType Of(Type type) => Cache.GetOrAdd(type, static t => new MetaType(t));

    public Type Type { get; }

    public IReadOnlyDictionary<string, MetaProperty> Properties { get; }

    public IReadOnlyDictionary<string, MetaMethod> Methods { get; }

    public IReadOnlyDictionary<string, SignalDefinition> Signals { get; }

    public IReadOnlyDictionary<string, int> Enums { get; }

    public string? DefaultProperty { get; }
}
