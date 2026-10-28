using System.Runtime.CompilerServices;
using Jint.Native;

namespace Winshell.Runtime;

/// <summary>Something that is told when a property it read has changed, such as a binding.</summary>
public interface IPropertyObserver
{
    void OnPropertyChanged(QObject sender, string property);
}

/// <summary>
/// The base of every QML object. Properties read inside a binding are recorded as its dependencies, and
/// a property that changes tells its observers and emits its <c>nameChanged</c> signal.
/// </summary>
[QmlSignal("destroyed")]
public class QObject
{
    private Dictionary<string, List<IPropertyObserver>>? _observers;
    private Dictionary<string, SignalSlot>? _signals;
    private Dictionary<string, Binding>? _bindings;
    private Dictionary<string, IPropertyInterceptor>? _interceptors;
    private Dictionary<string, DynamicProperty>? _dynamic;
    private Dictionary<string, JsValue>? _functions;
    private Dictionary<Type, QObject>? _attached;
    private List<QObject>? _owned;
    private QObject? _owner;
    private string _objectName = "";
    private bool _destroying;

    public QObject()
    {
        Meta = MetaType.Of(GetType());
    }

    public MetaType Meta { get; }

    /// <summary>
    /// The engine this object belongs to. Set when the object is created by the engine, or by a service for the
    /// objects it makes itself, so their signals reach script handlers.
    /// </summary>
    public QmlEngine? Engine { get; set; }

    /// <summary>The object that owns this one and destroys it with itself.</summary>
    public QObject? Owner => _owner;

    public bool IsDestroyed { get; private set; }

    /// <summary>The context the object was created in, which its own bindings and functions use.</summary>
    public ComponentContext? CreationContext { get; internal set; }

    /// <summary>The QML type name, for messages.</summary>
    public virtual string TypeName => Meta.QmlName;

    internal JsValue? Wrapper { get; set; }

    [Qml]
    public string ObjectName
    {
        get { Track(); return _objectName; }
        set => SetField(ref _objectName, value);
    }

    public IReadOnlyList<QObject> OwnedObjects => (IReadOnlyList<QObject>?)_owned ?? [];

    public IEnumerable<DynamicProperty> DynamicProperties => (IEnumerable<DynamicProperty>?)_dynamic?.Values ?? [];

    // Property reads and writes

    /// <summary>Records a read of a property, so the binding being evaluated depends on it.</summary>
    protected void Track([CallerMemberName] string property = "") => DependencyTracker.Read(this, Names.ToQml(property));

    protected void TrackQml(string qmlName) => DependencyTracker.Read(this, qmlName);

    /// <summary>Sets a field and reports the change when the value differs.</summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string property = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        NotifyChanged(Names.ToQml(property));
        return true;
    }

    /// <summary>Tells the observers of a property that it changed, and emits <c>nameChanged</c>.</summary>
    public void NotifyChanged(string qmlName)
    {
        if (IsDestroyed)
            return;
        if (_observers is not null && _observers.TryGetValue(qmlName, out var list) && list.Count > 0)
        {
            foreach (var observer in list.ToArray())
                observer.OnPropertyChanged(this, qmlName);
        }
        if (_signals is not null && _signals.TryGetValue(qmlName + "Changed", out var slot))
            slot.Emit([]);
    }

    public void AddObserver(string qmlName, IPropertyObserver observer)
    {
        _observers ??= [];
        if (!_observers.TryGetValue(qmlName, out var list))
            _observers[qmlName] = list = [];
        list.Add(observer);
    }

    public void RemoveObserver(string qmlName, IPropertyObserver observer)
    {
        if (_observers is not null && _observers.TryGetValue(qmlName, out var list))
            list.Remove(observer);
    }

    // Signals

    /// <summary>The slot list of a signal, created on first use. Returns null when the type has no such signal.</summary>
    public SignalSlot? GetSignal(string name)
    {
        if (_signals is not null && _signals.TryGetValue(name, out var slot))
            return slot;
        var definition = FindSignal(name);
        if (definition is null)
            return null;
        _signals ??= [];
        return _signals[name] = new SignalSlot(this, definition);
    }

    public SignalDefinition? FindSignal(string name)
    {
        if (Meta.Signals.TryGetValue(name, out var signal))
            return signal;
        if (_dynamicSignals is not null && _dynamicSignals.TryGetValue(name, out signal))
            return signal;
        if (name.EndsWith("Changed", StringComparison.Ordinal))
        {
            var property = name[..^"Changed".Length];
            if (HasProperty(property))
                return new SignalDefinition(name, []);
        }
        return null;
    }

    private Dictionary<string, SignalDefinition>? _dynamicSignals;

    public void AddSignal(SignalDefinition signal)
    {
        _dynamicSignals ??= [];
        _dynamicSignals[signal.Name] = signal;
    }

    public void Emit(string name, params object?[] args)
    {
        if (IsDestroyed)
            return;
        GetSignal(name)?.Emit(args);
    }

    /// <summary>Connects a C# handler to a signal.</summary>
    public IDisposable Connect(string signal, Action<object?[]> handler)
    {
        var slot = GetSignal(signal) ?? throw new InvalidOperationException($"{TypeName} has no signal '{signal}'");
        return slot.Connect(handler);
    }

    // Properties as QML sees them

    public bool HasProperty(string name) =>
        Meta.Properties.ContainsKey(name) || (_dynamic?.ContainsKey(name) ?? false);

    public bool HasMember(string name) =>
        HasProperty(name) || Meta.Methods.ContainsKey(name) || (_functions?.ContainsKey(name) ?? false) || FindSignal(name) is not null;

    public DynamicProperty? GetDynamicProperty(string name) =>
        _dynamic is not null && _dynamic.TryGetValue(name, out var p) ? p : null;

    public DynamicProperty AddDynamicProperty(DynamicProperty property)
    {
        _dynamic ??= [];
        _dynamic[property.Name] = property;
        return property;
    }

    public void AddFunction(string name, JsValue function)
    {
        _functions ??= [];
        _functions[name] = function;
    }

    private Dictionary<string, Winshell.Qml.QmlFunctionDeclaration>? _functionDeclarations;

    /// <summary>The declaration of a function written in QML, with its parameter and return types.</summary>
    public Winshell.Qml.QmlFunctionDeclaration? FunctionDeclaration(string name) =>
        _functionDeclarations is not null && _functionDeclarations.TryGetValue(name, out var d) ? d : null;

    internal void AddFunctionDeclaration(Winshell.Qml.QmlFunctionDeclaration declaration)
    {
        _functionDeclarations ??= [];
        _functionDeclarations[declaration.Name] = declaration;
    }

    public IEnumerable<string> FunctionNames => (IEnumerable<string>?)_functions?.Keys ?? [];

    public JsValue? GetFunction(string name) =>
        _functions is not null && _functions.TryGetValue(name, out var f) ? f : null;

    /// <summary>Reads a property by its QML name. The read is tracked.</summary>
    public object? ReadProperty(string name)
    {
        if (_dynamic is not null && _dynamic.TryGetValue(name, out var dynamicProperty))
            return dynamicProperty.Read();
        if (Meta.Properties.TryGetValue(name, out var meta))
            return meta.Get(this);
        throw new QmlException($"{TypeName} has no property '{name}'");
    }

    /// <summary>
    /// Writes a property by its QML name, through its interceptor (a Behavior) when it has one.
    /// The binding on the property stays; use <see cref="AssignFromScript"/> for a script assignment.
    /// </summary>
    public void WriteProperty(string name, object? value)
    {
        if (_interceptors is not null && _interceptors.TryGetValue(name, out var interceptor) && interceptor.Enabled)
        {
            interceptor.Intercept(value);
            return;
        }
        WritePropertyDirect(name, value);
    }

    /// <summary>Writes a property without going through an interceptor.</summary>
    public void WritePropertyDirect(string name, object? value)
    {
        if (_dynamic is not null && _dynamic.TryGetValue(name, out var dynamicProperty))
        {
            dynamicProperty.Write(value);
            return;
        }
        if (Meta.Properties.TryGetValue(name, out var meta))
        {
            // A list property takes the items of the list, or the one object, assigned to it.
            if (!meta.CanWrite && meta.Get(this) is IQmlList list)
            {
                ValueConverter.AssignList(list, value, Engine);
                return;
            }
            if (!meta.CanWrite)
                throw new QmlException($"Cannot assign to read-only property \"{name}\"");
            meta.Set(this, value);
            return;
        }
        throw new QmlException($"Cannot assign to non-existent property \"{name}\"");
    }

    /// <summary>An assignment from a script: removes the property's binding, then writes.</summary>
    public void AssignFromScript(string name, object? value)
    {
        RemoveBinding(name);
        var dynamicProperty = GetDynamicProperty(name);
        if (dynamicProperty is { IsReadonly: true })
            throw new QmlException($"Cannot assign to read-only property \"{name}\"");
        WriteProperty(name, value);
    }

    public Type? PropertyClrType(string name)
    {
        if (_dynamic is not null && _dynamic.ContainsKey(name))
            return null;
        return Meta.Properties.TryGetValue(name, out var meta) ? meta.Type : null;
    }

    // Bindings and interceptors

    public void SetBinding(string name, Binding binding)
    {
        RemoveBinding(name);
        _bindings ??= [];
        _bindings[name] = binding;
    }

    public Binding? GetBinding(string name) => _bindings is not null && _bindings.TryGetValue(name, out var b) ? b : null;

    public void RemoveBinding(string name)
    {
        if (_bindings is not null && _bindings.Remove(name, out var old))
            old.Dispose();
    }

    public void SetInterceptor(string name, IPropertyInterceptor interceptor)
    {
        _interceptors ??= [];
        _interceptors[name] = interceptor;
    }

    // Attached objects

    public T GetAttached<T>(Func<QObject, T> create) where T : QObject => (T)GetAttached(typeof(T), o => create(o));

    public QObject GetAttached(Type type, Func<QObject, QObject> create)
    {
        _attached ??= [];
        if (!_attached.TryGetValue(type, out var attached))
        {
            attached = create(this);
            attached.Engine = Engine;
            _attached[type] = attached;
        }
        return attached;
    }

    public QObject? FindAttached(Type type) => _attached is not null && _attached.TryGetValue(type, out var a) ? a : null;

    // Ownership and lifetime

    /// <summary>Makes this object owned by <paramref name="owner"/>, which destroys it with itself.</summary>
    public void SetOwner(QObject? owner)
    {
        if (_owner == owner)
            return;
        _owner?._owned?.Remove(this);
        _owner = owner;
        if (owner is not null)
        {
            owner._owned ??= [];
            owner._owned.Add(this);
        }
        OnOwnerChanged();
    }

    protected virtual void OnOwnerChanged()
    {
    }

    /// <summary>Called once every binding of the component this object was created in has its first value.</summary>
    public virtual void ComponentComplete()
    {
    }

    public bool IsComplete { get; private set; }

    internal void MarkComplete()
    {
        IsComplete = true;
        ComponentComplete();
    }

    /// <summary>
    /// QML's <c>destroy()</c>: destroys the object once control returns to the event loop, or after
    /// <paramref name="delay"/> milliseconds.
    /// </summary>
    [Qml("destroy")]
    public void DestroyLater(double delay = 0)
    {
        if (Engine is not { } engine)
        {
            Destroy();
            return;
        }
        if (delay > 0)
            engine.Loop.Schedule(TimeSpan.FromMilliseconds(delay), Destroy);
        else
            engine.Loop.Post(Destroy);
    }

    /// <summary>Destroys this object and the objects it owns.</summary>
    public void Destroy()
    {
        if (IsDestroyed || _destroying)
            return;
        _destroying = true;
        if (_attached is not null)
        {
            foreach (var attached in _attached.Values)
                (attached as IDestructionListener)?.OnDestroying();
        }
        Emit("destroyed");
        if (_owned is not null)
        {
            foreach (var child in _owned.ToArray())
                child.Destroy();
            _owned.Clear();
        }
        OnDestroy();
        // Attached objects, such as Layout or ToolTip, can hold bindings to objects that outlive this one.
        if (_attached is not null)
        {
            foreach (var attached in _attached.Values)
                attached.Destroy();
        }
        if (_bindings is not null)
        {
            foreach (var binding in _bindings.Values)
                binding.Dispose();
            _bindings.Clear();
        }
        if (_signals is not null)
        {
            foreach (var slot in _signals.Values)
                slot.Clear();
        }
        IsDestroyed = true;
        _owner?._owned?.Remove(this);
        _owner = null;
        _observers?.Clear();
    }

    /// <summary>Releases what the object holds: timers, processes, windows.</summary>
    protected virtual void OnDestroy()
    {
    }

    public override string ToString() =>
        string.IsNullOrEmpty(_objectName) ? $"{TypeName}_{GetHashCode():x}" : $"{TypeName}_{GetHashCode():x}(\"{_objectName}\")";
}

/// <summary>An attached object that wants to know when the object it is attached to is destroyed.</summary>
public interface IDestructionListener
{
    void OnDestroying();
}

/// <summary>A Behavior or similar that takes over writes to one property.</summary>
public interface IPropertyInterceptor
{
    bool Enabled { get; }

    void Intercept(object? value);
}

public class QmlException(string message) : Exception(message);
