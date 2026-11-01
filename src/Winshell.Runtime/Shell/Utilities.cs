using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Shell;

/// <summary>The <c>EasingCurve</c>: an easing curve to evaluate directly.</summary>
public sealed class EasingCurve : QObject
{
    public EasingCurve()
    {
        Curve = new EasingGroup();
        Curve.SetOwner(this);
    }

    [Qml] public EasingGroup Curve { get; }

    [Qml]
    public double ValueAt(double x) => Curve.Value(Math.Clamp(x, 0, 1));

    [Qml]
    public object? Interpolate(double x, object? a, object? b)
    {
        var t = ValueAt(x);
        return (a, b) switch
        {
            (QRect ra, QRect rb) => new QRect(Lerp(ra.X, rb.X, t), Lerp(ra.Y, rb.Y, t), Lerp(ra.Width, rb.Width, t), Lerp(ra.Height, rb.Height, t)),
            (QPoint pa, QPoint pb) => new QPoint(Lerp(pa.X, pb.X, t), Lerp(pa.Y, pb.Y, t)),
            _ => Lerp(ValueConverter.ToDouble(a, "real"), ValueConverter.ToDouble(b, "real"), t),
        };
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>
/// The <c>ColorQuantizer</c>: the prevalent colors of an image, found by splitting its pixels at the median
/// of their widest channel <c>depth</c> times and averaging each part.
/// </summary>
public sealed class ColorQuantizer : QObject
{
    private string _source = "";
    private double _depth;
    private double _rescaleSize;
    private QRect? _imageRect;
    private List<object?> _colors = [];
    private int _generation;

    [Qml]
    public string Source
    {
        get { Track(); return _source; }
        set
        {
            if (SetField(ref _source, value ?? ""))
                Run();
        }
    }

    [Qml]
    public double Depth
    {
        get { Track(); return _depth; }
        set
        {
            if (SetField(ref _depth, value))
                Run();
        }
    }

    [Qml]
    public double RescaleSize
    {
        get { Track(); return _rescaleSize; }
        set
        {
            if (SetField(ref _rescaleSize, value))
                Run();
        }
    }

    [Qml]
    public object? ImageRect
    {
        get { Track(); return _imageRect; }
        set
        {
            _imageRect = value as QRect?;
            NotifyChanged("imageRect");
            Run();
        }
    }

    [Qml] public List<object?> Colors { get { Track(); return _colors; } }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        Run();
    }

    private void Run()
    {
        if (!IsComplete || Engine is not { } engine)
            return;
        var generation = ++_generation;
        if (_source.Length == 0)
        {
            Publish(generation, []);
            return;
        }
        var baseDir = CreationContext?.Imports?.Directory ?? engine.ShellDir;
        var (source, depth, size, rect) = (_source, (int)Math.Clamp(_depth, 0, 8), (int)Math.Max(0, _rescaleSize), _imageRect);
        _ = Task.Run(() =>
        {
            using var image = ImageLoader.Load(source, baseDir, size, size, cache: false);
            return image is null ? null : Quantize(image, depth, rect);
        }).ContinueWith(t =>
        {
            var colors = t.IsCompletedSuccessfully ? t.Result : null;
            engine.Loop.Post(() =>
            {
                if (colors is null)
                    engine.Log.Warn("colorquantizer", $"Cannot open: {source}");
                Publish(generation, colors ?? []);
            });
        }, TaskScheduler.Default);
    }

    private void Publish(int generation, List<object?> colors)
    {
        if (generation != _generation || IsDestroyed)
            return;
        _colors = colors;
        NotifyChanged("colors");
    }

    private static List<object?> Quantize(SKImage image, int depth, QRect? crop)
    {
        using var bitmap = SKBitmap.FromImage(image);
        var area = crop is { } r
            ? SKRectI.Intersect(new SKRectI((int)r.X, (int)r.Y, (int)(r.X + r.Width), (int)(r.Y + r.Height)), new SKRectI(0, 0, bitmap.Width, bitmap.Height))
            : new SKRectI(0, 0, bitmap.Width, bitmap.Height);
        var pixels = new List<SKColor>(Math.Max(0, area.Width * area.Height));
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
                pixels.Add(bitmap.GetPixel(x, y));
        }
        var result = new List<object?>();
        Split(pixels.ToArray(), depth, result);
        return result;
    }

    private static void Split(SKColor[] pixels, int depth, List<object?> result)
    {
        if (pixels.Length == 0)
            return;
        if (depth == 0)
        {
            double r = 0, g = 0, b = 0;
            foreach (var p in pixels)
            {
                r += p.Red;
                g += p.Green;
                b += p.Blue;
            }
            var n = pixels.Length * 255.0;
            result.Add(new QColor(r / n, g / n, b / n, 1));
            return;
        }
        static int Range(SKColor[] px, Func<SKColor, byte> channel) => px.Max(channel) - px.Min(channel);
        Func<SKColor, byte> widest = p => p.Red;
        var rRange = Range(pixels, p => p.Red);
        var gRange = Range(pixels, p => p.Green);
        var bRange = Range(pixels, p => p.Blue);
        if (gRange >= rRange && gRange >= bRange)
            widest = p => p.Green;
        else if (bRange >= rRange && bRange >= gRange)
            widest = p => p.Blue;
        var sorted = pixels.OrderBy(widest).ToArray();
        var mid = sorted.Length / 2;
        Split(sorted[..mid], depth - 1, result);
        Split(sorted[mid..], depth - 1, result);
    }
}

/// <summary>
/// The <c>TransformWatcher</c>: <c>transform</c> changes whenever anything that places <c>a</c> and
/// <c>b</c> relative to each other changes.
/// </summary>
public sealed class TransformWatcher : QObject
{
    private static readonly string[] Geometry = ["x", "y", "width", "height", "scale", "rotation"];
    private Item? _a, _b, _commonParent;
    private readonly List<PropertyWatch> _watches = [];

    [Qml] public Item? A { get { Track(); return _a; } set { if (SetField(ref _a, value)) Rewatch(); } }

    [Qml] public Item? B { get { Track(); return _b; } set { if (SetField(ref _b, value)) Rewatch(); } }

    [Qml] public Item? CommonParent { get { Track(); return _commonParent; } set { if (SetField(ref _commonParent, value)) Rewatch(); } }

    [Qml] public object? Transform { get { Track(); return null; } }

    private void Rewatch()
    {
        foreach (var w in _watches)
            w.Dispose();
        _watches.Clear();
        var watched = new HashSet<Item>();
        foreach (var start in new[] { _a, _b })
        {
            for (var item = start; item is not null && item != _commonParent && watched.Add(item); item = item.ParentItem)
            {
                _watches.Add(new PropertyWatch(item, () => NotifyChanged("transform"), Geometry));
                // A new parent changes the path between the items.
                _watches.Add(new PropertyWatch(item, () => Engine?.Loop.Post(Rewatch), "parent"));
            }
        }
        NotifyChanged("transform");
    }

    protected override void OnDestroy()
    {
        foreach (var w in _watches)
            w.Dispose();
        base.OnDestroy();
    }
}

/// <summary>
/// The <c>BoundComponent</c>: loads a component with this object's declared properties applied to it.
/// <c>source</c> is a file and <c>sourceComponent</c> is a component; the one set last is loaded. With
/// <c>bindValues</c> (the default) those properties stay in sync, and this object's <c>onSignal</c> functions
/// are connected to the loaded object's signals. A change after the object has loaded is ignored.
/// </summary>
public sealed class BoundComponent : Item
{
    private string _source = "";
    private QmlComponent? _component;
    private bool _ownsComponent;
    private bool _bindValues = true;
    private QObject? _item;
    private readonly List<IDisposable> _links = [];

    [Qml]
    public string Source
    {
        get { Track(); return _source; }
        set => SetSource(value ?? "");
    }

    /// <summary>The component to load. Null once the object has loaded: the component is released then, as in Quickshell.</summary>
    [Qml]
    public QmlComponent? SourceComponent
    {
        get { Track(); return _component; }
        set => SetSourceComponent(value);
    }

    [Qml]
    public bool BindValues
    {
        get { Track(); return _bindValues; }
        set
        {
            // Quickshell rejects the write after creation even when the value is unchanged, and before that
            // it keeps the new value. The same value before creation has nothing to report.
            if (IsComplete)
            {
                Engine?.Log.Warn("scene", "BoundComponent: bindValues cannot be set after creation");
                return;
            }
            if (_bindValues != value)
            {
                _bindValues = value;
                NotifyChanged("bindValues");
            }
        }
    }

    [Qml("item")] public QObject? Item_ { get { Track("item"); return _item; } }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        CreateItem();
    }

    private void SetSource(string source)
    {
        if (source == _source)
            return;
        if (IsComplete)
        {
            Engine?.Log.Warn("scene", "BoundComponent: source cannot be set after creation");
            return;
        }
        if (Engine is not { } engine)
            return;
        var path = ImageLoader.LocalPath(source, CreationContext?.Imports?.Directory ?? engine.ShellDir);
        if (path is null || !File.Exists(path))
        {
            engine.Log.Warn("scene", $"BoundComponent: Cannot open: {source}");
            return;
        }
        QmlComponent component;
        try
        {
            component = new QmlComponent(engine, engine.LoadDocument(path));
        }
        catch (Exception e) when (e is QmlException or QmlCompileException or Winshell.Qml.QmlSyntaxException or IOException)
        {
            engine.Log.Warn("scene", $"BoundComponent: Cannot open: {source}");
            return;
        }
        // A failed open leaves the previous component and the previous url in place.
        component.SetOwner(this);
        DropComponent();
        _component = component;
        _ownsComponent = true;
        _source = source;
        NotifyChanged("source");
        NotifyChanged("sourceComponent");
    }

    private void SetSourceComponent(QmlComponent? component)
    {
        if (ReferenceEquals(component, _component))
            return;
        if (IsComplete)
        {
            Engine?.Log.Warn("scene", "BoundComponent: sourceComponent cannot be set after creation");
            return;
        }
        // The caller's component stays theirs. Setting this does not clear source.
        DropComponent();
        _component = component;
        _ownsComponent = false;
        NotifyChanged("sourceComponent");
    }

    private void CreateItem()
    {
        if (Engine is not { } engine)
            return;
        if (_component is null || _component.IsDestroyed)
        {
            engine.Log.Warn("scene", "BoundComponent has no component");
            return;
        }
        var props = DynamicProperties.ToDictionary(p => p.Name, p => p.Read());
        QObject target;
        try
        {
            // The parent owns the item, so the engine does not root it for the life of the shell.
            target = _component.Create(this, props, null);
        }
        catch (Exception e) when (e is QmlException or QmlCompileException or Winshell.Qml.QmlSyntaxException or IOException)
        {
            engine.Log.Warn("scene", e.Message);
            DropComponent();
            NotifyChanged("sourceComponent");
            return;
        }
        // Quickshell releases the component once incubation finishes. An owned file component is destroyed;
        // a caller's component is only dropped from this property.
        DropComponent();
        _item = target;
        if (_bindValues)
        {
            foreach (var property in DynamicProperties)
            {
                var name = property.Name;
                if (!target.HasProperty(name))
                {
                    engine.Log.Warn("scene", $"property {name} defined on BoundComponent but not on its contained object.");
                    continue;
                }
                if (!CanWrite(target, name))
                {
                    engine.Log.Warn("scene", $"property {name} defined on BoundComponent is not writable for its contained object.");
                    continue;
                }
                var captured = property;
                _links.Add(new PropertyWatch(this, () => WriteToTarget(target, name, captured.Read()), name));
                WriteToTarget(target, name, property.Read());
            }
        }
        var thisValue = engine.Js.Wrap(this);
        foreach (var name in FunctionNames)
        {
            if (Names.SignalOfHandler(name) is { } signal && target.GetSignal(signal) is { } slot)
                _links.Add(slot.ConnectScript(GetFunction(name)!, thisValue, engine.Js));
        }
        if (target is Item child)
        {
            _links.Add(new PropertyWatch(this, () =>
            {
                if (!child.IsDestroyed && !child.IsDestroying)
                    FitChild(child);
            }, "width", "height"));
            _links.Add(new PropertyWatch(child, () =>
            {
                if (!IsDestroyed && !IsDestroying)
                    FollowImplicitSize(child);
            }, "implicitWidth", "implicitHeight"));
            FollowImplicitSize(child);
            FitChild(child);
        }
        NotifyChanged("item");
        NotifyChanged("sourceComponent");
    }

    private static void WriteToTarget(QObject target, string name, object? value)
    {
        if (!target.IsDestroyed && !target.IsDestroying)
            target.WriteProperty(name, value);
    }

    private static bool CanWrite(QObject target, string name)
    {
        if (target.GetDynamicProperty(name) is { } dynamic)
            return !dynamic.IsReadonly;
        return target.Meta.Properties.TryGetValue(name, out var meta) && (meta.CanWrite || meta.IsList);
    }

    private void FitChild(Item child)
    {
        child.SetLayoutWidth(Width);
        child.SetLayoutHeight(Height);
    }

    private void FollowImplicitSize(Item child)
    {
        ImplicitWidth = child.ImplicitWidth;
        ImplicitHeight = child.ImplicitHeight;
    }

    /// <summary>Drops the watches and signal connections before the loaded object goes, so they cannot keep it.</summary>
    private void Unload()
    {
        foreach (var link in _links)
            link.Dispose();
        _links.Clear();
        var item = _item;
        _item = null;
        if (item is not null && !item.IsDestroyed && !item.IsDestroying)
            item.Destroy();
    }

    private void DropComponent()
    {
        var component = _component;
        var owns = _ownsComponent;
        _component = null;
        _ownsComponent = false;
        if (owns && component is not null && !component.IsDestroyed)
            component.Destroy();
    }

    protected override void OnDestroy()
    {
        Unload();
        DropComponent();
        base.OnDestroy();
    }
}
