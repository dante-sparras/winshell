using SkiaSharp;

namespace Winshell.Runtime.Quick.Controls;

/// <summary>A step of the Windows 11 type ramp, in Segoe UI Variable.</summary>
public readonly record struct TypeStyle(double PixelSize, int Weight);

/// <summary>
/// The Windows 11 Fluent look the default control visuals share: sizes, corner radii, the type ramp, motion, the
/// elevation border of buttons and fields, and the Segoe Fluent Icons glyphs. The values and names are WinUI 3's.
/// </summary>
public static class Fluent
{
    public const double ControlHeight = 32;
    public const float ControlCornerRadius = 4;
    public const float OverlayCornerRadius = 8;

    public static readonly TypeStyle Caption = new(12, 400);
    public static readonly TypeStyle Body = new(14, 400);
    public static readonly TypeStyle BodyStrong = new(14, 600);
    public static readonly TypeStyle BodyLarge = new(18, 400);
    public static readonly TypeStyle Subtitle = new(20, 600);
    public static readonly TypeStyle Title = new(28, 600);
    public static readonly TypeStyle TitleLarge = new(40, 600);
    public static readonly TypeStyle Display = new(68, 600);

    /// <summary>Milliseconds a control takes for a state change, such as a color on hover.</summary>
    public const int ControlFasterAnimationDuration = 83;

    /// <summary>Milliseconds a control takes for a short move, such as a toggle switch's knob.</summary>
    public const int ControlFastAnimationDuration = 167;

    /// <summary>Milliseconds a control takes for a longer move, such as a flyout opening.</summary>
    public const int ControlNormalAnimationDuration = 250;

    /// <summary>The cubic Bézier control points of the curve WinUI's controls move along: fast out, slow in.</summary>
    public static readonly (double X1, double Y1, double X2, double Y2) ControlFastOutSlowInKeySpline = (0, 0, 0, 1);

    public const string CheckMark = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string ChevronRight = "";
    public const string ChevronLeft = "";
    public const string CaretLeftSolid = "\uEDD9";
    public const string CaretRightSolid = "\uEDDA";
    public const string CaretUpSolid = "\uEDDB";
    public const string CaretDownSolid = "\uEDDC";
    public const string Cancel = "\uE894";

    public static Theme T => Theme.Current;

    private static SKTypeface? _iconFont;
    private static bool _iconFontSearched;

    /// <summary>Segoe Fluent Icons, or Segoe MDL2 Assets on systems without it, or null.</summary>
    public static SKTypeface? IconFont
    {
        get
        {
            if (_iconFontSearched)
                return _iconFont;
            _iconFontSearched = true;
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                var tf = SKFontManager.Default.MatchFamily(name);
                if (tf is not null && string.Equals(tf.FamilyName, name, StringComparison.OrdinalIgnoreCase))
                {
                    _iconFont = tf;
                    break;
                }
            }
            return _iconFont;
        }
    }

    /// <summary>Draws an icon glyph centered on a point. Returns false when no icon font is installed.</summary>
    public static bool DrawGlyph(SKCanvas canvas, string glyph, float cx, float cy, float size, QColor color)
    {
        var tf = IconFont;
        if (tf is null)
            return false;
        using var font = new SKFont(tf, size) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { Color = color.ToSk(), IsAntialias = true };
        var advance = font.MeasureText(glyph, out _);
        // The glyphs sit on an em-square: center that square, as XAML's FontIcon does.
        font.GetFontMetrics(out var metrics);
        var baseline = cy - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(glyph, cx - advance / 2, baseline, SKTextAlign.Left, font, paint);
        return true;
    }

    /// <summary>A check mark drawn as a path, for systems without the icon font.</summary>
    public static void DrawCheck(SKCanvas canvas, float cx, float cy, float size, QColor color)
    {
        if (DrawGlyph(canvas, CheckMark, cx, cy, size, color))
            return;
        using var paint = new SKPaint { Color = color.ToSk(), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size / 8, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var builder = new SKPathBuilder();
        builder.MoveTo(cx - size * 0.38f, cy + size * 0.02f);
        builder.LineTo(cx - size * 0.12f, cy + size * 0.28f);
        builder.LineTo(cx + size * 0.4f, cy - size * 0.26f);
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    /// <summary>A chevron drawn with the icon font, or as two lines without it.</summary>
    public static void DrawChevron(SKCanvas canvas, string glyph, float cx, float cy, float size, QColor color)
    {
        if (DrawGlyph(canvas, glyph, cx, cy, size, color))
            return;
        using var paint = new SKPaint { Color = color.ToSk(), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, StrokeCap = SKStrokeCap.Round };
        var s = size / 4;
        using var path = new SKPathBuilder();
        switch (glyph)
        {
            case ChevronUp:
                path.MoveTo(cx - s, cy + s / 2);
                path.LineTo(cx, cy - s / 2);
                path.LineTo(cx + s, cy + s / 2);
                break;
            case ChevronRight:
                path.MoveTo(cx - s / 2, cy - s);
                path.LineTo(cx + s / 2, cy);
                path.LineTo(cx - s / 2, cy + s);
                break;
            case ChevronLeft:
                path.MoveTo(cx + s / 2, cy - s);
                path.LineTo(cx - s / 2, cy);
                path.LineTo(cx + s / 2, cy + s);
                break;
            default:
                path.MoveTo(cx - s, cy - s / 2);
                path.LineTo(cx, cy + s / 2);
                path.LineTo(cx + s, cy - s / 2);
                break;
        }
        using var built = path.Detach();
        canvas.DrawPath(built, paint);
    }

    public static SKRoundRect Round(SKRect rect, float radius) => new(rect, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));

    /// <summary>
    /// Fills a rounded rectangle and draws the Fluent elevation border: one pixel of <paramref name="stroke"/>,
    /// shading to <paramref name="edgeStroke"/> over three pixels at the bottom edge, or at the top edge when
    /// <paramref name="edgeOnTop"/>. That is WinUI's <c>ControlElevationBorderBrush</c>: its edge is at the top in
    /// dark and at the bottom in light.
    /// </summary>
    public static void ElevatedRect(SKCanvas canvas, SKRect rect, float radius, QColor fill, QColor stroke, QColor edgeStroke, bool edgeOnTop = false)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        using (var rr = Round(rect, radius))
        using (var paint = new SKPaint { Color = fill.ToSk(), IsAntialias = true })
        {
            if (fill.A > 0)
                canvas.DrawRoundRect(rr, paint);
        }
        if (stroke.A <= 0 && edgeStroke.A <= 0)
            return;
        var inner = SKRect.Inflate(rect, -0.5f, -0.5f);
        using var border = Round(inner, Math.Max(0, radius - 0.5f));
        var h = rect.Height;
        using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        if (h > 4 && stroke != edgeStroke)
        {
            strokePaint.Shader = edgeOnTop
                ? SKShader.CreateLinearGradient(
                    new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
                    [edgeStroke.ToSk(), edgeStroke.ToSk(), stroke.ToSk(), stroke.ToSk()],
                    [0, 1 / h, 3 / h, 1],
                    SKShaderTileMode.Clamp)
                : SKShader.CreateLinearGradient(
                    new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
                    [stroke.ToSk(), stroke.ToSk(), edgeStroke.ToSk(), edgeStroke.ToSk()],
                    [0, (h - 3) / h, (h - 1) / h, 1],
                    SKShaderTileMode.Clamp);
        }
        else
        {
            strokePaint.Color = stroke.ToSk();
        }
        canvas.DrawRoundRect(border, strokePaint);
        strokePaint.Shader?.Dispose();
    }

    /// <summary>WinUI's check box mark, drawn as a path in a 20 pixel box.</summary>
    public static void DrawCheckMark(SKCanvas canvas, SKRect box, QColor color, float progress = 1)
    {
        if (color.A <= 0 || progress <= 0)
            return;
        float s = box.Width / 20;
        using var builder = new SKPathBuilder();
        builder.MoveTo(box.Left + 5.5f * s, box.Top + 10.5f * s);
        builder.LineTo(box.Left + 8.2f * s, box.Top + 13.2f * s);
        builder.LineTo(box.Left + 14.5f * s, box.Top + 7f * s);
        using var path = builder.Detach();
        using var paint = new SKPaint { Color = color.ToSk(), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f * s, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        if (progress >= 1)
        {
            canvas.DrawPath(path, paint);
            return;
        }
        // The mark draws itself in from its left end, as WinUI's animated check does.
        using var measure = new SKPathMeasure(path);
        using var segment = new SKPathBuilder();
        if (!measure.GetSegment(0, measure.Length * progress, segment, true))
            return;
        using var partial = segment.Detach();
        canvas.DrawPath(partial, paint);
    }

    public static void FillRound(SKCanvas canvas, SKRect rect, float radius, QColor color)
    {
        if (color.A <= 0 || rect.Width <= 0 || rect.Height <= 0)
            return;
        using var rr = Round(rect, radius);
        using var paint = new SKPaint { Color = color.ToSk(), IsAntialias = true };
        canvas.DrawRoundRect(rr, paint);
    }

    public static void StrokeRound(SKCanvas canvas, SKRect rect, float radius, QColor color, float width = 1)
    {
        if (color.A <= 0 || rect.Width <= 0 || rect.Height <= 0)
            return;
        var inner = SKRect.Inflate(rect, -width / 2, -width / 2);
        using var rr = Round(inner, Math.Max(0, radius - width / 2));
        using var paint = new SKPaint { Color = color.ToSk(), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
        canvas.DrawRoundRect(rr, paint);
    }

    public static void FillCircle(SKCanvas canvas, float cx, float cy, float radius, QColor color)
    {
        if (color.A <= 0 || radius <= 0)
            return;
        using var paint = new SKPaint { Color = color.ToSk(), IsAntialias = true };
        canvas.DrawCircle(cx, cy, radius, paint);
    }

    public static void StrokeCircle(SKCanvas canvas, float cx, float cy, float radius, QColor color, float width = 1)
    {
        if (color.A <= 0 || radius <= 0)
            return;
        using var paint = new SKPaint { Color = color.ToSk(), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
        canvas.DrawCircle(cx, cy, radius - width / 2, paint);
    }

    /// <summary>
    /// The one pixel border of a raised circle, such as a slider thumb: WinUI's <c>CircleElevationBorderBrush</c>,
    /// a shade darker over the bottom third. A contrast theme draws it in the button text color.
    /// </summary>
    public static void StrokeCircleElevation(SKCanvas canvas, float cx, float cy, float radius, Theme t)
    {
        if (t.IsHighContrast)
        {
            StrokeCircle(canvas, cx, cy, radius, t.SystemColorButtonTextColor);
            return;
        }
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(cx, cy - radius), new SKPoint(cx, cy + radius),
            [t.ControlStrokeColorDefault.ToSk(), t.ControlStrokeColorDefault.ToSk(), t.ControlStrokeColorSecondary.ToSk(), t.ControlStrokeColorSecondary.ToSk()],
            [0, 0.5f, 0.7f, 1],
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawCircle(cx, cy, radius - 0.5f, paint);
    }

    /// <summary>The soft shadow under a flyout, menu or tooltip.</summary>
    public static void Shadow(SKCanvas canvas, SKRect rect, float radius, Elevation elevation)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || elevation.Color.A <= 0)
            return;
        using var paint = new SKPaint
        {
            Color = elevation.Color.ToSk(),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, elevation.Blur / 2),
        };
        rect.Offset(0, elevation.OffsetY);
        using var rr = Round(rect, radius);
        canvas.DrawRoundRect(rr, paint);
    }

    /// <summary>Draws the two-ring keyboard focus visual around a control.</summary>
    public static void FocusRing(SKCanvas canvas, SKRect rect, float radius)
    {
        var outer = SKRect.Inflate(rect, 3, 3);
        StrokeRound(canvas, outer, radius + 3, T.FocusStrokeColorOuter, 2);
        StrokeRound(canvas, SKRect.Inflate(outer, -2, -2), radius + 1, T.FocusStrokeColorInner, 1);
    }
}

/// <summary>
/// The colors of one part of a control in each visual state, as WinUI's <c>…</c>, <c>…PointerOver</c>,
/// <c>…Pressed</c> and <c>…Disabled</c> resources give them.
/// </summary>
public readonly record struct StateColors(QColor Rest, QColor Hover, QColor Pressed, QColor Disabled)
{
    public QColor For(bool enabled, bool pressed, bool hovered) =>
        !enabled ? Disabled : pressed ? Pressed : hovered ? Hover : Rest;
}

/// <summary>
/// A painted value that eases to a new target instead of jumping to it, as a WinUI visual state transition does.
/// It starts moving on the first paint after the target changes and asks for frames until it arrives. Without a
/// clock (a paint at time zero) it jumps.
/// </summary>
public abstract class Transition<T>(int milliseconds, bool easeOut) where T : struct
{
    private static readonly double[] FastOutSlowIn = [0, 0, 0, 1, 1, 1];
    private T _from, _to;
    private TimeSpan _start;
    private bool _started;

    protected abstract T Lerp(T from, T to, double t);

    /// <summary>Moves to a value at once, so the next change eases from there.</summary>
    public void Jump(T value)
    {
        _started = true;
        _from = _to = value;
        _start = TimeSpan.MinValue;
    }

    public T Step(T target, PaintContext context)
    {
        var now = context.Time;
        if (!_started || now <= TimeSpan.Zero)
        {
            Jump(target);
            return target;
        }
        if (!EqualityComparer<T>.Default.Equals(target, _to))
        {
            _from = At(now);
            _to = target;
            _start = now;
        }
        var value = At(now);
        if (Progress(now) < 1)
            context.WantsAnotherFrame = true;
        return value;
    }

    private double Progress(TimeSpan now) =>
        milliseconds <= 0 || _start == TimeSpan.MinValue ? 1 : Math.Clamp((now - _start).TotalMilliseconds / milliseconds, 0, 1);

    private T At(TimeSpan now)
    {
        var p = Progress(now);
        return Lerp(_from, _to, easeOut ? Easing.Value(Easing.BezierSpline, p, bezier: FastOutSlowIn) : p);
    }
}

/// <summary>A number that eases to its target along WinUI's fast-out, slow-in curve.</summary>
public sealed class NumberTransition(int milliseconds) : Transition<double>(milliseconds, easeOut: true)
{
    protected override double Lerp(double from, double to, double t) => from + (to - from) * t;
}

/// <summary>A color that fades to its target linearly, as WinUI's brush transitions do.</summary>
public sealed class ColorTransition(int milliseconds) : Transition<QColor>(milliseconds, easeOut: false)
{
    protected override QColor Lerp(QColor from, QColor to, double t) => QColor.Blend(from, to, t);
}

/// <summary>Watches some properties of an object and runs an action when one changes.</summary>
public sealed class PropertyWatch : IPropertyObserver, IDisposable
{
    private readonly QObject _target;
    private readonly string[] _names;
    private readonly Action _changed;
    private bool _disposed;

    public PropertyWatch(QObject target, Action changed, params string[] names)
    {
        _target = target;
        _names = names;
        _changed = changed;
        foreach (var name in names)
            target.AddObserver(name, this);
    }

    public QObject Target => _target;

    public void OnPropertyChanged(QObject sender, string property)
    {
        if (!_disposed)
            _changed();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var name in _names)
            _target.RemoveObserver(name, this);
    }
}
