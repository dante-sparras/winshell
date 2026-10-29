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
    private readonly Action<QObject, int> _destroy;
    private readonly List<(ModelEntry Entry, QObject? Instance)> _instances = [];

    /// <param name="destroy">Destroys an instance that was at the given index.</param>
    public DelegateSet(QObject owner, Func<ModelEntry, int, QObject?> create, Action<QObject, int> destroy)
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
                if (instance is { IsDestroyed: false })
                    Update(instance, row, i);
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
                _destroy(gone, j);
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

    /// <summary>
    /// Gives a kept instance its row's current index and roles: a row's roles can change in place, as
    /// <c>ListModel.setProperty</c> does.
    /// </summary>
    private static void Update(QObject instance, ModelEntry row, int index)
    {
        var context = ModelReader.ContextFor(row, index);
        if (instance.CreationContext is { } ctx)
        {
            var rolesChanged = false;
            foreach (var (k, v) in context)
            {
                if (k != "model" && ctx.SetContextProperty(k, v) && k != "index")
                    rolesChanged = true;
            }
            // model.role reads the row as a whole; a changed role is a changed model.
            ctx.SetContextProperty("model", rolesChanged && row.Roles is not null ? new Dictionary<string, object?>(row.Roles) : context["model"]);
        }
        using (DependencyTracker.Suspend())
        {
            foreach (var (k, v) in context)
            {
                var required = instance.GetDynamicProperty(k) is { IsRequired: true };
                if ((required || (k == "index" && instance.HasProperty(k))) && !ValueConverter.AreEqual(instance.ReadProperty(k), v))
                    instance.WriteProperty(k, v);
            }
        }
    }

    public void Clear()
    {
        for (var i = _instances.Count - 1; i >= 0; i--)
        {
            if (_instances[i].Instance is { } instance)
                _destroy(instance, i);
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

    private void Remove(QObject obj, int index)
    {
        if (!_destroying)
            Emit("itemRemoved", index, obj);
        obj.Destroy();
    }

    private bool _destroying;

    protected override void OnDestroy()
    {
        if (_observed is not null)
            _observed.ModelChanged -= Regenerate;
        _destroying = true;
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
    private double _progress;
    private IDisposable? _itemDestroyed;
    private bool _inReload;
    private bool _reloadAgain;

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

    /// <summary>
    /// Accepted so a Qt document loads. Creation still finishes on this thread before the assignment
    /// returns; <c>true</c> does not defer it. <c>status</c> passes through <see cref="StatusValue.Loading"/>
    /// either way.
    /// </summary>
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
    public double Progress
    {
        get { Track(); return _progress; }
    }

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
        // Status and progress notify in the middle of a load, and a handler can change the source.
        // Finish this pass, then load whatever those handlers left behind.
        if (_inReload)
        {
            _reloadAgain = true;
            return;
        }
        _inReload = true;
        try
        {
            do
            {
                _reloadAgain = false;
                LoadOnce();
            }
            while (_reloadAgain);
        }
        finally
        {
            _inReload = false;
        }
    }

    private void LoadOnce()
    {
        ReleaseItem();
        if (!_active)
        {
            SetProgress(0);
            SetStatus((int)StatusValue.Null);
            NotifyChanged("item");
            return;
        }
        try
        {
            QmlComponent? component = _sourceComponent;
            if (component is null && _source.Length > 0 && Engine is { } engine)
            {
                var path = ImageLoader.LocalPath(_source, CreationContext?.Imports?.Directory ?? engine.ShellDir)
                    ?? throw new QmlException("Only local files can be loaded");
                component = new QmlComponent(engine, engine.LoadDocument(path));
            }
            if (component is null)
            {
                SetProgress(0);
                SetStatus((int)StatusValue.Null);
                NotifyChanged("item");
                return;
            }
            SetProgress(0);
            SetStatus((int)StatusValue.Loading);
            if (_reloadAgain)
                return;
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
            if (_reloadAgain)
            {
                ReleaseItem();
                return;
            }
            WatchItem();
        }
        catch (Exception e) when (e is QmlException or QmlCompileException or Winshell.Qml.QmlSyntaxException or IOException or Jint.Runtime.JavaScriptException)
        {
            ReleaseItem();
            var message = e is FileNotFoundException or DirectoryNotFoundException ? $"{_source}: No such file or directory" : Log.Describe(e, _source);
            Engine?.Log.Warn("scene", message);
            SetProgress(0);
            SetStatus((int)StatusValue.Error);
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
        SetProgress(1);
        SetStatus((int)StatusValue.Ready);
        NotifyChanged("item");
        Emit("loaded");
    }

    /// <summary>Drops the loaded object without treating our own destroy as someone else's.</summary>
    private void ReleaseItem()
    {
        _itemDestroyed?.Dispose();
        _itemDestroyed = null;
        var old = _item;
        _item = null;
        old?.Destroy();
    }

    private void WatchItem()
    {
        _itemDestroyed?.Dispose();
        _itemDestroyed = _item?.GetSignal("destroyed")?.Connect(_ => OnLoadedItemDestroyed());
    }

    private void OnLoadedItemDestroyed()
    {
        _itemDestroyed?.Dispose();
        _itemDestroyed = null;
        if (IsDestroying || IsDestroyed)
        {
            _item = null;
            return;
        }
        if (_item is null)
            return;
        _item = null;
        SetProgress(0);
        SetStatus((int)StatusValue.Null);
        NotifyChanged("item");
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

    private void SetProgress(double progress)
    {
        if (_progress == progress)
            return;
        _progress = progress;
        NotifyChanged("progress");
    }

    private sealed class SizeFollower(Loader loader) : IPropertyObserver
    {
        public void OnPropertyChanged(QObject sender, string property) => loader.SyncSize();
    }
}
