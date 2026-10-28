using Jint;
using Jint.Native;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Quickshell;

/// <summary>Quickshell's <c>ObjectModel</c>: a list of objects that services keep up to date, such as tray items.</summary>
[QmlSignal("objectInsertedPre", "object", "index")]
[QmlSignal("objectInsertedPost", "object", "index")]
[QmlSignal("objectRemovedPre", "object", "index")]
[QmlSignal("objectRemovedPost", "object", "index")]
public class ObjectModel : QObject, IObservableModel
{
    private readonly List<object?> _values = [];

    public event Action? ModelChanged;

    [Qml]
    public IReadOnlyList<object?> Values
    {
        get { Track(); return _values.ToList(); }
    }

    public IReadOnlyList<object?> Rows => _values;

    [Qml]
    public int IndexOf(object? obj) => _values.FindIndex(v => ReferenceEquals(v, ValueConverter.Unwrap(obj)));

    public void Insert(int index, object? value)
    {
        index = Math.Clamp(index, 0, _values.Count);
        Emit("objectInsertedPre", value, index);
        _values.Insert(index, value);
        Emit("objectInsertedPost", value, index);
        Changed();
    }

    public void Add(object? value) => Insert(_values.Count, value);

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _values.Count)
            return;
        var value = _values[index];
        Emit("objectRemovedPre", value, index);
        _values.RemoveAt(index);
        Emit("objectRemovedPost", value, index);
        Changed();
    }

    public void Remove(object? value) => RemoveAt(_values.IndexOf(value));

    public void Clear()
    {
        while (_values.Count > 0)
            RemoveAt(_values.Count - 1);
    }

    /// <summary>Replaces the contents, keeping objects that stay so delegates for them stay too.</summary>
    public void Set(IReadOnlyList<object?> values)
    {
        for (var i = _values.Count - 1; i >= 0; i--)
        {
            if (!values.Contains(_values[i]))
                RemoveAt(i);
        }
        for (var i = 0; i < values.Count; i++)
        {
            var current = _values.IndexOf(values[i]);
            if (current == i)
                continue;
            if (current >= 0)
            {
                _values.RemoveAt(current);
                _values.Insert(i, values[i]);
                Changed();
            }
            else
            {
                Insert(i, values[i]);
            }
        }
    }

    private void Changed()
    {
        NotifyChanged("values");
        ModelChanged?.Invoke();
    }
}

/// <summary>
/// Quickshell's <c>ScriptModel</c>: a model made from a script's array, which keeps objects that stay in it,
/// so delegates for them are not made again when the array is computed again.
/// </summary>
public sealed class ScriptModel : ObjectModel
{
    private object? _values;
    private string _objectProp = "";
    private int _comparisonMode;

    [Qml("values")]
    public new object? Values
    {
        get { Track("values"); return base.Values; }
        set
        {
            _values = value;
            Recompute();
        }
    }

    [Qml]
    public string ObjectProp
    {
        get { Track(); return _objectProp; }
        set
        {
            if (SetField(ref _objectProp, value ?? ""))
                Recompute();
        }
    }

    [Qml]
    public int ComparisonMode
    {
        get { Track(); return _comparisonMode; }
        set => SetField(ref _comparisonMode, value);
    }

    private void Recompute()
    {
        var incoming = ModelReader.Read(_values).Select(e => e.ModelData).ToList();
        var current = Rows.ToList();
        var result = new List<object?>(incoming.Count);
        foreach (var value in incoming)
        {
            var match = current.FindIndex(c => Equivalent(c, value));
            if (match >= 0)
            {
                result.Add(current[match]);
                current.RemoveAt(match);
            }
            else
            {
                result.Add(value);
            }
        }
        Set(result);
    }

    private bool Equivalent(object? a, object? b)
    {
        if (ReferenceEquals(a, b) || (a is not JsValue && a is not QObject && Equals(a, b)))
            return true;
        if (a is JsValue ja && b is JsValue jb && ja.IsObject() && jb.IsObject())
        {
            if (_objectProp.Length > 0)
                return ja.AsObject().Get(_objectProp).Equals(jb.AsObject().Get(_objectProp));
            if (_comparisonMode == 1)
                return false;
            var engine = ja.AsObject().Engine;
            var serializer = new Jint.Native.Json.JsonSerializer(engine);
            try
            {
                return serializer.Serialize(ja).ToString() == serializer.Serialize(jb).ToString();
            }
            catch (Jint.Runtime.JavaScriptException)
            {
                return false;
            }
        }
        return false;
    }
}

/// <summary>Quickshell's <c>Region</c>: a shape for a window's input mask.</summary>
[QmlDefaultProperty("regions")]
[QmlSignal("changed")]
public sealed class Region : QObject, IPropertyObserver
{
    private Item? _item;
    private double _x, _y, _width, _height;
    private int _shape;
    private int _intersection;
    private double _radius;

    public Region()
    {
        Regions = new QmlList<Region>(this, "regions", added: r =>
        {
            r.SetOwner(this);
            r.Connect("changed", _ => Emit("changed"));
            Emit("changed");
        });
    }

    [QmlEnum]
    public enum ShapeValue
    {
        Rect = 0,
        Ellipse = 1,
    }

    private void Changed() => Emit("changed");

    [Qml]
    public Item? Item
    {
        get { Track(); return _item; }
        set
        {
            if (_item == value)
                return;
            foreach (var p in new[] { "x", "y", "width", "height" })
                _item?.RemoveObserver(p, this);
            _item = value;
            foreach (var p in new[] { "x", "y", "width", "height" })
                _item?.AddObserver(p, this);
            NotifyChanged("item");
            Changed();
        }
    }

    public void OnPropertyChanged(QObject sender, string property) => Changed();

    [Qml] public double X { get { Track(); return _x; } set { if (SetField(ref _x, value)) Changed(); } }

    [Qml] public double Y { get { Track(); return _y; } set { if (SetField(ref _y, value)) Changed(); } }

    [Qml] public double Width { get { Track(); return _width; } set { if (SetField(ref _width, value)) Changed(); } }

    [Qml] public double Height { get { Track(); return _height; } set { if (SetField(ref _height, value)) Changed(); } }

    [Qml] public int Shape { get { Track(); return _shape; } set { if (SetField(ref _shape, value)) Changed(); } }

    [Qml] public int Intersection { get { Track(); return _intersection; } set { if (SetField(ref _intersection, value)) Changed(); } }

    [Qml] public double Radius { get { Track(); return _radius; } set { if (SetField(ref _radius, value)) Changed(); } }

    [Qml] public double TopLeftRadius { get; set; }

    [Qml] public double TopRightRadius { get; set; }

    [Qml] public double BottomLeftRadius { get; set; }

    [Qml] public double BottomRightRadius { get; set; }

    [Qml]
    public QmlList<Region> Regions { get; }

    /// <summary>This region's own rectangle, in the coordinates of the window's content item.</summary>
    public QRect Bounds(Item? windowRoot)
    {
        using var _ = DependencyTracker.Suspend();
        if (_item is not null)
        {
            var origin = _item.MapToScene(0, 0);
            return new QRect(origin.X, origin.Y, _item.Width, _item.Height);
        }
        return new QRect(_x, _y, _width, _height);
    }

    /// <summary>The region as a set of rectangles, with its children combined, subtracted or intersected in order.</summary>
    public SkiaSharp.SKRegion ToSkRegion(Item? windowRoot)
    {
        var region = new SkiaSharp.SKRegion();
        var b = Bounds(windowRoot);
        var rect = SkiaSharp.SKRectI.Round(SkiaSharp.SKRect.Create((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height));
        if (_shape == (int)ShapeValue.Ellipse)
        {
            using var builder = new SkiaSharp.SKPathBuilder();
            builder.AddOval(rect);
            using var path = builder.Detach();
            region.SetPath(path);
        }
        else
        {
            region.SetRect(rect);
        }
        foreach (var child in Regions.Snapshot)
        {
            using var other = child.ToSkRegion(windowRoot);
            var op = child._intersection switch
            {
                1 => SkiaSharp.SKRegionOperation.Difference,
                2 => SkiaSharp.SKRegionOperation.Intersect,
                3 => SkiaSharp.SKRegionOperation.XOR,
                _ => SkiaSharp.SKRegionOperation.Union,
            };
            region.Op(other, op);
        }
        return region;
    }
}
