using Jint;
using SkiaSharp;

namespace Winshell.Runtime.Quick.Shapes;

/// <summary>A path being built from path elements, with the point the next element starts at.</summary>
public sealed class PathCursor(SKPathBuilder builder)
{
    public SKPathBuilder Builder { get; } = builder;

    public float X { get; set; }

    public float Y { get; set; }

    public void MoveTo(float x, float y)
    {
        Builder.MoveTo(x, y);
        X = x;
        Y = y;
    }

    public void LineTo(float x, float y)
    {
        Builder.LineTo(x, y);
        X = x;
        Y = y;
    }
}

/// <summary>A piece of a path: a line, an arc, a curve. Changing one redraws the shape it is in.</summary>
public abstract class PathElement : QObject
{
    public event Action? Changed;

    protected void Raise() => Changed?.Invoke();

    /// <summary>Sets a field and redraws.</summary>
    protected void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
    {
        if (SetField(ref field, value, property))
            Raise();
    }

    public abstract void AddTo(PathCursor cursor);

    /// <summary>An absolute coordinate, a relative one from the current point, or the current point.</summary>
    protected static float Coord(double? absolute, double? relative, float current) =>
        absolute is { } a ? (float)a : relative is { } r ? current + (float)r : current;
}

/// <summary>An element that ends at a point given by x and y, or by relativeX and relativeY.</summary>
public abstract class PathPointElement : PathElement
{
    private double? _x, _y, _relativeX, _relativeY;

    [Qml] public double X { get { Track(); return _x ?? 0; } set => Set(ref _x, value); }

    [Qml] public double Y { get { Track(); return _y ?? 0; } set => Set(ref _y, value); }

    [Qml] public double RelativeX { get { Track(); return _relativeX ?? 0; } set => Set(ref _relativeX, value); }

    [Qml] public double RelativeY { get { Track(); return _relativeY ?? 0; } set => Set(ref _relativeY, value); }

    protected (float X, float Y) End(PathCursor c) => (Coord(_x, _relativeX, c.X), Coord(_y, _relativeY, c.Y));
}

/// <summary>QtQuick's <c>PathMove</c>: starts a new subpath.</summary>
public sealed class PathMove : PathPointElement
{
    public override void AddTo(PathCursor cursor)
    {
        var (x, y) = End(cursor);
        cursor.MoveTo(x, y);
    }
}

/// <summary>QtQuick's <c>PathLine</c>.</summary>
public sealed class PathLine : PathPointElement
{
    public override void AddTo(PathCursor cursor)
    {
        var (x, y) = End(cursor);
        cursor.LineTo(x, y);
    }
}

/// <summary>QtQuick's <c>PathQuad</c>: a quadratic Bézier curve.</summary>
public sealed class PathQuad : PathPointElement
{
    private double? _cx, _cy, _rcx, _rcy;

    [Qml] public double ControlX { get { Track(); return _cx ?? 0; } set => Set(ref _cx, value); }

    [Qml] public double ControlY { get { Track(); return _cy ?? 0; } set => Set(ref _cy, value); }

    [Qml] public double RelativeControlX { get { Track(); return _rcx ?? 0; } set => Set(ref _rcx, value); }

    [Qml] public double RelativeControlY { get { Track(); return _rcy ?? 0; } set => Set(ref _rcy, value); }

    public override void AddTo(PathCursor cursor)
    {
        var cx = Coord(_cx, _rcx, cursor.X);
        var cy = Coord(_cy, _rcy, cursor.Y);
        var (x, y) = End(cursor);
        cursor.Builder.QuadTo(cx, cy, x, y);
        cursor.X = x;
        cursor.Y = y;
    }
}

/// <summary>QtQuick's <c>PathCubic</c>: a cubic Bézier curve.</summary>
public sealed class PathCubic : PathPointElement
{
    private double? _c1x, _c1y, _c2x, _c2y, _r1x, _r1y, _r2x, _r2y;

    [Qml] public double Control1X { get { Track(); return _c1x ?? 0; } set => Set(ref _c1x, value); }

    [Qml] public double Control1Y { get { Track(); return _c1y ?? 0; } set => Set(ref _c1y, value); }

    [Qml] public double Control2X { get { Track(); return _c2x ?? 0; } set => Set(ref _c2x, value); }

    [Qml] public double Control2Y { get { Track(); return _c2y ?? 0; } set => Set(ref _c2y, value); }

    [Qml] public double RelativeControl1X { get { Track(); return _r1x ?? 0; } set => Set(ref _r1x, value); }

    [Qml] public double RelativeControl1Y { get { Track(); return _r1y ?? 0; } set => Set(ref _r1y, value); }

    [Qml] public double RelativeControl2X { get { Track(); return _r2x ?? 0; } set => Set(ref _r2x, value); }

    [Qml] public double RelativeControl2Y { get { Track(); return _r2y ?? 0; } set => Set(ref _r2y, value); }

    public override void AddTo(PathCursor cursor)
    {
        var c1x = Coord(_c1x, _r1x, cursor.X);
        var c1y = Coord(_c1y, _r1y, cursor.Y);
        var c2x = Coord(_c2x, _r2x, cursor.X);
        var c2y = Coord(_c2y, _r2y, cursor.Y);
        var (x, y) = End(cursor);
        cursor.Builder.CubicTo(c1x, c1y, c2x, c2y, x, y);
        cursor.X = x;
        cursor.Y = y;
    }
}

/// <summary>QtQuick's <c>PathArc</c>: an elliptical arc to a point, as in SVG.</summary>
public sealed class PathArc : PathPointElement
{
    private double _radiusX, _radiusY, _xAxisRotation;
    private bool _useLargeArc;
    private int _direction;

    [QmlEnum]
    public enum ArcDirection
    {
        Clockwise = 0,
        Counterclockwise = 1,
    }

    [Qml] public double RadiusX { get { Track(); return _radiusX; } set => Set(ref _radiusX, value); }

    [Qml] public double RadiusY { get { Track(); return _radiusY; } set => Set(ref _radiusY, value); }

    [Qml] public bool UseLargeArc { get { Track(); return _useLargeArc; } set => Set(ref _useLargeArc, value); }

    [Qml] public int Direction { get { Track(); return _direction; } set => Set(ref _direction, value); }

    [Qml] public double XAxisRotation { get { Track(); return _xAxisRotation; } set => Set(ref _xAxisRotation, value); }

    public override void AddTo(PathCursor cursor)
    {
        var (x, y) = End(cursor);
        if (_radiusX <= 0 || _radiusY <= 0)
        {
            cursor.LineTo(x, y);
            return;
        }
        cursor.Builder.ArcTo(
            (float)_radiusX, (float)_radiusY, (float)_xAxisRotation,
            _useLargeArc ? SKPathArcSize.Large : SKPathArcSize.Small,
            _direction == (int)ArcDirection.Clockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
            x, y);
        cursor.X = x;
        cursor.Y = y;
    }
}

/// <summary>QtQuick's <c>PathAngleArc</c>: an arc of an ellipse given by its center, start angle and sweep.</summary>
public sealed class PathAngleArc : PathElement
{
    private double _centerX, _centerY, _radiusX, _radiusY, _startAngle, _sweepAngle;
    private bool _moveToStart = true;

    [Qml] public double CenterX { get { Track(); return _centerX; } set => Set(ref _centerX, value); }

    [Qml] public double CenterY { get { Track(); return _centerY; } set => Set(ref _centerY, value); }

    [Qml] public double RadiusX { get { Track(); return _radiusX; } set => Set(ref _radiusX, value); }

    [Qml] public double RadiusY { get { Track(); return _radiusY; } set => Set(ref _radiusY, value); }

    [Qml] public double StartAngle { get { Track(); return _startAngle; } set => Set(ref _startAngle, value); }

    [Qml] public double SweepAngle { get { Track(); return _sweepAngle; } set => Set(ref _sweepAngle, value); }

    [Qml] public bool MoveToStart { get { Track(); return _moveToStart; } set => Set(ref _moveToStart, value); }

    public override void AddTo(PathCursor cursor)
    {
        var oval = SKRect.Create((float)(_centerX - _radiusX), (float)(_centerY - _radiusY), (float)(_radiusX * 2), (float)(_radiusY * 2));
        var sweep = (float)Math.Clamp(_sweepAngle, -359.99, 359.99);
        if (Math.Abs(_sweepAngle) >= 360)
        {
            // A whole ellipse, as two halves so it closes.
            cursor.Builder.ArcTo(oval, (float)_startAngle, 180 * Math.Sign(_sweepAngle), _moveToStart);
            cursor.Builder.ArcTo(oval, (float)_startAngle + 180 * Math.Sign(_sweepAngle), 180 * Math.Sign(_sweepAngle), false);
        }
        else
        {
            cursor.Builder.ArcTo(oval, (float)_startAngle, sweep, _moveToStart);
        }
        var end = (_startAngle + _sweepAngle) * Math.PI / 180;
        cursor.X = (float)(_centerX + _radiusX * Math.Cos(end));
        cursor.Y = (float)(_centerY + _radiusY * Math.Sin(end));
    }
}

/// <summary>QtQuick's <c>PathSvg</c>: path data in SVG syntax.</summary>
public sealed class PathSvg : PathElement
{
    private string _path = "";

    [Qml] public string Path { get { Track(); return _path; } set => Set(ref _path, value ?? ""); }

    public override void AddTo(PathCursor cursor)
    {
        if (_path.Length == 0)
            return;
        using var parsed = SKPath.ParseSvgPathData(_path);
        if (parsed is null)
            return;
        cursor.Builder.AddPath(parsed, SKPathAddMode.Append);
        var last = parsed.LastPoint;
        cursor.X = last.X;
        cursor.Y = last.Y;
    }
}

/// <summary>QtQuick's <c>PathRectangle</c> (Qt 6.8): a rectangle with optionally rounded corners.</summary>
public sealed class PathRectangle : PathElement
{
    private double _x, _y, _width, _height, _radius, _strokeAdjustment;
    private double? _tl, _tr, _bl, _br;
    private bool _bevel;

    [Qml] public double X { get { Track(); return _x; } set => Set(ref _x, value); }

    [Qml] public double Y { get { Track(); return _y; } set => Set(ref _y, value); }

    [Qml] public double Width { get { Track(); return _width; } set => Set(ref _width, value); }

    [Qml] public double Height { get { Track(); return _height; } set => Set(ref _height, value); }

    [Qml] public double Radius { get { Track(); return _radius; } set => Set(ref _radius, value); }

    [Qml] public double TopLeftRadius { get { Track(); return _tl ?? _radius; } set => Set(ref _tl, value); }

    [Qml] public double TopRightRadius { get { Track(); return _tr ?? _radius; } set => Set(ref _tr, value); }

    [Qml] public double BottomLeftRadius { get { Track(); return _bl ?? _radius; } set => Set(ref _bl, value); }

    [Qml] public double BottomRightRadius { get { Track(); return _br ?? _radius; } set => Set(ref _br, value); }

    [Qml] public double StrokeAdjustment { get { Track(); return _strokeAdjustment; } set => Set(ref _strokeAdjustment, value); }

    [Qml] public bool Bevel { get { Track(); return _bevel; } set => Set(ref _bevel, value); }

    public override void AddTo(PathCursor cursor)
    {
        var a = (float)_strokeAdjustment / 2;
        var rect = SKRect.Create((float)_x + a, (float)_y + a, Math.Max(0, (float)_width - 2 * a), Math.Max(0, (float)_height - 2 * a));
        var max = Math.Min(rect.Width, rect.Height) / 2;
        float R(double r) => (float)Math.Clamp(r - a, 0, max);
        using var rr = new SKRoundRect();
        rr.SetRectRadii(rect,
        [
            new SKPoint(R(TopLeftRadius), R(TopLeftRadius)),
            new SKPoint(R(TopRightRadius), R(TopRightRadius)),
            new SKPoint(R(BottomRightRadius), R(BottomRightRadius)),
            new SKPoint(R(BottomLeftRadius), R(BottomLeftRadius)),
        ]);
        cursor.Builder.AddRoundRect(rr, SKPathDirection.Clockwise);
        cursor.X = rect.Left;
        cursor.Y = rect.Top;
    }
}

/// <summary>QtQuick's <c>PathPolyline</c>: lines through a list of points.</summary>
public sealed class PathPolyline : PathElement
{
    private object? _path;

    [Qml]
    public object? Path
    {
        get { Track(); return _path; }
        set
        {
            _path = value is Jint.Native.JsValue js ? ValueConverter.KeepJs(js) : value;
            NotifyChanged("path");
            Raise();
        }
    }

    [Qml] public QPoint Start => Points().FirstOrDefault();

    private List<QPoint> Points()
    {
        var points = new List<QPoint>();
        foreach (var item in ValueConverter.ToList(_path))
        {
            switch (item)
            {
                case QPoint p:
                    points.Add(p);
                    break;
                case Jint.Native.JsValue js when js.IsObject():
                    {
                        var o = js.AsObject();
                        points.Add(new QPoint(ValueConverter.ToDouble(ValueConverter.FromJs(o.Get("x")), "real"), ValueConverter.ToDouble(ValueConverter.FromJs(o.Get("y")), "real")));
                        break;
                    }
                case IDictionary<string, object?> d when d.TryGetValue("x", out var x) && d.TryGetValue("y", out var y):
                    points.Add(new QPoint(ValueConverter.ToDouble(x, "real"), ValueConverter.ToDouble(y, "real")));
                    break;
            }
        }
        return points;
    }

    public override void AddTo(PathCursor cursor)
    {
        var points = Points();
        for (var i = 0; i < points.Count; i++)
        {
            if (i == 0)
                cursor.MoveTo((float)points[i].X, (float)points[i].Y);
            else
                cursor.LineTo((float)points[i].X, (float)points[i].Y);
        }
    }
}

/// <summary>QtQuick.Shapes' gradients: a list of stops and how they repeat.</summary>
[QmlDefaultProperty("stops")]
public abstract class ShapeGradient : QObject, IPropertyObserver
{
    private int _spread;
    private int _orientation = 2;

    [QmlEnum]
    public enum SpreadMode
    {
        PadSpread = 0,
        ReflectSpread = 1,
        RepeatSpread = 2,
    }

    protected ShapeGradient()
    {
        Stops = new QmlList<GradientStop>(this, "stops", added: s =>
        {
            s.SetOwner(this);
            s.AddObserver("position", this);
            s.AddObserver("color", this);
            Raise();
        }, removed: s =>
        {
            s.RemoveObserver("position", this);
            s.RemoveObserver("color", this);
            Raise();
        });
    }

    public event Action? Changed;

    protected void Raise() => Changed?.Invoke();

    void IPropertyObserver.OnPropertyChanged(QObject sender, string property) => Raise();

    protected void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
    {
        if (SetField(ref field, value, property))
            Raise();
    }

    [Qml] public QmlList<GradientStop> Stops { get; }

    [Qml] public int Spread { get { Track(); return _spread; } set => Set(ref _spread, value); }

    [Qml] public int Orientation { get { Track(); return _orientation; } set => Set(ref _orientation, value); }

    protected SKShaderTileMode TileMode => _spread switch
    {
        1 => SKShaderTileMode.Mirror,
        2 => SKShaderTileMode.Repeat,
        _ => SKShaderTileMode.Clamp,
    };

    protected (SKColor[] Colors, float[] Positions) StopArrays()
    {
        var stops = Stops.Snapshot.OrderBy(s => s.Position).ToArray();
        if (stops.Length == 0)
            return ([SKColors.Transparent, SKColors.Transparent], [0, 1]);
        if (stops.Length == 1)
            return ([stops[0].Color.ToSk(), stops[0].Color.ToSk()], [0, 1]);
        return (stops.Select(s => s.Color.ToSk()).ToArray(), stops.Select(s => (float)Math.Clamp(s.Position, 0, 1)).ToArray());
    }

    public abstract SKShader CreateShader();
}

/// <summary>QtQuick.Shapes' <c>LinearGradient</c>: from (x1, y1) to (x2, y2).</summary>
public sealed class LinearGradient : ShapeGradient
{
    private double _x1, _y1, _x2 = 100, _y2 = 100;

    [Qml] public double X1 { get { Track(); return _x1; } set => Set(ref _x1, value); }

    [Qml] public double Y1 { get { Track(); return _y1; } set => Set(ref _y1, value); }

    [Qml] public double X2 { get { Track(); return _x2; } set => Set(ref _x2, value); }

    [Qml] public double Y2 { get { Track(); return _y2; } set => Set(ref _y2, value); }

    public override SKShader CreateShader()
    {
        var (colors, positions) = StopArrays();
        return SKShader.CreateLinearGradient(new SKPoint((float)_x1, (float)_y1), new SKPoint((float)_x2, (float)_y2), colors, positions, TileMode);
    }
}

/// <summary>QtQuick.Shapes' <c>RadialGradient</c>: from a focal circle to a center circle.</summary>
public sealed class RadialGradient : ShapeGradient
{
    private double _centerX, _centerY, _centerRadius, _focalX, _focalY, _focalRadius;

    [Qml] public double CenterX { get { Track(); return _centerX; } set => Set(ref _centerX, value); }

    [Qml] public double CenterY { get { Track(); return _centerY; } set => Set(ref _centerY, value); }

    [Qml] public double CenterRadius { get { Track(); return _centerRadius; } set => Set(ref _centerRadius, value); }

    [Qml] public double FocalX { get { Track(); return _focalX; } set => Set(ref _focalX, value); }

    [Qml] public double FocalY { get { Track(); return _focalY; } set => Set(ref _focalY, value); }

    [Qml] public double FocalRadius { get { Track(); return _focalRadius; } set => Set(ref _focalRadius, value); }

    public override SKShader CreateShader()
    {
        var (colors, positions) = StopArrays();
        return SKShader.CreateTwoPointConicalGradient(
            new SKPoint((float)_focalX, (float)_focalY), (float)_focalRadius,
            new SKPoint((float)_centerX, (float)_centerY), (float)Math.Max(0.01, _centerRadius),
            colors, positions, TileMode);
    }
}

/// <summary>QtQuick.Shapes' <c>ConicalGradient</c>: around a center, counterclockwise from an angle.</summary>
public sealed class ConicalGradient : ShapeGradient
{
    private double _centerX, _centerY, _angle;

    [Qml] public double CenterX { get { Track(); return _centerX; } set => Set(ref _centerX, value); }

    [Qml] public double CenterY { get { Track(); return _centerY; } set => Set(ref _centerY, value); }

    [Qml] public double Angle { get { Track(); return _angle; } set => Set(ref _angle, value); }

    public override SKShader CreateShader()
    {
        var (colors, positions) = StopArrays();
        var cx = (float)_centerX;
        var cy = (float)_centerY;
        // Qt turns counterclockwise from the angle; Skia's sweep turns clockwise from 3 o'clock.
        var matrix = SKMatrix.CreateRotationDegrees(-(float)_angle, cx, cy).PreConcat(SKMatrix.CreateScale(1, -1, cx, cy));
        return SKShader.CreateSweepGradient(new SKPoint(cx, cy), colors, positions, matrix);
    }
}

/// <summary>QtQuick.Shapes' <c>ShapePath</c>: one path of a shape, with its stroke and fill.</summary>
[QmlDefaultProperty("pathElements")]
public sealed class ShapePath : QObject
{
    private QColor _strokeColor = QColor.White;
    private double _strokeWidth = 1;
    private QColor _fillColor = QColor.White;
    private ShapeGradient? _fillGradient;
    private int _fillRule;
    private int _joinStyle = (int)JoinStyleValue.BevelJoin;
    private double _miterLimit = 2;
    private int _capStyle = (int)CapStyleValue.SquareCap;
    private int _strokeStyle = (int)StrokeStyleValue.SolidLine;
    private double _dashOffset;
    private object? _dashPattern;
    private double _startX, _startY;
    private int _pathHints;
    private QSize _scale = new(1, 1);
    private SKPath? _path;

    [QmlEnum]
    public enum FillRuleValue
    {
        OddEvenFill = 0,
        WindingFill = 1,
    }

    [QmlEnum]
    public enum JoinStyleValue
    {
        MiterJoin = 0,
        BevelJoin = 0x40,
        RoundJoin = 0x80,
    }

    [QmlEnum]
    public enum CapStyleValue
    {
        FlatCap = 0,
        SquareCap = 0x10,
        RoundCap = 0x20,
    }

    [QmlEnum]
    public enum StrokeStyleValue
    {
        SolidLine = 1,
        DashLine = 2,
    }

    [QmlEnum]
    public enum PathHint
    {
        PathLinear = 1,
        PathQuadratic = 2,
        PathConvex = 4,
        PathFillOnRight = 8,
        PathSolid = 16,
        PathNonIntersecting = 32,
        PathNonOverlappingControlPointTriangles = 64,
    }

    public ShapePath()
    {
        PathElements = new QmlList<PathElement>(this, "pathElements", added: e =>
        {
            e.SetOwner(this);
            e.Changed += InvalidatePath;
            InvalidatePath();
        }, removed: e =>
        {
            e.Changed -= InvalidatePath;
            InvalidatePath();
        });
    }

    public event Action? Changed;

    private void Raise()
    {
        Changed?.Invoke();
        (Owner as Item)?.Update();
        (Owner as Shape)?.PathChanged();
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
    {
        if (SetField(ref field, value, property))
            Raise();
    }

    /// <summary>Sets a field the path is built from, so the path is built again.</summary>
    private void SetPath<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
    {
        if (SetField(ref field, value, property))
            InvalidatePath();
    }

    private void InvalidatePath()
    {
        _path?.Dispose();
        _path = null;
        Raise();
    }

    [Qml] public QmlList<PathElement> PathElements { get; }

    [Qml] public QColor StrokeColor { get { Track(); return _strokeColor; } set => Set(ref _strokeColor, value); }

    [Qml] public double StrokeWidth { get { Track(); return _strokeWidth; } set => Set(ref _strokeWidth, value); }

    [Qml] public QColor FillColor { get { Track(); return _fillColor; } set => Set(ref _fillColor, value); }

    [Qml]
    public ShapeGradient? FillGradient
    {
        get { Track(); return _fillGradient; }
        set
        {
            if (_fillGradient == value)
                return;
            if (_fillGradient is not null)
                _fillGradient.Changed -= Raise;
            _fillGradient = value;
            if (value is not null)
                value.Changed += Raise;
            NotifyChanged("fillGradient");
            Raise();
        }
    }

    [Qml] public int FillRule { get { Track(); return _fillRule; } set => SetPath(ref _fillRule, value); }

    [Qml] public int JoinStyle { get { Track(); return _joinStyle; } set => Set(ref _joinStyle, value); }

    [Qml] public double MiterLimit { get { Track(); return _miterLimit; } set => Set(ref _miterLimit, value); }

    [Qml] public int CapStyle { get { Track(); return _capStyle; } set => Set(ref _capStyle, value); }

    [Qml] public int StrokeStyle { get { Track(); return _strokeStyle; } set => Set(ref _strokeStyle, value); }

    [Qml] public double DashOffset { get { Track(); return _dashOffset; } set => Set(ref _dashOffset, value); }

    [Qml]
    public object? DashPattern
    {
        get { Track(); return _dashPattern ?? new List<object?> { 4.0, 2.0 }; }
        set
        {
            _dashPattern = value is Jint.Native.JsValue js ? ValueConverter.FromJs(js) : value;
            NotifyChanged("dashPattern");
            Raise();
        }
    }

    [Qml] public double StartX { get { Track(); return _startX; } set => SetPath(ref _startX, value); }

    [Qml] public double StartY { get { Track(); return _startY; } set => SetPath(ref _startY, value); }

    [Qml] public int PathHints { get { Track(); return _pathHints; } set => Set(ref _pathHints, value); }

    [Qml] public QSize Scale { get { Track(); return _scale; } set => SetPath(ref _scale, value); }

    [Qml] public Item? FillItem { get; set; }

    [Qml] public object? FillTransform { get; set; }

    /// <summary>The path its elements make, from the start point. Built once per change; callers don't dispose it.</summary>
    internal SKPath Path => _path ??= BuildPath();

    internal int PathBuilds { get; private set; }

    private SKPath BuildPath()
    {
        PathBuilds++;
        using var builder = new SKPathBuilder();
        builder.FillType = _fillRule == (int)FillRuleValue.WindingFill ? SKPathFillType.Winding : SKPathFillType.EvenOdd;
        var cursor = new PathCursor(builder);
        cursor.MoveTo((float)_startX, (float)_startY);
        foreach (var element in PathElements.Snapshot)
            element.AddTo(cursor);
        var path = builder.Detach();
        if (_scale.Width != 1 || _scale.Height != 1)
        {
            var m = SKMatrix.CreateScale((float)_scale.Width, (float)_scale.Height);
            path.Transform(in m);
        }
        return path;
    }

    public void Draw(SKCanvas canvas, bool antialias)
    {
        var path = Path;
        if (_fillGradient is not null || _fillColor.A > 0)
        {
            using var fill = new SKPaint { IsAntialias = antialias, Style = SKPaintStyle.Fill };
            if (_fillGradient is not null)
                fill.Shader = _fillGradient.CreateShader();
            else
                fill.Color = _fillColor.ToSk();
            canvas.DrawPath(path, fill);
            fill.Shader?.Dispose();
        }
        if (_strokeWidth > 0 && _strokeColor.A > 0)
        {
            using var stroke = new SKPaint
            {
                IsAntialias = antialias,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)_strokeWidth,
                Color = _strokeColor.ToSk(),
                StrokeMiter = (float)_miterLimit,
                StrokeCap = _capStyle switch
                {
                    (int)CapStyleValue.RoundCap => SKStrokeCap.Round,
                    (int)CapStyleValue.SquareCap => SKStrokeCap.Square,
                    _ => SKStrokeCap.Butt,
                },
                StrokeJoin = _joinStyle switch
                {
                    (int)JoinStyleValue.RoundJoin => SKStrokeJoin.Round,
                    (int)JoinStyleValue.MiterJoin => SKStrokeJoin.Miter,
                    _ => SKStrokeJoin.Bevel,
                },
            };
            if (_strokeStyle == (int)StrokeStyleValue.DashLine)
            {
                // The dash pattern is in units of the stroke width, as in Qt.
                var w = (float)Math.Max(_strokeWidth, 1);
                var intervals = ValueConverter.ToList(DashPattern).Select(v => (float)ValueConverter.ToDouble(v, "real") * w).Where(v => v >= 0).ToList();
                if (intervals.Count % 2 == 1)
                    intervals.AddRange(intervals);
                if (intervals.Count >= 2 && intervals.Sum() > 0)
                    stroke.PathEffect = SKPathEffect.CreateDash(intervals.ToArray(), (float)_dashOffset * w);
            }
            canvas.DrawPath(path, stroke);
            stroke.PathEffect?.Dispose();
        }
    }

    protected override void OnDestroy()
    {
        _path?.Dispose();
        _path = null;
    }
}

/// <summary>QtQuick.Shapes' <c>Shape</c>: draws its ShapePaths.</summary>
public sealed class Shape : Item
{
    private int _containsMode;
    private int _preferredRendererType;
    private bool _asynchronous;
    private bool _vendorExtensionsEnabled = true;
    private int _fillMode;

    [QmlEnum]
    public enum RendererTypeValue
    {
        UnknownRenderer = 0,
        GeometryRenderer = 1,
        NvprRenderer = 2,
        SoftwareRenderer = 3,
        CurveRenderer = 4,
    }

    [QmlEnum]
    public enum StatusValue
    {
        Null = 0,
        Ready = 1,
        Processing = 2,
    }

    [QmlEnum]
    public enum ContainsModeValue
    {
        BoundingRectContains = 0,
        FillContains = 1,
    }

    [QmlEnum]
    public enum FillModeValue
    {
        NoResize = 0,
        PreserveAspectFit = 1,
        PreserveAspectCrop = 2,
        Stretch = 3,
    }

    [Qml] public int ContainsMode { get { Track(); return _containsMode; } set => SetField(ref _containsMode, value); }

    [Qml] public int PreferredRendererType { get { Track(); return _preferredRendererType; } set => SetField(ref _preferredRendererType, value); }

    [Qml] public int RendererType => (int)RendererTypeValue.SoftwareRenderer;

    [Qml] public bool Asynchronous { get { Track(); return _asynchronous; } set => SetField(ref _asynchronous, value); }

    [Qml] public bool VendorExtensionsEnabled { get { Track(); return _vendorExtensionsEnabled; } set => SetField(ref _vendorExtensionsEnabled, value); }

    [Qml] public int Status => (int)StatusValue.Ready;

    [Qml] public int FillMode { get { Track(); return _fillMode; } set { if (SetField(ref _fillMode, value)) Update(); } }

    [Qml] public int HorizontalAlignment { get; set; } = QtEnums.AlignLeft;

    [Qml] public int VerticalAlignment { get; set; } = QtEnums.AlignTop;

    [Qml]
    public QRect BoundingRect
    {
        get
        {
            TrackQml("boundingRect");
            var b = Bounds();
            return new QRect(b.Left, b.Top, b.Width, b.Height);
        }
    }

    private IEnumerable<ShapePath> Paths => Resources.Snapshot.OfType<ShapePath>();

    private SKRect Bounds()
    {
        var bounds = SKRect.Empty;
        foreach (var p in Paths)
        {
            var b = p.Path.Bounds;
            var half = (float)Math.Max(0, p.StrokeWidth) / 2;
            if (p.StrokeColor.A > 0 && p.StrokeWidth > 0)
                b.Inflate(half, half);
            bounds = bounds.IsEmpty ? b : SKRect.Union(bounds, b);
        }
        return bounds;
    }

    internal void PathChanged()
    {
        if (!IsComplete)
            return;
        UpdateImplicitSize();
        NotifyChanged("boundingRect");
    }

    private void UpdateImplicitSize()
    {
        using var _ = DependencyTracker.Suspend();
        var b = Bounds();
        ImplicitWidth = Math.Max(0, b.Right);
        ImplicitHeight = Math.Max(0, b.Bottom);
    }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        UpdateImplicitSize();
    }

    public override bool HitTest(double x, double y)
    {
        if (_containsMode != (int)ContainsModeValue.FillContains)
            return base.HitTest(x, y);
        if (!FillMatrix().TryInvert(out var inverse))
            return false;
        var point = inverse.MapPoint((float)x, (float)y);
        foreach (var p in Paths)
        {
            if (p.Path.Contains(point.X, point.Y))
                return true;
        }
        return false;
    }

    /// <summary>How <c>fillMode</c> scales the paths into the item.</summary>
    private SKMatrix FillMatrix()
    {
        if (_fillMode == (int)FillModeValue.NoResize)
            return SKMatrix.Identity;
        var b = Bounds();
        if (b.Width <= 0 || b.Height <= 0 || Width <= 0 || Height <= 0)
            return SKMatrix.Identity;
        var sx = (float)(Width / b.Width);
        var sy = (float)(Height / b.Height);
        if (_fillMode == (int)FillModeValue.PreserveAspectFit)
            sx = sy = Math.Min(sx, sy);
        else if (_fillMode == (int)FillModeValue.PreserveAspectCrop)
            sx = sy = Math.Max(sx, sy);
        return SKMatrix.CreateScale(sx, sy).PreConcat(SKMatrix.CreateTranslation(-b.Left, -b.Top));
    }

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        var paths = Paths.ToList();
        if (paths.Count == 0)
            return;
        canvas.Save();
        var m = FillMatrix();
        canvas.Concat(in m);
        foreach (var p in paths)
            p.Draw(canvas, Antialiasing);
        canvas.Restore();
    }
}
