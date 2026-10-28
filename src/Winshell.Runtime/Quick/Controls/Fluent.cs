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
    /// shading to <paramref name="bottomStroke"/> along the bottom edge, the way WinUI's buttons and fields look.
    /// </summary>
    public static void ElevatedRect(SKCanvas canvas, SKRect rect, float radius, QColor fill, QColor stroke, QColor bottomStroke)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        using (var rr = Round(rect, radius))
        using (var paint = new SKPaint { Color = fill.ToSk(), IsAntialias = true })
        {
            if (fill.A > 0)
                canvas.DrawRoundRect(rr, paint);
        }
        if (stroke.A <= 0 && bottomStroke.A <= 0)
            return;
        var inner = SKRect.Inflate(rect, -0.5f, -0.5f);
        using var border = Round(inner, Math.Max(0, radius - 0.5f));
        var h = rect.Height;
        using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        if (h > 4 && stroke != bottomStroke)
        {
            strokePaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
                [stroke.ToSk(), stroke.ToSk(), bottomStroke.ToSk(), bottomStroke.ToSk()],
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
