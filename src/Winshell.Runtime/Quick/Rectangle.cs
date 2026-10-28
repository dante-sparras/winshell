using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>What painting knows besides the canvas.</summary>
public sealed class PaintContext
{
    public double DevicePixelRatio { get; init; } = 1;

    /// <summary>The time of this frame, for items that animate while they draw.</summary>
    public TimeSpan Time { get; init; }

    /// <summary>Set by an item whose drawing keeps changing, such as a busy indicator, to get another frame.</summary>
    public bool WantsAnotherFrame { get; set; }
}

public static class SkiaExtensions
{
    public static SKColor ToSk(this QColor c) => new(c.R8, c.G8, c.B8, c.A8);

    public static QColor ToQColor(this SKColor c) => QColor.FromRgb(c.Red, c.Green, c.Blue, c.Alpha);
}

/// <summary>QtQuick's <c>Rectangle</c>.</summary>
public class Rectangle : Item
{
    private QColor _color = QColor.White;
    private double _radius;
    private double? _topLeftRadius, _topRightRadius, _bottomLeftRadius, _bottomRightRadius;
    private Gradient? _gradient;

    public Rectangle()
    {
        Border = new BorderGroup(this);
    }

    [Qml]
    public QColor Color
    {
        get { Track(); return _color; }
        set
        {
            if (SetField(ref _color, value))
                Update();
        }
    }

    [Qml]
    public double Radius
    {
        get { Track(); return _radius; }
        set
        {
            if (SetField(ref _radius, value))
                Update();
        }
    }

    [Qml]
    public double TopLeftRadius { get { Track(); return _topLeftRadius ?? _radius; } set { _topLeftRadius = value; NotifyChanged("topLeftRadius"); Update(); } }

    [Qml]
    public double TopRightRadius { get { Track(); return _topRightRadius ?? _radius; } set { _topRightRadius = value; NotifyChanged("topRightRadius"); Update(); } }

    [Qml]
    public double BottomLeftRadius { get { Track(); return _bottomLeftRadius ?? _radius; } set { _bottomLeftRadius = value; NotifyChanged("bottomLeftRadius"); Update(); } }

    [Qml]
    public double BottomRightRadius { get { Track(); return _bottomRightRadius ?? _radius; } set { _bottomRightRadius = value; NotifyChanged("bottomRightRadius"); Update(); } }

    [Qml]
    public BorderGroup Border { get; }

    [Qml]
    public Gradient? Gradient
    {
        get { Track(); return _gradient; }
        set
        {
            if (_gradient == value)
                return;
            _gradient = value;
            _gradient?.Changed.Add(Update);
            NotifyChanged("gradient");
            Update();
        }
    }

    /// <summary>The outline of the rectangle, with its corners.</summary>
    public SKRoundRect Shape(double inset = 0)
    {
        var w = Width;
        var h = Height;
        var rect = new SKRect((float)inset, (float)inset, (float)(w - inset), (float)(h - inset));
        var max = Math.Min(w, h) / 2;
        float R(double r) => (float)Math.Max(0, Math.Min(r, max) - inset);
        var rr = new SKRoundRect();
        rr.SetRectRadii(rect,
        [
            new SKPoint(R(TopLeftRadius), R(TopLeftRadius)),
            new SKPoint(R(TopRightRadius), R(TopRightRadius)),
            new SKPoint(R(BottomRightRadius), R(BottomRightRadius)),
            new SKPoint(R(BottomLeftRadius), R(BottomLeftRadius)),
        ]);
        return rr;
    }

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        if (Width <= 0 || Height <= 0)
            return;
        var borderWidth = Border.IsVisible ? Border.Width : 0;
        using var fill = new SKPaint { IsAntialias = Antialiasing, Style = SKPaintStyle.Fill };
        var gradient = _gradient;
        if (gradient is not null && gradient.Stops.Count > 0)
        {
            fill.Shader = gradient.CreateShader(Width, Height);
        }
        else
        {
            fill.Color = _color.ToSk();
        }
        if (fill.Shader is not null || _color.A > 0)
        {
            // The fill goes under the border, so a half-transparent border does not show a seam.
            using var shape = Shape(borderWidth > 0 && Border.Color.A >= 1 ? borderWidth / 2 : 0);
            canvas.DrawRoundRect(shape, fill);
        }
        if (borderWidth > 0)
        {
            using var stroke = new SKPaint
            {
                IsAntialias = Antialiasing,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)borderWidth,
                Color = Border.Color.ToSk(),
            };
            using var shape = Shape(borderWidth / 2);
            canvas.DrawRoundRect(shape, stroke);
        }
    }

    public override SKRoundRect? ClipShape => Shape();
}

/// <summary>A rectangle's <c>border</c> group.</summary>
public sealed class BorderGroup(Item owner) : QObject
{
    private double _width = 1;
    private QColor _color = QColor.Black;
    private bool _widthSet;
    private bool _colorSet;
    private bool _pixelAligned = true;

    /// <summary>A border shows once its width or color is set, like Qt's.</summary>
    public bool IsVisible => (_widthSet || _colorSet) && _width > 0 && _color.A > 0;

    [Qml]
    public double Width
    {
        get { Track(); return _width; }
        set
        {
            _widthSet = true;
            if (SetField(ref _width, value))
                owner.Update();
            owner.Update();
        }
    }

    [Qml]
    public QColor Color
    {
        get { Track(); return _color; }
        set
        {
            _colorSet = true;
            if (SetField(ref _color, value))
                owner.Update();
            owner.Update();
        }
    }

    [Qml]
    public bool PixelAligned
    {
        get { Track(); return _pixelAligned; }
        set => SetField(ref _pixelAligned, value);
    }
}

/// <summary>QtQuick's <c>Gradient</c>.</summary>
[QmlDefaultProperty("stops")]
public sealed class Gradient : QObject
{
    private int _orientation = (int)OrientationValue.Vertical;

    public Gradient()
    {
        Stops = new QmlList<GradientStop>(this, "stops", added: s =>
        {
            s.SetOwner(this);
            s.Gradient = this;
            Notify();
        }, removed: _ => Notify());
    }

    [QmlEnum]
    public enum OrientationValue
    {
        Horizontal = 1,
        Vertical = 2,
    }

    internal List<Action> Changed { get; } = [];

    internal void Notify()
    {
        foreach (var c in Changed)
            c();
    }

    [Qml]
    public QmlList<GradientStop> Stops { get; }

    [Qml]
    public int Orientation
    {
        get { Track(); return _orientation; }
        set
        {
            if (SetField(ref _orientation, value))
                Notify();
        }
    }

    public SKShader CreateShader(double width, double height)
    {
        var stops = Stops.Snapshot.OrderBy(s => s.Position).ToArray();
        var colors = stops.Select(s => s.Color.ToSk()).ToArray();
        var positions = stops.Select(s => (float)Math.Clamp(s.Position, 0, 1)).ToArray();
        var end = _orientation == (int)OrientationValue.Horizontal ? new SKPoint((float)width, 0) : new SKPoint(0, (float)height);
        return SKShader.CreateLinearGradient(new SKPoint(0, 0), end, colors, positions, SKShaderTileMode.Clamp);
    }
}

public sealed class GradientStop : QObject
{
    private double _position;
    private QColor _color = QColor.Black;

    internal Gradient? Gradient { get; set; }

    [Qml]
    public double Position
    {
        get { Track(); return _position; }
        set
        {
            if (SetField(ref _position, value))
                Gradient?.Notify();
        }
    }

    [Qml]
    public QColor Color
    {
        get { Track(); return _color; }
        set
        {
            if (SetField(ref _color, value))
                Gradient?.Notify();
        }
    }
}
