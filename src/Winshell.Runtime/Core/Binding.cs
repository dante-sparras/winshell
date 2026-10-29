namespace Winshell.Runtime;

/// <summary>Records which properties are read while a binding is evaluated.</summary>
public static class DependencyTracker
{
    [ThreadStatic]
    private static List<(QObject Object, string Property)>? _current;

    public static bool IsTracking => _current is not null;

    public static void Read(QObject obj, string property) => _current?.Add((obj, property));

    /// <summary>Starts recording into <paramref name="sink"/>. Disposing the result restores the previous recorder.</summary>
    public static Scope Begin(List<(QObject, string)>? sink)
    {
        var previous = _current;
        _current = sink;
        return new Scope(previous);
    }

    /// <summary>Stops recording until disposed, for reads that must not become dependencies.</summary>
    public static Scope Suspend() => Begin(null);

    public readonly struct Scope(List<(QObject, string)>? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }
}

/// <summary>
/// Keeps one property equal to the value of an expression. The expression is evaluated again, synchronously,
/// whenever a property it read changes.
/// </summary>
public sealed class Binding : IPropertyObserver, IDisposable
{
    private readonly Func<object?> _evaluate;
    private readonly HashSet<(QObject, string)> _dependencies = [];
    private readonly List<(QObject Object, string Property)> _reads = [];
    private bool _evaluating;
    private bool _disposed;

    public Binding(QObject target, string property, Func<object?> evaluate, string description)
    {
        Target = target;
        Property = property;
        _evaluate = evaluate;
        Description = description;
    }

    public QObject Target { get; }

    public string Property { get; }

    /// <summary>Where the binding was written, for messages.</summary>
    public string Description { get; }

    public bool HasEvaluated { get; private set; }

    public void Evaluate()
    {
        if (_disposed || Target.IsDestroyed || Target.IsDestroying)
            return;
        if (_evaluating)
        {
            (Target.Engine?.Log ?? Log.Default).Warn("scene", $"{Description}: QML {Target.TypeName}: Binding loop detected for property \"{Property}\"");
            return;
        }
        _evaluating = true;
        try
        {
            _reads.Clear();
            object? value;
            bool ok;
            using (DependencyTracker.Begin(_reads))
            {
                try
                {
                    using (Target.Engine?.Js.EnterScope(Target))
                        value = _evaluate();
                    ok = true;
                }
                catch (Exception e)
                {
                    (Target.Engine?.Log ?? Log.Default).ScriptError(e, Description);
                    value = null;
                    ok = false;
                }
            }
            Resubscribe(_reads);
            HasEvaluated = true;
            if (!ok || _disposed)
                return;
            try
            {
                Target.WriteProperty(Property, value);
            }
            catch (Exception e)
            {
                (Target.Engine?.Log ?? Log.Default).Warn("scene", $"{Description}: {e.Message}");
            }
        }
        finally
        {
            _evaluating = false;
        }
    }

    private void Resubscribe(List<(QObject Object, string Property)> reads)
    {
        if (_disposed)
        {
            reads.Clear();
            return;
        }
        reads.RemoveAll(static read => read.Object.IsDestroyed || read.Object.IsDestroying);
        for (var i = 1; i < reads.Count; i++)
        {
            if (!ContainsRead(reads, reads[i], i))
                continue;
            reads.RemoveAt(i);
            i--;
        }
        foreach (var old in _dependencies)
        {
            if (!ContainsRead(reads, old, reads.Count))
                old.Item1.RemoveObserver(old.Item2, this);
        }
        foreach (var dep in reads)
        {
            if (!_dependencies.Contains(dep) && !dep.Item1.IsDestroyed)
                dep.Item1.AddObserver(dep.Item2, this);
        }
        _dependencies.Clear();
        foreach (var dep in reads)
            _dependencies.Add(dep);
        reads.Clear();
    }

    private static bool ContainsRead(List<(QObject Object, string Property)> reads, (QObject Object, string Property) dep, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(reads[i].Object, dep.Object) && reads[i].Property == dep.Property)
                return true;
        }
        return false;
    }

    public void OnPropertyChanged(QObject sender, string property)
    {
        if (Target.Engine is { } engine && engine.IsCreating)
        {
            // While a component is being created, a binding that has not run yet runs in order later.
            if (!HasEvaluated)
                return;
        }
        Evaluate();
    }

    internal void ForgetDependency(QObject source, string property) => _dependencies.Remove((source, property));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var (obj, prop) in _dependencies)
            obj.RemoveObserver(prop, this);
        _dependencies.Clear();
    }
}

/// <summary>A property declared in QML with <c>property type name</c>.</summary>
public sealed class DynamicProperty
{
    private object? _value;
    private Func<(QObject Object, string? Property)>? _alias;

    public DynamicProperty(QObject owner, string name, string qmlType)
    {
        Owner = owner;
        Name = name;
        QmlType = qmlType;
        _value = ValueConverter.DefaultOfQmlType(qmlType);
        if (_value is IQmlList list)
            list.Changed += () => owner.NotifyChanged(name);
    }

    public QObject Owner { get; }

    public string Name { get; }

    public string QmlType { get; }

    public bool IsReadonly { get; init; }

    public bool IsRequired { get; init; }

    public bool IsDefault { get; init; }

    public bool WasAssigned { get; private set; }

    public bool IsAlias => _alias is not null;

    public void MakeAlias(Func<(QObject Object, string? Property)> resolve) => _alias = resolve;

    public object? Read()
    {
        if (_alias is not null)
        {
            // Bindings that read the alias subscribe here. The watch notifies this name when the path changes,
            // including when an intermediate object is replaced and the old target would no longer tell them.
            DependencyTracker.Read(Owner, Name);
            var (obj, prop) = _alias();
            return prop is null ? obj : obj.ReadProperty(prop);
        }
        DependencyTracker.Read(Owner, Name);
        return _value;
    }

    public void Write(object? value)
    {
        WasAssigned = true;
        if (_alias is not null)
        {
            var (obj, prop) = _alias();
            if (prop is null)
                throw new QmlException($"Cannot assign to alias \"{Name}\" of an object");
            obj.WriteProperty(prop, value);
            return;
        }
        if (_value is IQmlList list)
        {
            ValueConverter.AssignList(list, value, Owner.Engine);
            return;
        }
        var converted = ValueConverter.ConvertToQmlType(value, QmlType, Owner.Engine);
        if (ValueConverter.AreEqual(_value, converted))
            return;
        _value = converted;
        Owner.NotifyChanged(Name);
    }

    /// <summary>Marks an alias as assigned without writing, so its required check passes.</summary>
    internal void MarkAssigned() => WasAssigned = true;
}
