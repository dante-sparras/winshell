using Jint;
using Jint.Native;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Shell;

/// <summary>
/// The <c>Variants</c>: one instance of its delegate per model value, given as <c>modelData</c>. An
/// instance stays while its value is in the model; after a reload, instances take over the old instance made for
/// the same value.
/// </summary>
[QmlDefaultProperty("delegate")]
public sealed class Variants : Reloadable
{
    private List<object?> _model = [];
    private QmlComponent? _delegate;
    private readonly List<(object? Value, QObject Instance)> _instances = [];
    private bool _loaded;

    [Qml]
    public object? Model
    {
        get { Track(); return _model; }
        set
        {
            var list = value is JsValue js ? ValueConverter.KeepJs(js) : value;
            _model = list switch
            {
                null => [],
                JsValue array when array.IsArray() => ToList(array),
                IObservableModel observable => observable.Rows.ToList(),
                IQmlList q => q.ToList(),
                List<object?> l => l,
                System.Collections.IEnumerable e when list is not string => e.Cast<object?>().ToList(),
                _ => Warn(list),
            };
            Update();
            NotifyChanged("model");
            NotifyChanged("instances");
        }
    }

    private List<object?> Warn(object? value)
    {
        Engine?.Log.Warn("scene", $"Non list data {ValueConverter.Describe(value)} assigned to Variants.model, Ignoring.");
        return _model;
    }

    private static List<object?> ToList(JsValue array)
    {
        var a = array.AsArray();
        var list = new List<object?>((int)a.Length);
        for (uint i = 0; i < a.Length; i++)
            list.Add(ValueConverter.KeepJs(a[i]));
        return list;
    }

    [Qml]
    public QmlComponent? Delegate
    {
        get { Track(); return _delegate; }
        set
        {
            if (SetField(ref _delegate, value))
                Update();
        }
    }

    [Qml]
    public IReadOnlyList<QObject> Instances
    {
        get { Track(); return _instances.Select(i => i.Instance).ToList(); }
    }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        Update();
    }

    private static bool Same(object? a, object? b) => a switch
    {
        QObject or JsValue => ReferenceEquals(a, b) || (a is JsValue ja && b is JsValue jb && ja.IsPrimitive() && ja.Equals(jb)),
        _ => Equals(a, b),
    };

    private void Update()
    {
        if (!IsComplete || IsDestroyed)
            return;
        if (_delegate is null)
        {
            Engine?.Log.Warn("scene", "Variants instance does not have a component specified");
            return;
        }
        for (var i = _instances.Count - 1; i >= 0; i--)
        {
            if (!_model.Any(v => Same(v, _instances[i].Value)))
            {
                _instances[i].Instance.Destroy();
                _instances.RemoveAt(i);
            }
        }
        var seen = new List<object?>();
        foreach (var value in _model)
        {
            if (seen.Any(v => Same(v, value)))
            {
                Engine?.Log.Warn("scene", $"same value specified twice in Variants, duplicates will be ignored: {ValueConverter.Describe(value)}");
                continue;
            }
            seen.Add(value);
            if (_instances.Any(i => Same(i.Value, value)))
                continue;
            QObject instance;
            try
            {
                instance = _delegate.Create(null, new Dictionary<string, object?> { ["modelData"] = value }, null, o => o.SetOwner(this));
            }
            catch (Exception e) when (e is QmlException or QmlCompileException)
            {
                Engine?.Log.Warn("scene", $"failed to create variant with object {ValueConverter.Describe(value)}: {e.Message}");
                continue;
            }
            _instances.Add((value, instance));
            if (_loaded)
                ReloadRecursive(instance, null);
        }
        // Instances follow the model's order.
        _instances.Sort((a, b) => IndexIn(a.Value).CompareTo(IndexIn(b.Value)));
        NotifyChanged("instances");
    }

    private int IndexIn(object? value) => _model.FindIndex(v => Same(v, value));

    protected override void OnReload(QObject? oldInstance)
    {
        var old = oldInstance as Variants;
        var oldValues = old?._instances.ToList() ?? [];
        foreach (var (value, instance) in _instances)
        {
            QObject? match = null;
            var index = oldValues.FindIndex(o => SameAcrossReload(o.Value, value));
            if (index >= 0)
            {
                match = oldValues[index].Instance;
                oldValues.RemoveAt(index);
            }
            if (instance is Reloadable reloadable)
                reloadable.Reload(match);
            else
                ReloadChildrenRecursive(instance, match);
        }
        _loaded = true;
    }

    /// <summary>
    /// Values of the old shell are objects of the old engine, so screens match by name and plain values by
    /// value. A JavaScript object matches the old object that shares the most of its properties.
    /// </summary>
    private static bool SameAcrossReload(object? oldValue, object? newValue) => (oldValue, newValue) switch
    {
        (ShellScreen a, ShellScreen b) => a.Id == b.Id,
        (QObject a, QObject b) => ReferenceEquals(a, b),
        (JsValue a, JsValue b) when a.IsObject() && b.IsObject() => Json(a) == Json(b),
        _ => Equals(oldValue, newValue),
    };

    private static string Json(JsValue value)
    {
        try
        {
            var engine = value.AsObject().Engine;
            return new Jint.Native.Json.JsonSerializer(engine).Serialize(value).ToString();
        }
        catch (Jint.Runtime.JavaScriptException)
        {
            return "";
        }
    }

    protected override void OnDestroy()
    {
        foreach (var (_, instance) in _instances)
            instance.Destroy();
        _instances.Clear();
        base.OnDestroy();
    }
}

/// <summary>
/// The <c>LazyLoader</c>: creates its component when made active, and destroys it when made inactive.
/// </summary>
[QmlDefaultProperty("component")]
public sealed class LazyLoader : Reloadable
{
    private QmlComponent? _component;
    private string _source = "";
    private bool _active;
    private bool _loading;
    private QObject? _item;

    [Qml]
    public QmlComponent? Component
    {
        get { Track(); return _component; }
        set
        {
            if (SetField(ref _component, value) && _active)
                Recreate();
        }
    }

    [Qml]
    public string Source
    {
        get { Track(); return _source; }
        set
        {
            if (!SetField(ref _source, value ?? ""))
                return;
            if (Engine is not null && _source.Length > 0)
            {
                var path = ImageLoader.LocalPath(_source, CreationContext?.Imports?.Directory ?? Engine.ShellDir);
                if (path is not null && File.Exists(path))
                    _component = new QmlComponent(Engine, Engine.LoadDocument(path));
            }
            if (_active)
                Recreate();
        }
    }

    [Qml]
    public bool Active
    {
        get { Track(); return _active; }
        set
        {
            if (!SetField(ref _active, value))
                return;
            if (!IsComplete)
                return;
            if (value)
                Create();
            else
                DestroyItem();
        }
    }

    [Qml]
    public bool ActiveAsync
    {
        get { Track(); return _active; }
        set
        {
            if (value && !_active && Engine is not null)
            {
                Loading = true;
                Engine.Loop.Post(() =>
                {
                    if (!IsDestroyed && _loading)
                        Active = true;
                });
            }
            else
            {
                Active = value;
            }
            NotifyChanged("activeAsync");
        }
    }

    [Qml]
    public bool Loading
    {
        get { Track(); return _loading; }
        set
        {
            if (value && _item is null && !_loading && Engine is not null)
            {
                SetField(ref _loading, true);
                Engine.Loop.Post(() =>
                {
                    if (!IsDestroyed && _loading)
                        Create();
                });
            }
        }
    }

    [Qml("item")]
    public QObject? Item_
    {
        get { Track("item"); return _item; }
    }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        if (_active)
            Create();
    }

    private void Create()
    {
        if (_item is not null || _component is null)
        {
            if (_loading)
                SetField(ref _loading, false, nameof(Loading));
            return;
        }
        try
        {
            _item = _component.Create(null, null, null, o => o.SetOwner(this));
        }
        catch (Exception e) when (e is QmlException or QmlCompileException)
        {
            Engine?.Log.Warn("scene", e.Message);
        }
        if (!_active)
        {
            _active = true;
            NotifyChanged("active");
            NotifyChanged("activeAsync");
        }
        SetField(ref _loading, false, nameof(Loading));
        NotifyChanged("item");
        if (_item is not null && ReloadDone)
            ReloadRecursive(_item, null);
    }

    private void Recreate()
    {
        DestroyItem();
        Create();
    }

    private void DestroyItem()
    {
        var item = _item;
        _item = null;
        item?.Destroy();
        NotifyChanged("item");
    }

    protected override void OnReload(QObject? oldInstance)
    {
        var old = oldInstance as LazyLoader;
        if (_item is not null)
            ReloadRecursive(_item, old?._item);
    }
}
