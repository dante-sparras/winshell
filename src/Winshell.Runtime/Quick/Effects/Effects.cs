using SkiaSharp;

namespace Winshell.Runtime.Quick.Effects;

/// <summary>Draws an item's subtree into a picture, for an effect that takes it as its source.</summary>
public static class EffectSource
{
    /// <summary>How far outside an item's bounds a recording keeps drawing, for shadows and glows.</summary>
    public const float Margin = 512;

    /// <summary>Records an item and its children in the item's coordinates, even when the item is hidden.</summary>
    public static SKPicture? Record(Item? source, PaintContext context)
    {
        if (source is null || source.IsDestroyed)
            return null;
        return Record(source.Width, source.Height, canvas => PaintSubtree(source, canvas, context));
    }

    public static SKPicture Record(double width, double height, Action<SKCanvas> paint)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create(-Margin, -Margin, (float)width + 2 * Margin, (float)height + 2 * Margin));
        using (DependencyTracker.Suspend())
            paint(canvas);
        return recorder.EndRecording();
    }

    /// <summary>Draws an item's content and children the way the scene does, without its position or visibility.</summary>
    public static void PaintSubtree(Item item, SKCanvas canvas, PaintContext context)
    {
        item.Paint(canvas, context);
        var clipped = item.Clip;
        if (clipped)
        {
            canvas.Save();
            if (item is IRoundedClip && item.ClipShape is { } shape)
            {
                canvas.ClipRoundRect(shape, SKClipOperation.Intersect, true);
                shape.Dispose();
            }
            else
            {
                canvas.ClipRect(SKRect.Create(0, 0, (float)item.Width, (float)item.Height));
            }
        }
        foreach (var child in item.PaintOrder)
            Scene.PaintItem(child, canvas, context);
        if (item is IPaintOverChildren over)
            over.PaintOver(canvas, context);
        if (clipped)
            canvas.Restore();
    }

    /// <summary>The matrix that draws a source of one size into an effect of another, as Qt stretches the source texture.</summary>
    public static SKMatrix Fit(Item? source, double width, double height)
    {
        if (source is null || source.Width <= 0 || source.Height <= 0 || width <= 0 || height <= 0)
            return SKMatrix.Identity;
        return SKMatrix.CreateScale((float)(width / source.Width), (float)(height / source.Height));
    }
}

/// <summary>Builds the color matrices of brightness, contrast, saturation and colorization.</summary>
public static class ColorMatrix
{
    public static float[] Identity() => [1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0];

    /// <summary>Multiplies two 4x5 color matrices: the result applies <paramref name="first"/>, then <paramref name="then"/>.</summary>
    public static float[] Then(float[] first, float[] then)
    {
        var result = new float[20];
        for (var row = 0; row < 4; row++)
        {
            for (var col = 0; col < 5; col++)
            {
                float v = col == 4 ? then[row * 5 + 4] : 0;
                for (var k = 0; k < 4; k++)
                    v += then[row * 5 + k] * first[k * 5 + col];
                result[row * 5 + col] = v;
            }
        }
        return result;
    }

    /// <summary>Adds to each channel, -1 to 1.</summary>
    public static float[] Brightness(double b) => [1, 0, 0, 0, (float)b, 0, 1, 0, 0, (float)b, 0, 0, 1, 0, (float)b, 0, 0, 0, 1, 0];

    /// <summary>Scales each channel around the middle gray, -1 to 1.</summary>
    public static float[] Contrast(double c)
    {
        var k = (float)(1 + c);
        if (c > 0)
            k = (float)(1 / Math.Max(0.01, 1 - c));
        var o = 0.5f * (1 - k);
        return [k, 0, 0, 0, o, 0, k, 0, 0, o, 0, 0, k, 0, o, 0, 0, 0, 1, 0];
    }

    /// <summary>Saturation as a factor: 0 is gray, 1 unchanged, more is stronger.</summary>
    public static float[] Saturation(double s)
    {
        var f = (float)s;
        const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
        return
        [
            lr + (1 - lr) * f, lg - lg * f, lb - lb * f, 0, 0,
            lr - lr * f, lg + (1 - lg) * f, lb - lb * f, 0, 0,
            lr - lr * f, lg - lg * f, lb + (1 - lb) * f, 0, 0,
            0, 0, 0, 1, 0,
        ];
    }

    /// <summary>Mixes the color toward the luminance tinted by a color.</summary>
    public static float[] Colorize(QColor color, double amount)
    {
        var a = (float)Math.Clamp(amount, 0, 1);
        const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
        var cr = (float)color.R;
        var cg = (float)color.G;
        var cb = (float)color.B;
        return
        [
            1 - a + a * cr * lr, a * cr * lg, a * cr * lb, 0, 0,
            a * cg * lr, 1 - a + a * cg * lg, a * cg * lb, 0, 0,
            a * cb * lr, a * cb * lg, 1 - a + a * cb * lb, 0, 0,
            0, 0, 0, 1, 0,
        ];
    }

    public static bool IsIdentity(float[] m) => m.AsSpan().SequenceEqual(Identity());
}

/// <summary>An effect item: draws a source item, or the item it is the layer effect of, through filters.</summary>
public abstract class EffectItem : Item, IItemEffect
{
    private Item? _source;
    private bool _cached;

    [Qml]
    public Item? Source
    {
        get { Track(); return _source; }
        set
        {
            if (SetField(ref _source, value))
                Update();
        }
    }

    [Qml] public bool Cached { get { Track(); return _cached; } set => SetField(ref _cached, value); }

    /// <summary>Draws a recorded source, in the effect's coordinates, sized <paramref name="width"/> by <paramref name="height"/>.</summary>
    protected abstract void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context);

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        if (Width <= 0 || Height <= 0)
            return;
        // A source that has this effect as its layer effect would draw itself twice.
        if (_source is { } s && s.Layer.Enabled && s.Layer.Effect == this)
            return;
        using var picture = EffectSource.Record(_source, context);
        if (picture is null && NeedsSource)
            return;
        Render(canvas, picture, EffectSource.Fit(_source, Width, Height), Width, Height, context);
    }

    /// <summary>Whether the effect draws nothing without a source. Gradients and glows draw without one.</summary>
    protected virtual bool NeedsSource => true;

    public void PaintWithEffect(Item item, SKCanvas canvas, PaintContext context, Action paintItem)
    {
        // The item's content is recorded at its origin, then drawn through the effect.
        using var picture = EffectSource.Record(item.Width, item.Height, recording => EffectSource.PaintSubtree(item, recording, context));
        Render(canvas, picture, SKMatrix.Identity, item.Width, item.Height, context);
    }

    protected static void DrawPicture(SKCanvas canvas, SKPicture? picture, SKMatrix matrix, SKPaint? paint)
    {
        if (picture is null)
            return;
        canvas.DrawPicture(picture, in matrix, paint);
    }

    /// <summary>Qt's Gaussian deviation for a blur radius, when none is given.</summary>
    protected static float Deviation(double radius) => (float)Math.Max(0, (radius + 1) / 3.3333);
}

/// <summary>QtQuick.Effects' <c>MultiEffect</c>: shadow, blur, color adjustments and a mask in one effect.</summary>
public sealed class MultiEffect : EffectItem
{
    private bool _shadowEnabled, _blurEnabled, _maskEnabled, _autoPadding = true, _maskInverted;
    private QColor _shadowColor = QColor.Black, _colorizationColor = QColor.Parse("#ff0000");
    private double _shadowBlur = 1, _shadowH, _shadowV, _shadowOpacity = 1, _shadowScale = 1;
    private double _blur, _blurMax = 32, _blurMultiplier;
    private double _brightness, _contrast, _saturation, _colorization;
    private Item? _maskSource;
    private double _maskThresholdMin, _maskSpreadAtMin, _maskThresholdMax = 1, _maskSpreadAtMax;
    private QRect _paddingRect;

    [Qml] public bool ShadowEnabled { get { Track(); return _shadowEnabled; } set { if (SetField(ref _shadowEnabled, value)) Update(); } }

    [Qml] public QColor ShadowColor { get { Track(); return _shadowColor; } set { if (SetField(ref _shadowColor, value)) Update(); } }

    [Qml] public double ShadowBlur { get { Track(); return _shadowBlur; } set { if (SetField(ref _shadowBlur, value)) Update(); } }

    [Qml] public double ShadowHorizontalOffset { get { Track(); return _shadowH; } set { if (SetField(ref _shadowH, value)) Update(); } }

    [Qml] public double ShadowVerticalOffset { get { Track(); return _shadowV; } set { if (SetField(ref _shadowV, value)) Update(); } }

    [Qml] public double ShadowOpacity { get { Track(); return _shadowOpacity; } set { if (SetField(ref _shadowOpacity, value)) Update(); } }

    [Qml] public double ShadowScale { get { Track(); return _shadowScale; } set { if (SetField(ref _shadowScale, value)) Update(); } }

    [Qml] public bool BlurEnabled { get { Track(); return _blurEnabled; } set { if (SetField(ref _blurEnabled, value)) Update(); } }

    [Qml] public double Blur { get { Track(); return _blur; } set { if (SetField(ref _blur, value)) Update(); } }

    [Qml] public double BlurMax { get { Track(); return _blurMax; } set { if (SetField(ref _blurMax, value)) Update(); } }

    [Qml] public double BlurMultiplier { get { Track(); return _blurMultiplier; } set { if (SetField(ref _blurMultiplier, value)) Update(); } }

    [Qml] public double Brightness { get { Track(); return _brightness; } set { if (SetField(ref _brightness, value)) Update(); } }

    [Qml] public double Contrast { get { Track(); return _contrast; } set { if (SetField(ref _contrast, value)) Update(); } }

    [Qml] public double Saturation { get { Track(); return _saturation; } set { if (SetField(ref _saturation, value)) Update(); } }

    [Qml] public double Colorization { get { Track(); return _colorization; } set { if (SetField(ref _colorization, value)) Update(); } }

    [Qml] public QColor ColorizationColor { get { Track(); return _colorizationColor; } set { if (SetField(ref _colorizationColor, value)) Update(); } }

    [Qml] public bool MaskEnabled { get { Track(); return _maskEnabled; } set { if (SetField(ref _maskEnabled, value)) Update(); } }

    [Qml] public Item? MaskSource { get { Track(); return _maskSource; } set { if (SetField(ref _maskSource, value)) Update(); } }

    [Qml] public double MaskThresholdMin { get { Track(); return _maskThresholdMin; } set { if (SetField(ref _maskThresholdMin, value)) Update(); } }

    [Qml] public double MaskSpreadAtMin { get { Track(); return _maskSpreadAtMin; } set { if (SetField(ref _maskSpreadAtMin, value)) Update(); } }

    [Qml] public double MaskThresholdMax { get { Track(); return _maskThresholdMax; } set { if (SetField(ref _maskThresholdMax, value)) Update(); } }

    [Qml] public double MaskSpreadAtMax { get { Track(); return _maskSpreadAtMax; } set { if (SetField(ref _maskSpreadAtMax, value)) Update(); } }

    [Qml] public bool MaskInverted { get { Track(); return _maskInverted; } set { if (SetField(ref _maskInverted, value)) Update(); } }

    [Qml] public bool AutoPaddingEnabled { get { Track(); return _autoPadding; } set => SetField(ref _autoPadding, value); }

    [Qml] public QRect PaddingRect { get { Track(); return _paddingRect; } set => SetField(ref _paddingRect, value); }

    [Qml] public QRect ItemRect => new(0, 0, Width, Height);

    [Qml] public string FragmentShader => "";

    [Qml] public string VertexShader => "";

    [Qml] public bool HasProxySource => false;

    private SKColorFilter? ColorFilter()
    {
        var m = ColorMatrix.Identity();
        if (_brightness != 0)
            m = ColorMatrix.Then(m, ColorMatrix.Brightness(_brightness));
        if (_contrast != 0)
            m = ColorMatrix.Then(m, ColorMatrix.Contrast(_contrast));
        if (_saturation != 0)
            m = ColorMatrix.Then(m, ColorMatrix.Saturation(1 + _saturation));
        if (_colorization > 0)
            m = ColorMatrix.Then(m, ColorMatrix.Colorize(_colorizationColor, _colorization));
        return ColorMatrix.IsIdentity(m) ? null : SKColorFilter.CreateColorMatrix(m);
    }

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        if (source is null)
            return;
        var masked = _maskEnabled && _maskSource is not null;
        if (masked)
            canvas.SaveLayer(null);

        if (_shadowEnabled && _shadowOpacity > 0 && _shadowColor.A > 0)
        {
            var sigma = (float)Math.Max(0, _shadowBlur * _blurMax * 0.5);
            var color = _shadowColor.WithAlpha(_shadowColor.A * Math.Clamp(_shadowOpacity, 0, 1));
            using var tint = SKColorFilter.CreateBlendMode(color.ToSk(), SKBlendMode.SrcIn);
            using var blur = sigma > 0 ? SKImageFilter.CreateBlur(sigma, sigma) : null;
            using var shadowFilter = SKImageFilter.CreateColorFilter(tint, blur);
            using var paint = new SKPaint { ImageFilter = shadowFilter };
            var cx = (float)width / 2;
            var cy = (float)height / 2;
            var m = SKMatrix.CreateTranslation((float)_shadowH, (float)_shadowV)
                .PreConcat(SKMatrix.CreateScale((float)_shadowScale, (float)_shadowScale, cx, cy))
                .PreConcat(sourceMatrix);
            DrawPicture(canvas, source, m, paint);
        }

        using (var colorFilter = ColorFilter())
        {
            var sigma = _blurEnabled ? (float)Math.Max(0, _blur * _blurMax * 0.5) : 0;
            using var blur = sigma > 0 ? SKImageFilter.CreateBlur(sigma, sigma) : null;
            using var paint = new SKPaint { ImageFilter = blur, ColorFilter = colorFilter };
            DrawPicture(canvas, source, sourceMatrix, blur is null && colorFilter is null ? null : paint);
        }

        if (masked)
        {
            using var mask = EffectSource.Record(_maskSource, context);
            using var table = MaskTable();
            using var paint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = table };
            DrawPicture(canvas, mask, EffectSource.Fit(_maskSource, width, height), paint);
            canvas.Restore();
        }
    }

    /// <summary>Maps the mask's alpha through the thresholds and spreads, as Qt's mask does.</summary>
    private SKColorFilter? MaskTable()
    {
        var plain = _maskThresholdMin == 0 && _maskSpreadAtMin == 0 && _maskThresholdMax >= 1 && _maskSpreadAtMax == 0 && !_maskInverted;
        if (plain)
            return null;
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var a = i / 255.0;
            double low;
            if (_maskSpreadAtMin > 0)
            {
                var start = _maskThresholdMin * (1 - _maskSpreadAtMin);
                var end = _maskThresholdMin + (1 - _maskThresholdMin) * _maskSpreadAtMin;
                low = SmoothStep(start, end, a);
            }
            else
            {
                low = a > _maskThresholdMin || (_maskThresholdMin == 0 && a > 0) ? a : 0;
            }
            double high;
            if (_maskSpreadAtMax > 0)
                high = 1 - SmoothStep(_maskThresholdMax, _maskThresholdMax + (1 - _maskThresholdMax) * _maskSpreadAtMax + 1e-6, a);
            else
                high = a <= _maskThresholdMax ? 1 : 0;
            var v = low * high;
            if (_maskInverted)
                v = 1 - v;
            table[i] = (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        }
        return SKColorFilter.CreateTable(table, null, null, null);
    }

    private static double SmoothStep(double e0, double e1, double x)
    {
        if (e1 <= e0)
            return x >= e0 ? 1 : 0;
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>QtQuick.Effects' <c>RectangularShadow</c>: a blurred rounded rectangle, without a source.</summary>
public sealed class RectangularShadow : EffectItem
{
    private double _blur = 10, _radius, _spread;
    private QColor _color = QColor.Black;
    private QPoint _offset;

    [Qml] public double Blur { get { Track(); return _blur; } set { if (SetField(ref _blur, value)) Update(); } }

    [Qml] public double Radius { get { Track(); return _radius; } set { if (SetField(ref _radius, value)) Update(); } }

    [Qml] public double Spread { get { Track(); return _spread; } set { if (SetField(ref _spread, value)) Update(); } }

    [Qml] public QColor Color { get { Track(); return _color; } set { if (SetField(ref _color, value)) Update(); } }

    [Qml] public QPoint Offset { get { Track(); return _offset; } set { if (SetField(ref _offset, value)) Update(); } }

    [Qml] public object? Material { get; set; }

    protected override bool NeedsSource => false;

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        var rect = SKRect.Create((float)_offset.X, (float)_offset.Y, (float)width, (float)height);
        rect.Inflate((float)_spread, (float)_spread);
        var radius = (float)Math.Min(_radius + _spread, Math.Min(rect.Width, rect.Height) / 2);
        using var paint = new SKPaint { Color = _color.ToSk(), IsAntialias = true };
        if (_blur > 0)
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)_blur / 2);
        canvas.DrawRoundRect(rect, radius, radius, paint);
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>DropShadow</c>: the source with a blurred, offset shadow under it.</summary>
public sealed class DropShadow : EffectItem
{
    private double _h, _v, _radius = 4, _spread;
    private double? _samples;
    private QColor _color = QColor.Black;
    private bool _transparentBorder = true;

    [Qml] public double HorizontalOffset { get { Track(); return _h; } set { if (SetField(ref _h, value)) Update(); } }

    [Qml] public double VerticalOffset { get { Track(); return _v; } set { if (SetField(ref _v, value)) Update(); } }

    [Qml] public double Radius { get { Track(); return _radius; } set { if (SetField(ref _radius, value)) Update(); } }

    [Qml] public double Samples { get { Track(); return _samples ?? 9; } set { _samples = value; NotifyChanged("samples"); } }

    [Qml] public double Spread { get { Track(); return _spread; } set { if (SetField(ref _spread, value)) Update(); } }

    [Qml] public QColor Color { get { Track(); return _color; } set { if (SetField(ref _color, value)) Update(); } }

    [Qml] public bool TransparentBorder { get { Track(); return _transparentBorder; } set => SetField(ref _transparentBorder, value); }

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        using (var shadow = Shadow.Paint(_color, Deviation(_radius), _spread))
            DrawPicture(canvas, source, SKMatrix.CreateTranslation((float)_h, (float)_v).PreConcat(sourceMatrix), shadow);
        DrawPicture(canvas, source, sourceMatrix, null);
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>Glow</c>: a blurred halo of a color around the source.</summary>
public sealed class Glow : EffectItem
{
    private double _radius = 4, _spread = 0.5;
    private QColor _color = QColor.White;

    [Qml] public double Radius { get { Track(); return _radius; } set { if (SetField(ref _radius, value)) Update(); } }

    [Qml] public double Samples { get; set; } = 9;

    [Qml] public double Spread { get { Track(); return _spread; } set { if (SetField(ref _spread, value)) Update(); } }

    [Qml] public QColor Color { get { Track(); return _color; } set { if (SetField(ref _color, value)) Update(); } }

    [Qml] public bool TransparentBorder { get; set; } = true;

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        using (var glow = Shadow.Paint(_color, Deviation(_radius), _spread))
            DrawPicture(canvas, source, sourceMatrix, glow);
        DrawPicture(canvas, source, sourceMatrix, null);
    }
}

/// <summary>A shadow or glow paint: the source's alpha, blurred, strengthened by a spread, in one color.</summary>
internal static class Shadow
{
    public static SKPaint Paint(QColor color, float sigma, double spread)
    {
        using var tint = SKColorFilter.CreateBlendMode(color.ToSk(), SKBlendMode.SrcIn);
        using var blur = sigma > 0 ? SKImageFilter.CreateBlur(sigma, sigma) : null;
        SKImageFilter filter;
        if (spread > 0)
        {
            var table = new byte[256];
            var k = 1 / Math.Max(0.02, 1 - Math.Clamp(spread, 0, 1));
            for (var i = 0; i < 256; i++)
                table[i] = (byte)Math.Clamp(Math.Round(i * k), 0, 255);
            using var boost = SKColorFilter.CreateTable(table, null, null, null);
            using var boosted = SKImageFilter.CreateColorFilter(boost, blur);
            filter = SKImageFilter.CreateColorFilter(tint, boosted);
        }
        else
        {
            filter = SKImageFilter.CreateColorFilter(tint, blur);
        }
        return new SKPaint { ImageFilter = filter };
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>RectangularGlow</c>: a glow around a rectangle, without a source.</summary>
public sealed class RectangularGlow : EffectItem
{
    private double _glowRadius, _spread;
    private double? _cornerRadius;
    private QColor _color = QColor.White;

    [Qml] public double GlowRadius { get { Track(); return _glowRadius; } set { if (SetField(ref _glowRadius, value)) Update(); } }

    [Qml] public double Spread { get { Track(); return _spread; } set { if (SetField(ref _spread, value)) Update(); } }

    [Qml] public QColor Color { get { Track(); return _color; } set { if (SetField(ref _color, value)) Update(); } }

    [Qml] public double CornerRadius { get { Track(); return _cornerRadius ?? _glowRadius; } set { _cornerRadius = value; NotifyChanged("cornerRadius"); Update(); } }

    protected override bool NeedsSource => false;

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        var g = Math.Max(0, _glowRadius);
        var s = Math.Clamp(_spread, 0, 1);
        var rect = SKRect.Create(0, 0, (float)width, (float)height);
        var inflate = (float)(g / 2 + s * g / 2);
        rect.Inflate(inflate, inflate);
        var radius = (float)Math.Min(CornerRadius + inflate, Math.Min(rect.Width, rect.Height) / 2);
        using var paint = new SKPaint { Color = _color.ToSk(), IsAntialias = true };
        var sigma = (float)((1 - s) * g / 4);
        if (sigma > 0)
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
        canvas.DrawRoundRect(rect, radius, radius, paint);
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>ColorOverlay</c>: the source's shape filled with a color.</summary>
public sealed class ColorOverlay : EffectItem
{
    private QColor _color = QColor.Transparent;

    [Qml] public QColor Color { get { Track(); return _color; } set { if (SetField(ref _color, value)) Update(); } }

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        if (_color.A <= 0)
        {
            DrawPicture(canvas, source, sourceMatrix, null);
            return;
        }
        using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(_color.ToSk(), SKBlendMode.SrcIn) };
        DrawPicture(canvas, source, sourceMatrix, paint);
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>OpacityMask</c>: the source, shown only where the mask is opaque.</summary>
public sealed class OpacityMask : EffectItem
{
    private Item? _maskSource;
    private bool _invert;

    [Qml] public Item? MaskSource { get { Track(); return _maskSource; } set { if (SetField(ref _maskSource, value)) Update(); } }

    [Qml] public bool Invert { get { Track(); return _invert; } set { if (SetField(ref _invert, value)) Update(); } }

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        canvas.SaveLayer(null);
        DrawPicture(canvas, source, sourceMatrix, null);
        using var mask = EffectSource.Record(_maskSource, context);
        using var paint = new SKPaint { BlendMode = _invert ? SKBlendMode.DstOut : SKBlendMode.DstIn };
        DrawPicture(canvas, mask, EffectSource.Fit(_maskSource, width, height), paint);
        canvas.Restore();
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>GaussianBlur</c>.</summary>
public sealed class GaussianBlur : EffectItem
{
    private double _radius = 4;
    private double? _deviation;
    private bool _transparentBorder;

    [Qml] public double Radius { get { Track(); return _radius; } set { if (SetField(ref _radius, value)) Update(); } }

    [Qml] public double Samples { get; set; } = 9;

    [Qml("deviation")] public double Deviation_ { get { Track(); return _deviation ?? Deviation(_radius); } set { _deviation = value; NotifyChanged("deviation"); Update(); } }

    [Qml] public bool TransparentBorder { get { Track(); return _transparentBorder; } set { if (SetField(ref _transparentBorder, value)) Update(); } }

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context) =>
        Blurs.Draw(canvas, source, sourceMatrix, (float)(_deviation ?? Deviation(_radius)), _transparentBorder, width, height);
}

/// <summary>Qt5Compat.GraphicalEffects' <c>FastBlur</c>.</summary>
public sealed class FastBlur : EffectItem
{
    private double _radius;
    private bool _transparentBorder;

    [Qml] public double Radius { get { Track(); return _radius; } set { if (SetField(ref _radius, value)) Update(); } }

    [Qml] public bool TransparentBorder { get { Track(); return _transparentBorder; } set { if (SetField(ref _transparentBorder, value)) Update(); } }

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context) =>
        Blurs.Draw(canvas, source, sourceMatrix, (float)(_radius * 0.4), _transparentBorder, width, height);
}

internal static class Blurs
{
    /// <summary>Blurs a source. Without a transparent border the edges are kept, as Qt's blurs do by default.</summary>
    public static void Draw(SKCanvas canvas, SKPicture? source, SKMatrix matrix, float sigma, bool transparentBorder, double width, double height)
    {
        if (source is null)
            return;
        if (sigma <= 0)
        {
            canvas.DrawPicture(source, in matrix, null);
            return;
        }
        var bounds = SKRect.Create(0, 0, (float)width, (float)height);
        using var paint = new SKPaint();
        if (transparentBorder)
        {
            paint.ImageFilter = SKImageFilter.CreateBlur(sigma, sigma);
            canvas.DrawPicture(source, in matrix, paint);
        }
        else
        {
            using var picture = SKImageFilter.CreatePicture(source, bounds);
            using var placed = SKImageFilter.CreateMatrix(in matrix, new SKSamplingOptions(SKFilterMode.Linear), picture);
            using var crop = SKImageFilter.CreateCrop(bounds, SKShaderTileMode.Clamp, placed);
            paint.ImageFilter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp, crop, bounds);
            canvas.Save();
            canvas.ClipRect(bounds);
            canvas.DrawRect(bounds, paint);
            canvas.Restore();
        }
        paint.ImageFilter?.Dispose();
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>Desaturate</c>.</summary>
public sealed class Desaturate : EffectItem
{
    private double _desaturation;

    [Qml] public double Desaturation { get { Track(); return _desaturation; } set { if (SetField(ref _desaturation, value)) Update(); } }

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(ColorMatrix.Saturation(1 - Math.Clamp(_desaturation, 0, 1))) };
        DrawPicture(canvas, source, sourceMatrix, paint);
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>BrightnessContrast</c>.</summary>
public sealed class BrightnessContrast : EffectItem
{
    private double _brightness, _contrast;

    [Qml] public double Brightness { get { Track(); return _brightness; } set { if (SetField(ref _brightness, value)) Update(); } }

    [Qml] public double Contrast { get { Track(); return _contrast; } set { if (SetField(ref _contrast, value)) Update(); } }

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        var m = ColorMatrix.Then(ColorMatrix.Brightness(_brightness), ColorMatrix.Contrast(_contrast));
        using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(m) };
        DrawPicture(canvas, source, sourceMatrix, paint);
    }
}

/// <summary>A gradient effect: fills the item, masked by the source's alpha when it has one.</summary>
public abstract class GradientEffect : EffectItem
{
    private Gradient? _gradient;

    [Qml]
    public Gradient? Gradient
    {
        get { Track(); return _gradient; }
        set
        {
            if (SetField(ref _gradient, value))
            {
                value?.Changed.Add(Update);
                Update();
            }
        }
    }

    protected override bool NeedsSource => false;

    protected (SKColor[] Colors, float[] Positions) Stops()
    {
        var stops = _gradient?.Stops.Snapshot.OrderBy(s => s.Position).ToArray() ?? [];
        if (stops.Length == 0)
            return ([SKColors.White, SKColors.Black], [0, 1]);
        return (stops.Select(s => s.Color.ToSk()).ToArray(), stops.Select(s => (float)Math.Clamp(s.Position, 0, 1)).ToArray());
    }

    protected abstract SKShader CreateShader(double width, double height);

    protected override void Render(SKCanvas canvas, SKPicture? source, SKMatrix sourceMatrix, double width, double height, PaintContext context)
    {
        var bounds = SKRect.Create(0, 0, (float)width, (float)height);
        using var shader = CreateShader(width, height);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        if (source is null)
        {
            canvas.DrawRect(bounds, paint);
            return;
        }
        canvas.SaveLayer(null);
        canvas.DrawRect(bounds, paint);
        using var mask = new SKPaint { BlendMode = SKBlendMode.DstIn };
        DrawPicture(canvas, source, sourceMatrix, mask);
        canvas.Restore();
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>LinearGradient</c>.</summary>
public sealed class LinearGradient : GradientEffect
{
    private QPoint _start;
    private QPoint? _end;

    [Qml] public QPoint Start { get { Track(); return _start; } set { if (SetField(ref _start, value)) Update(); } }

    [Qml] public QPoint End { get { Track(); return _end ?? new QPoint(0, Height); } set { _end = value; NotifyChanged("end"); Update(); } }

    protected override SKShader CreateShader(double width, double height)
    {
        var (colors, positions) = Stops();
        var end = _end ?? new QPoint(0, height);
        return SKShader.CreateLinearGradient(new SKPoint((float)_start.X, (float)_start.Y), new SKPoint((float)end.X, (float)end.Y), colors, positions, SKShaderTileMode.Clamp);
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>RadialGradient</c>.</summary>
public sealed class RadialGradient : GradientEffect
{
    private double _h, _v, _angle;
    private double? _hr, _vr;

    [Qml] public double HorizontalOffset { get { Track(); return _h; } set { if (SetField(ref _h, value)) Update(); } }

    [Qml] public double VerticalOffset { get { Track(); return _v; } set { if (SetField(ref _v, value)) Update(); } }

    [Qml] public double HorizontalRadius { get { Track(); return _hr ?? Width / 2; } set { _hr = value; NotifyChanged("horizontalRadius"); Update(); } }

    [Qml] public double VerticalRadius { get { Track(); return _vr ?? Height / 2; } set { _vr = value; NotifyChanged("verticalRadius"); Update(); } }

    [Qml] public double Angle { get { Track(); return _angle; } set { if (SetField(ref _angle, value)) Update(); } }

    protected override SKShader CreateShader(double width, double height)
    {
        var (colors, positions) = Stops();
        var hr = Math.Max(0.01, _hr ?? width / 2);
        var vr = Math.Max(0.01, _vr ?? height / 2);
        var cx = (float)(width / 2 + _h);
        var cy = (float)(height / 2 + _v);
        // A circle of radius hr, squashed to vr and turned by the angle.
        var matrix = SKMatrix.CreateRotationDegrees((float)_angle, cx, cy).PreConcat(SKMatrix.CreateScale(1, (float)(vr / hr), cx, cy));
        return SKShader.CreateRadialGradient(new SKPoint(cx, cy), (float)hr, colors, positions, SKShaderTileMode.Clamp, matrix);
    }
}

/// <summary>Qt5Compat.GraphicalEffects' <c>ConicalGradient</c>.</summary>
public sealed class ConicalGradient : GradientEffect
{
    private double _h, _v, _angle;

    [Qml] public double HorizontalOffset { get { Track(); return _h; } set { if (SetField(ref _h, value)) Update(); } }

    [Qml] public double VerticalOffset { get { Track(); return _v; } set { if (SetField(ref _v, value)) Update(); } }

    [Qml] public double Angle { get { Track(); return _angle; } set { if (SetField(ref _angle, value)) Update(); } }

    protected override SKShader CreateShader(double width, double height)
    {
        var (colors, positions) = Stops();
        var cx = (float)(width / 2 + _h);
        var cy = (float)(height / 2 + _v);
        // Qt's conical gradient starts at the top and turns clockwise; Skia's sweep starts at the right.
        var matrix = SKMatrix.CreateRotationDegrees((float)_angle - 90, cx, cy);
        return SKShader.CreateSweepGradient(new SKPoint(cx, cy), colors, positions, matrix);
    }
}
