using Jint;
namespace Winshell.Runtime.Quick;

/// <summary>
/// Creates one delegate per model row and keeps them in step with the model. A delegate for a row that is still
/// in the model stays when the model changes. Repeater and Variants share this.
/// </summary>
public sealed class DelegateSet
{
    private readonly QObject _owner;
    private readonly Func<ModelEntry, int, QObject?> _create;
    private readonly Action<QObject> _destroy;
    private readonly List<(ModelEntry Entry, QObject? Instance)> _instances = [];

    public DelegateSet(QObject owner, Func<ModelEntry, int, QObject?> create, Action<QObject> destroy)
    {
        _owner = owner;
        _create = create;
        _destroy = destroy;
    }

    public IReadOnlyList<QObject?> Instances => _instances.Select(i => i.Instance).ToList();

    public int Count => _instances.Count;

    public QObject? At(int index) => index >= 0 && index < _instances.Count ? _instances[index].Instance : null;

    /// <summary>Brings the instances in line with the rows. Returns whether anything changed.</summary>
    public bool Sync(IReadOnlyList<ModelEntry> rows, bool keepMatching = true)
    {
        var changed = false;
        var old = _instances.ToList();
        _instances.Clear();
        var used = new bool[old.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var match = -1;
            if (keepMatching)
            {
                for (var j = 0; j < old.Count; j++)
                {
                    if (!used[j] && ModelReader.SameRow(old[j].Entry, row))
                    {
                        match = j;
                        break;
                    }
                }
            }
            if (match >= 0)
            {
                used[match] = true;
                var instance = old[match].Instance;
                if (instance?.CreationContext is { } ctx)
                {
                    foreach (var (k, v) in ModelReader.ContextFor(row, i))
                    {
                        if (k == "index")
                            ctx.SetContextProperty(k, v);
                    }
                }
                if (instance is not null && instance.HasProperty("index") && !instance.IsDestroyed)
                {
                    using (DependencyTracker.Suspend())
                    {
                        if (!Equals(instance.ReadProperty("index"), i))
                            instance.WriteProperty("index", i);
                    }
                }
                _instances.Add((row, instance));
                if (match != i)
                    changed = true;
            }
            else
            {
                _instances.Add((row, null));
                changed = true;
            }
        }
        for (var j = 0; j < old.Count; j++)
        {
            if (!used[j] && old[j].Instance is { } gone)
            {
                _destroy(gone);
                changed = true;
            }
        }
        for (var i = 0; i < _instances.Count; i++)
        {
            if (_instances[i].Instance is null)
            {
                var instance = _create(_instances[i].Entry, i);
                _instances[i] = (_instances[i].Entry, instance);
            }
        }
        _ = _owner;
        return changed;
    }

    public void Clear()
    {
        foreach (var (_, instance) in _instances)
        {
            if (instance is not null)
                _destroy(instance);
        }
        _instances.Clear();
    }
}

/// <summary>QtQuick's <c>Repeater</c>.</summary>
[QmlDefaultProperty("delegate")]
[QmlSignal("itemAdded", "index", "item")]
[QmlSignal("itemRemoved", "index", "item")]
public sealed class Repeater : Item
{
    private object? _model;
    private QmlComponent? _delegate;
    private readonly DelegateSet _set;
    private IObservableModel? _observed;

    public Repeater()
    {
        _set = new DelegateSet(this, Create, Remove);
    }

    [Qml]
    public object? Model
    {
        get { Track(); return _model; }
        set
        {
            _model = value is Jint.Native.JsValue js ? ValueConverter.KeepJs(js) : value;
            NotifyChanged("model");
            Observe();
            Regenerate();
        }
    }

    [Qml]
    public QmlComponent? Delegate
    {
        get { Track(); return _delegate; }
        set
        {
            if (_delegate == value)
                return;
            _delegate = value;
            NotifyChanged("delegate");
            _set.Clear();
            Regenerate();
        }
    }

    [Qml]
    public int Count
    {
        get { Track(); return _set.Count; }
    }

    [Qml]
    public Item? ItemAt(int index) => _set.At(index) as Item;

    [Qml]
    public int IndexOf(Item item) => _set.Instances.ToList().IndexOf(item);

    private void Observe()
    {
        if (_observed is not null)
            _observed.ModelChanged -= Regenerate;
        _observed = _model as IObservableModel;
        if (_observed is not null)
            _observed.ModelChanged += Regenerate;
    }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        Regenerate();
    }

    protected override void OnParentChanged(Item? oldParent, Item? newParent)
    {
        if (IsComplete)
        {
            _set.Clear();
            Regenerate();
        }
    }

    private void Regenerate()
    {
        if (!IsComplete || IsDestroyed)
            return;
        var rows = _delegate is null || ParentItem is null ? [] : ModelReader.Read(_model);
        var before = _set.Count;
        _set.Sync(rows);
        // Delegates sit after the repeater among its parent's children, in model order.
        if (ParentItem is { } parent)
        {
            var at = parent.Children.IndexOf(this) + 1;
            foreach (var instance in _set.Instances)
            {
                if (instance is Item item && item.ParentItem == parent)
                {
                    var current = parent.Children.IndexOf(item);
                    if (current != at)
                    {
                        parent.Children.Move(current, current < at ? at - 1 : at);
                        if (current < at)
                            at--;
                    }
                    at++;
                }
            }
        }
        if (before != _set.Count)
            NotifyChanged("count");
    }

    private QObject? Create(ModelEntry entry, int index)
    {
        if (_delegate is null || ParentItem is not { } parent)
            return null;
        var props = new Dictionary<string, object?>(StringComparer.Ordinal);
        var context = ModelReader.ContextFor(entry, index);
        var obj = _delegate.Create(null, null, context, o =>
        {
            // Required properties named like model roles are set from the row.
            foreach (var (k, v) in context)
            {
                if (o.GetDynamicProperty(k) is { IsRequired: true })
                    o.WriteProperty(k, v);
            }
            if (o is Item item)
                item.SetParentItem(parent);
            o.SetOwner(this);
        });
        Emit("itemAdded", index, obj);
        _ = props;
        return obj;
    }

    private void Remove(QObject obj)
    {
        Emit("itemRemoved", -1, obj);
        obj.Destroy();
    }

    protected override void OnDestroy()
    {
        if (_observed is not null)
            _observed.ModelChanged -= Regenerate;
        _set.Clear();
        base.OnDestroy();
    }
}

/// <summary>QtQuick's <c>Loader</c>.</summary>
[QmlSignal("loaded")]
public sealed class Loader : Item
{
    private string _source = "";
    private QmlComponent? _sourceComponent;
    private bool _active = true;
    private bool _asynchronous;
    private QObject? _item;
    private int _status;

    [QmlEnum]
    public enum StatusValue
    {
        Null = 0,
        Ready = 1,
        Loading = 2,
        Error = 3,
    }

    [Qml]
    public string Source
    {
        get { Track(); return _source; }
        set
        {
            if (SetField(ref _source, value ?? ""))
                Reload();
        }
    }

    [Qml]
    public QmlComponent? SourceComponent
    {
        get { Track(); return _sourceComponent; }
        set
        {
            if (SetField(ref _sourceComponent, value))
                Reload();
        }
    }

    [Qml]
    public bool Active
    {
        get { Track(); return _active; }
        set
        {
            if (SetField(ref _active, value))
                Reload();
        }
    }

    [Qml]
    public bool Asynchronous
    {
        get { Track(); return _asynchronous; }
        set => SetField(ref _asynchronous, value);
    }

    [Qml("item")]
    public QObject? Item_
    {
        get { Track("item"); return _item; }
    }

    [Qml]
    public int Status
    {
        get { Track(); return _status; }
    }

    [Qml]
    public double Progress => _item is null ? 0 : 1;

    [Qml]
    public void SetSource(string source, object? properties = null)
    {
        _pendingProperties = properties;
        Source = source;
    }

    private object? _pendingProperties;

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        Reload();
    }

    private void Reload()
    {
        if (!IsComplete)
            return;
        var old = _item;
        _item = null;
        old?.Destroy();
        if (!_active)
        {
            SetStatus(0);
            NotifyChanged("item");
            return;
        }
        QmlComponent? component = _sourceComponent;
        if (component is null && _source.Length > 0 && Engine is { } engine)
        {
            var path = ImageLoader.LocalPath(_source, CreationContext?.Imports?.Directory ?? engine.ShellDir);
            if (path is not null && File.Exists(path))
                component = new QmlComponent(engine, engine.LoadDocument(path));
        }
        if (component is null)
        {
            SetStatus(_source.Length > 0 ? 3 : 0);
            NotifyChanged("item");
            return;
        }
        try
        {
            IReadOnlyDictionary<string, object?>? props = null;
            if (_pendingProperties is Jint.Native.JsValue js && js.IsObject())
                props = js.AsObject().GetOwnPropertyKeys().Where(k => k.IsString()).ToDictionary(k => k.AsString(), k => (object?)js.AsObject().Get(k));
            _pendingProperties = null;
            _item = component.Create(null, props, null, o =>
            {
                o.SetOwner(this);
                if (o is Item item)
                    item.SetParentItem(this);
            });
        }
        catch (Exception e) when (e is QmlException or QmlCompileException)
        {
            Engine?.Log.Warn("scene", e.Message);
            SetStatus(3);
            NotifyChanged("item");
            return;
        }
        if (_item is Item child)
        {
            child.AddObserver("implicitWidth", _sizeFollower ??= new SizeFollower(this));
            child.AddObserver("implicitHeight", _sizeFollower);
            child.AddObserver("width", _sizeFollower);
            child.AddObserver("height", _sizeFollower);
            SyncSize();
        }
        SetStatus(1);
        NotifyChanged("item");
        Emit("loaded");
    }

    private SizeFollower? _sizeFollower;

    /// <summary>The loader's implicit size follows its item; an explicit loader size sizes the item.</summary>
    private void SyncSize()
    {
        if (_item is not Item child)
            return;
        using var _ = DependencyTracker.Suspend();
        ImplicitWidth = child.HasExplicitWidth ? child.Width : child.ImplicitWidth;
        ImplicitHeight = child.HasExplicitHeight ? child.Height : child.ImplicitHeight;
        if (HasExplicitWidth || (HasAnchors && Anchors.ControlsWidth))
            child.SetLayoutWidth(Width);
        if (HasExplicitHeight || (HasAnchors && Anchors.ControlsHeight))
            child.SetLayoutHeight(Height);
    }

    protected override void OnGeometryChanged() => SyncSize();

    private void SetStatus(int status)
    {
        if (_status == status)
            return;
        _status = status;
        NotifyChanged("status");
    }

    private sealed class SizeFollower(Loader loader) : IPropertyObserver
    {
        public void OnPropertyChanged(QObject sender, string property) => loader.SyncSize();
    }
}
