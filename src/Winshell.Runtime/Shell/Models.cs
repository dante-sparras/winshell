using Jint;
using Jint.Native;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Shell;

/// <summary>The <c>ObjectModel</c>: a list of objects that services keep up to date, such as tray items.</summary>
[QmlSignal("objectInsertedPre", "object", "index")]
[QmlSignal("objectInsertedPost", "object", "index")]
[QmlSignal("objectRemovedPre", "object", "index")]
[QmlSignal("objectRemovedPost", "object", "index")]
public class ObjectModel : QObject, IObservableModel
{
    private readonly List<object?> _values = [];
    private List<object?>? _published;
    private int _silence;
    private bool _pendingChange;

    public event Action? ModelChanged;

    [Qml]
    public IReadOnlyList<object?> Values
    {
        get
        {
            Track();
            return _published ??= _values.ToList();
        }
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

    /// <summary>Replaces the contents, keeping objects that stay so delegates for them stay too. A repeated object is stored once, at its first place.</summary>
    public void Set(IReadOnlyList<object?> values)
    {
        var present = new HashSet<object?>(values.Count, EqualityComparer<object?>.Default);
        var next = new List<object?>(values.Count);
        foreach (var value in values)
        {
            if (present.Add(value))
                next.Add(value);
        }
        _silence++;
        try
        {
            for (var i = _values.Count - 1; i >= 0; i--)
            {
                if (!present.Contains(_values[i]))
                    RemoveAt(i);
            }
            // The rows before i already match. An insert at i moves every row after it one place, so the index
            // stays right with an offset; only a move rebuilds it.
            var index = IndexOfAll();
            var inserted = 0;
            for (var i = 0; i < next.Count; i++)
            {
                var value = next[i];
                var current = value is null ? _values.IndexOf(null) : index.TryGetValue(value, out var at) ? at + inserted : -1;
                if (current == i)
                    continue;
                if (current < 0)
                {
                    Insert(i, value);
                    inserted++;
                    continue;
                }
                _values.RemoveAt(current);
                _values.Insert(i, value);
                Changed();
                index = IndexOfAll();
                inserted = 0;
            }
        }
        finally
        {
            _silence--;
        }
        if (_pendingChange)
        {
            _pendingChange = false;
            NotifyChanged("values");
            ModelChanged?.Invoke();
        }
    }

    /// <summary>Where each object first appears.</summary>
    private Dictionary<object, int> IndexOfAll()
    {
        var index = new Dictionary<object, int>(_values.Count, EqualityComparer<object>.Default);
        for (var i = _values.Count - 1; i >= 0; i--)
        {
            if (_values[i] is { } key)
                index[key] = i;
        }
        return index;
    }

    private void Changed()
    {
        _published = null;
        if (_silence > 0)
        {
            _pendingChange = true;
            return;
        }
        NotifyChanged("values");
        ModelChanged?.Invoke();
    }
}

/// <summary>
/// The <c>ScriptModel</c>: a model made from a script's array, which keeps objects that stay in it,
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
        set
        {
            if (SetField(ref _comparisonMode, value))
                Recompute();
        }
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
            if (_comparisonMode == (int)ObjectComparison.Identity)
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

/// <summary>The <c>Region</c>: a shape for a window's <c>mask</c> or <c>visibleMask</c>.</summary>
[QmlDefaultProperty("regions")]
[QmlSignal("changed")]
public sealed class Region : QObject, IPropertyObserver
{
    private Item? _item;
    private double _x, _y, _width, _height;
    private int _shape;
    private int _intersection;
    private double _radius, _topLeftRadius, _topRightRadius, _bottomLeftRadius, _bottomRightRadius;

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

    [Qml] public double TopLeftRadius { get { Track(); return _topLeftRadius; } set { if (SetField(ref _topLeftRadius, value)) Changed(); } }

    [Qml] public double TopRightRadius { get { Track(); return _topRightRadius; } set { if (SetField(ref _topRightRadius, value)) Changed(); } }

    [Qml] public double BottomLeftRadius { get { Track(); return _bottomLeftRadius; } set { if (SetField(ref _bottomLeftRadius, value)) Changed(); } }

    [Qml] public double BottomRightRadius { get { Track(); return _bottomRightRadius; } set { if (SetField(ref _bottomRightRadius, value)) Changed(); } }

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
        else if (RoundRect(rect) is { } round)
        {
            using (round)
            using (var builder = new SkiaSharp.SKPathBuilder())
            {
                builder.AddRoundRect(round, SkiaSharp.SKPathDirection.Clockwise);
                using var path = builder.Detach();
                region.SetPath(path);
            }
        }
        else
        {
            region.SetRect(rect);
        }
        foreach (var child in Regions.Snapshot)
        {
            using var other = child.ToSkRegion(windowRoot);
            var op = (Shell.Intersection)child._intersection switch
            {
                Shell.Intersection.Subtract => SkiaSharp.SKRegionOperation.Difference,
                Shell.Intersection.Intersect => SkiaSharp.SKRegionOperation.Intersect,
                Shell.Intersection.Xor => SkiaSharp.SKRegionOperation.XOR,
                _ => SkiaSharp.SKRegionOperation.Union,
            };
            region.Op(other, op);
        }
        return region;
    }

    /// <summary>A round rectangle when any corner is curved. A corner above 0 replaces <c>radius</c>; 0 uses it.</summary>
    private SkiaSharp.SKRoundRect? RoundRect(SkiaSharp.SKRectI rect)
    {
        var max = Math.Min(rect.Width, rect.Height) / 2f;
        float Curve(double corner)
        {
            var chosen = corner > 0 ? corner : _radius;
            return (float)Math.Clamp(chosen, 0, max);
        }
        var topLeft = Curve(_topLeftRadius);
        var topRight = Curve(_topRightRadius);
        var bottomRight = Curve(_bottomRightRadius);
        var bottomLeft = Curve(_bottomLeftRadius);
        if (topLeft == 0 && topRight == 0 && bottomRight == 0 && bottomLeft == 0)
            return null;
        var round = new SkiaSharp.SKRoundRect();
        round.SetRectRadii(new SkiaSharp.SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
        [
            new SkiaSharp.SKPoint(topLeft, topLeft),
            new SkiaSharp.SKPoint(topRight, topRight),
            new SkiaSharp.SKPoint(bottomRight, bottomRight),
            new SkiaSharp.SKPoint(bottomLeft, bottomLeft),
        ]);
        return round;
    }
}

/// <summary>The <c>Intersection</c> values, for <c>Region.intersection</c>.</summary>
public enum Intersection
{
    Combine = 0,
    Subtract = 1,
    Intersect = 2,
    Xor = 3,
}

/// <summary>The <c>ObjectComparison</c> values, for <c>ScriptModel.comparisonMode</c>.</summary>
public enum ObjectComparison
{
    Structure = 0,
    Identity = 1,
}
