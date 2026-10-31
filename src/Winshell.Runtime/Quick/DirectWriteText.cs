#pragma warning disable CA1416

using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using SkiaSharp;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;

namespace Winshell.Runtime.Quick;

/// <summary>The process-wide DirectWrite and Direct2D factories text layout draws with.</summary>
internal static class DirectWriteServices
{
    internal static readonly object Gate = new();
    private static readonly Lazy<DirectWriteEngine> Engine = new(DirectWriteEngine.Create);

    internal static DirectWriteEngine Current => Engine.Value;

    internal static string ResolveFamily(string family)
    {
        var name = family.Split(',')[0].Trim().Trim('"', '\'');
        if (name.Equals("monospace", StringComparison.OrdinalIgnoreCase))
            return "Cascadia Mono";
        if (name.Equals("sans-serif", StringComparison.OrdinalIgnoreCase) || name.Equals("sans", StringComparison.OrdinalIgnoreCase))
            return "Segoe UI Variable";
        if (name.Equals("serif", StringComparison.OrdinalIgnoreCase))
            return "Cambria";
        return name.Length == 0 ? "Segoe UI Variable" : name;
    }
}

/// <summary>One copy of a layout to draw: its origin in DIPs, its colour, and whether span colours apply.</summary>
internal readonly record struct TextPass(float X, float Y, QColor Color, bool UseSpans);

internal sealed class DirectWriteEngine
{
    private readonly IDWriteFactory7 _factory;
    private readonly ID2D1Factory1 _d2d;
    private readonly IWICImagingFactory _wic;
    private readonly Dictionary<string, IDWriteFontFallback> _fallbacks = [];
    private readonly IDWriteFontCollection? _typographic;
    private readonly IDWriteRenderingParams _rendering;

    private DirectWriteEngine(IDWriteFactory7 factory, ID2D1Factory1 d2d, IWICImagingFactory wic, IDWriteFontCollection? typographic, IDWriteRenderingParams rendering)
    {
        _factory = factory;
        _d2d = d2d;
        _wic = wic;
        _typographic = typographic;
        _rendering = rendering;
    }

    internal static DirectWriteEngine Create()
    {
        var factory = DWrite.DWriteCreateFactory<IDWriteFactory7>(Vortice.DirectWrite.FactoryType.Shared);
        var d2d = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.MultiThreaded, DebugLevel.None);
        var wic = new IWICImagingFactory();
        IDWriteFontCollection? typographic = null;
        try
        {
            typographic = factory.GetSystemFontCollection(false, FontFamilyModel.Typographic);
        }
        catch (SharpGenException)
        {
            typographic = null;
        }
        return new DirectWriteEngine(factory, d2d, wic, typographic, factory.CreateRenderingParams());
    }

    internal (IDWriteTextLayout Layout, IDWriteInlineObject? Ellipsis) CreateLayout(string text, FontSpec font, WordWrapping wrapping, float maxWidth, float maxHeight, bool ellipsis, double lineHeight, bool proportional)
    {
        var format = CreateFormat(font);
        IDWriteTextLayout? layout = null;
        IDWriteInlineObject? sign = null;
        try
        {
            format.WordWrapping = wrapping;
            ApplySpacing(format, lineHeight, proportional);
            layout = _factory.CreateTextLayout(text, format, maxWidth, maxHeight);
            if (ellipsis)
            {
                sign = _factory.CreateEllipsisTrimmingSign(format);
                layout.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, sign);
            }
            var created = (layout, sign);
            layout = null;
            sign = null;
            return created;
        }
        finally
        {
            sign?.Dispose();
            layout?.Dispose();
            format.Dispose();
        }
    }

    /// <summary>
    /// Draws the passes of a layout into the Skia canvas, in order. Axis-aligned text is read back in device pixels
    /// so ClearType blends with what is already painted and the scale matches the canvas. Rotated text is grayscale.
    /// </summary>
    internal void Blit(IDWriteTextLayout layout, IReadOnlyList<(int Start, int Length, QColor Color)> spans, SKCanvas canvas, SKRect dipBox, ReadOnlySpan<TextPass> passes, TextPixelMode mode, bool readRoot = true)
    {
        var matrix = canvas.TotalMatrix;
        var axis = Math.Abs(matrix.SkewX) < 1e-3f && Math.Abs(matrix.SkewY) < 1e-3f && matrix.ScaleX > 0.01f && matrix.ScaleY > 0.01f;
        var grayscale = mode == TextPixelMode.Aliased ? TextAntialiasMode.Aliased : TextAntialiasMode.Grayscale;
        // A SaveLayer is not what ReadPixels returns. Reading the root and writing it back with Src
        // replaces the layer, so text inside opacity punches a hole. Draw grayscale over the layer instead.
        if (!axis || !readRoot || canvas.Surface is not { } surface)
        {
            BlitTransformed(layout, spans, canvas, dipBox, passes, grayscale);
            return;
        }
        var aa = mode switch
        {
            TextPixelMode.Aliased => TextAntialiasMode.Aliased,
            TextPixelMode.ClearType => TextAntialiasMode.Cleartype,
            _ => TextAntialiasMode.Grayscale,
        };
        var mapped = matrix.MapRect(dipBox);
        var clip = canvas.DeviceClipBounds;
        mapped = SKRect.Intersect(mapped, clip);
        if (mapped.IsEmpty)
            return;
        var left = (int)Math.Floor(mapped.Left);
        var top = (int)Math.Floor(mapped.Top);
        var right = (int)Math.Ceiling(mapped.Right);
        var bottom = (int)Math.Ceiling(mapped.Bottom);
        var pixelW = right - left;
        var pixelH = bottom - top;
        if (pixelW <= 0 || pixelH <= 0 || (long)pixelW * pixelH > 4096L * 4096L)
            return;
        lock (DirectWriteServices.Gate)
        {
            var scratch = ScratchFor(pixelW, pixelH);
            try
            {
                bool read;
                using (var locked = scratch.Bitmap.Lock(BitmapLockFlags.Write))
                using (var pixels = View(locked, pixelW, pixelH))
                    read = surface.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, left, top);
                if (read)
                {
                    var dipLeft = (left - matrix.TransX) / matrix.ScaleX;
                    var dipTop = (top - matrix.TransY) / matrix.ScaleY;
                    DrawPasses(scratch, layout, spans, passes, matrix.ScaleX, matrix.ScaleY, -dipLeft, -dipTop, aa);
                    canvas.Save();
                    canvas.ResetMatrix();
                    using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
                    using (var locked = scratch.Bitmap.Lock(BitmapLockFlags.Read))
                    using (var pixels = View(locked, pixelW, pixelH))
                        canvas.DrawBitmap(pixels, left, top, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
                    canvas.Restore();
                    return;
                }
            }
            finally
            {
                Release(scratch);
            }
        }
        BlitTransformed(layout, spans, canvas, dipBox, passes, grayscale);
    }

    /// <summary>Direct2D render targets made for text. Text drawing keeps one and reuses it, so a repaint makes none.</summary>
    internal int TargetsCreated { get; private set; }

    private void BlitTransformed(IDWriteTextLayout layout, IReadOnlyList<(int Start, int Length, QColor Color)> spans, SKCanvas canvas, SKRect dipBox, ReadOnlySpan<TextPass> passes, TextAntialiasMode aa)
    {
        var scale = Math.Max(1f, Math.Max(Math.Abs(canvas.TotalMatrix.ScaleX), Math.Abs(canvas.TotalMatrix.ScaleY)));
        var pixelW = Math.Max(1, (int)Math.Ceiling(dipBox.Width * scale));
        var pixelH = Math.Max(1, (int)Math.Ceiling(dipBox.Height * scale));
        if ((long)pixelW * pixelH > 4096L * 4096L)
            return;
        var dpiScaleX = pixelW / Math.Max(1f, dipBox.Width);
        var dpiScaleY = pixelH / Math.Max(1f, dipBox.Height);
        lock (DirectWriteServices.Gate)
        {
            var scratch = ScratchFor(pixelW, pixelH);
            try
            {
                using (var locked = scratch.Bitmap.Lock(BitmapLockFlags.Write))
                using (var pixels = View(locked, pixelW, pixelH))
                    pixels.Erase(SKColors.Transparent);
                DrawPasses(scratch, layout, spans, passes, dpiScaleX, dpiScaleY, -dipBox.Left, -dipBox.Top, aa);
                using (var locked = scratch.Bitmap.Lock(BitmapLockFlags.Read))
                using (var pixels = View(locked, pixelW, pixelH))
                    canvas.DrawBitmap(pixels, dipBox, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint: null);
            }
            finally
            {
                Release(scratch);
            }
        }
    }

    /// <summary>Draws every pass in one Direct2D draw. The pass that uses the span colours comes last.</summary>
    private void DrawPasses(Scratch scratch, IDWriteTextLayout layout, IReadOnlyList<(int Start, int Length, QColor Color)> spans, ReadOnlySpan<TextPass> passes, float scaleX, float scaleY, float offsetX, float offsetY, TextAntialiasMode aa)
    {
        var target = scratch.Target;
        var brushes = new List<ID2D1Brush>();
        var colored = false;
        try
        {
            target.SetDpi(96f * scaleX, 96f * scaleY);
            target.TextAntialiasMode = aa;
            target.BeginDraw();
            foreach (var pass in passes)
            {
                var fill = target.CreateSolidColorBrush(ToColor(pass.Color), null);
                brushes.Add(fill);
                if (pass.UseSpans)
                {
                    foreach (var (start, length, spanColor) in spans)
                    {
                        if (length <= 0)
                            continue;
                        var brush = target.CreateSolidColorBrush(ToColor(spanColor), null);
                        brushes.Add(brush);
                        layout.SetDrawingEffect(brush, new TextRange((uint)start, (uint)length));
                        colored = true;
                    }
                }
                target.DrawTextLayout(new Vector2(offsetX + pass.X, offsetY + pass.Y), layout, fill, DrawTextOptions.EnableColorFont);
            }
            target.EndDraw().CheckError();
        }
        catch
        {
            // A target that failed mid-draw is not drawn with again; Release disposes it.
            if (_scratch == scratch)
                _scratch = null;
            throw;
        }
        finally
        {
            if (colored)
            {
                foreach (var (start, length, _) in spans)
                {
                    if (length > 0)
                        layout.SetDrawingEffect(null!, new TextRange((uint)start, (uint)length));
                }
            }
            foreach (var brush in brushes)
                brush.Dispose();
        }
    }

    /// <summary>The largest scratch bitmap kept between draws. A larger draw gets its own, released after.</summary>
    private const long KeptPixels = 1024L * 1024L;

    private Scratch? _scratch;

    /// <summary>A scratch bitmap at least this size. The kept one grows to cover each draw, up to <see cref="KeptPixels"/>.</summary>
    private Scratch ScratchFor(int width, int height)
    {
        if (_scratch is { } kept && kept.Width >= width && kept.Height >= height)
            return kept;
        var w = Math.Max(width, _scratch?.Width ?? 0);
        var h = Math.Max(height, _scratch?.Height ?? 0);
        if ((long)w * h > KeptPixels)
            (w, h) = (width, height);
        var wic = _wic.CreateBitmap((uint)w, (uint)h, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        var props = new RenderTargetProperties(RenderTargetType.Default, new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96f, 96f, RenderTargetUsage.None, FeatureLevel.Default);
        ID2D1RenderTarget target;
        try
        {
            target = _d2d.CreateWicBitmapRenderTarget(wic, props);
        }
        catch
        {
            wic.Dispose();
            throw;
        }
        TargetsCreated++;
        target.TextRenderingParams = _rendering;
        var created = new Scratch(wic, target, w, h);
        if ((long)w * h <= KeptPixels)
        {
            _scratch?.Dispose();
            _scratch = created;
        }
        return created;
    }

    private void Release(Scratch scratch)
    {
        if (scratch != _scratch)
            scratch.Dispose();
    }

    /// <summary>The top-left corner of a locked WIC bitmap as a Skia bitmap. Skia copies it when it is drawn.</summary>
    private static SKBitmap View(IWICBitmapLock locked, int width, int height)
    {
        var bitmap = new SKBitmap();
        bitmap.InstallPixels(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), locked.Data.DataPointer, (int)locked.Data.Pitch);
        return bitmap;
    }

    private static Color4 ToColor(QColor color) => new((float)color.R, (float)color.G, (float)color.B, (float)color.A);

    /// <summary>A WIC bitmap and the Direct2D target that draws into it.</summary>
    private sealed class Scratch(IWICBitmap bitmap, ID2D1RenderTarget target, int width, int height) : IDisposable
    {
        public IWICBitmap Bitmap { get; } = bitmap;

        public ID2D1RenderTarget Target { get; } = target;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public void Dispose()
        {
            Target.Dispose();
            Bitmap.Dispose();
        }
    }

    private IDWriteTextFormat CreateFormat(FontSpec font)
    {
        var size = (float)font.PixelSize;
        if (float.IsNaN(size) || size < 1)
            size = (float)FontSpec.DefaultPixelSize;
        var weight = (FontWeight)Math.Clamp(font.Weight, 1, 999);
        var style = font.Italic ? FontStyle.Italic : FontStyle.Normal;
        var resolved = DirectWriteServices.ResolveFamily(font.Family);
        var locale = CultureInfo.CurrentUICulture.Name;
        IDWriteTextFormat? format = null;
        foreach (var name in new[] { resolved, "Segoe UI Variable", "Segoe UI" })
        {
            format = TryFormat(name, _typographic, weight, style, size, locale) ?? TryFormat(name, null, weight, style, size, locale);
            if (format is not null)
                break;
        }
        format ??= _factory.CreateTextFormat("Segoe UI", null, weight, style, FontStretch.Normal, size, locale);
        using var format1 = format.QueryInterfaceOrNull<IDWriteTextFormat1>();
        if (format1 is not null)
            format1.FontFallback = FallbackFor(locale);
        ApplyOpticalSize(format, size);
        return format;
    }

    /// <summary>
    /// Segoe UI Variable picks its optical master from the opsz axis. Automatic axes cover a resolved face;
    /// writing opsz as well keeps an 8px run and a 36px run on different masters.
    /// </summary>
    private static void ApplyOpticalSize(IDWriteTextFormat format, float size)
    {
        using var format3 = format.QueryInterfaceOrNull<IDWriteTextFormat3>();
        if (format3 is null)
            return;
        format3.AutomaticFontAxes = AutomaticFontAxes.OpticalSize;
        var count = format3.FontAxisValueCount;
        if (count == 0)
            return;
        var axes = new FontAxisValue[count];
        format3.GetFontAxisValues(axes, count);
        var opsz = size * 72f / 96f;
        var found = false;
        for (var i = 0; i < axes.Length; i++)
        {
            if (axes[i].AxisTag != FontAxisTag.OpticalSize)
                continue;
            var axis = axes[i];
            axis.Value = opsz;
            axes[i] = axis;
            found = true;
        }
        if (found)
            format3.SetFontAxisValues(axes, count);
    }

    private IDWriteTextFormat? TryFormat(string family, IDWriteFontCollection? collection, FontWeight weight, FontStyle style, float size, string locale)
    {
        try
        {
            return _factory.CreateTextFormat(family, collection!, weight, style, FontStretch.Normal, size, locale);
        }
        catch (SharpGenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static void ApplySpacing(IDWriteTextFormat format, double lineHeight, bool proportional)
    {
        if (proportional)
        {
            if (lineHeight > 0 && Math.Abs(lineHeight - 1) > 0.001)
                format.SetLineSpacing(LineSpacingMethod.Proportional, (float)lineHeight, (float)lineHeight);
            return;
        }
        if (lineHeight > 0)
            format.SetLineSpacing(LineSpacingMethod.Uniform, (float)lineHeight, (float)(lineHeight * 0.8));
    }

    private IDWriteFontFallback FallbackFor(string locale)
    {
        var han = HanFamilies(locale);
        lock (_fallbacks)
        {
            if (!_fallbacks.TryGetValue(han[0], out var fallback))
                _fallbacks[han[0]] = fallback = BuildFallback(_factory, han);
            return fallback;
        }
    }

    /// <summary>
    /// The same Han code point has Chinese, Taiwanese, Korean and Japanese glyph forms.
    /// The first font tried follows the UI language. Japanese and every other language keep Yu Gothic UI.
    /// </summary>
    internal static string[] HanFamilies(string locale)
    {
        if (Is(locale, "zh-Hant") || Is(locale, "zh-TW") || Is(locale, "zh-HK") || Is(locale, "zh-MO"))
            return ["Microsoft JhengHei UI", "Microsoft YaHei UI", "Yu Gothic UI", "Meiryo"];
        if (Is(locale, "zh"))
            return ["Microsoft YaHei UI", "Microsoft JhengHei UI", "Yu Gothic UI", "Meiryo"];
        if (Is(locale, "ko"))
            return ["Malgun Gothic", "Yu Gothic UI", "Microsoft YaHei UI", "Meiryo"];
        return ["Yu Gothic UI", "Yu Gothic", "Microsoft YaHei UI", "Meiryo"];

        static bool Is(string locale, string tag) =>
            locale.Equals(tag, StringComparison.OrdinalIgnoreCase) || locale.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static IDWriteFontFallback BuildFallback(IDWriteFactory7 factory, string[] han)
    {
        try
        {
            using var builder = factory.CreateFontFallbackBuilder();
            Map(builder, [Range(0xE700, 0xF8FF)], "Segoe Fluent Icons", "Segoe MDL2 Assets");
            Map(builder, [Range(0x1F300, 0x1FAFF), Range(0x2600, 0x27BF), Range(0xFE0E, 0xFE0F), Range(0x200D, 0x200D), Range(0x20E3, 0x20E3)], "Segoe UI Emoji");
            Map(builder, [Range(0x3040, 0x30FF), Range(0x31F0, 0x31FF), Range(0x3400, 0x9FFF)], han);
            Map(builder, [Range(0xAC00, 0xD7AF)], "Malgun Gothic", "Microsoft YaHei UI");
            builder.AddMappings(factory.SystemFontFallback);
            return builder.CreateFontFallback();
        }
        catch (SharpGenException)
        {
            return factory.SystemFontFallback;
        }
    }

    private static UnicodeRange Range(uint first, uint last) => new() { First = first, Last = last };

    private static unsafe void Map(IDWriteFontFallbackBuilder builder, UnicodeRange[] ranges, params string[] families)
    {
        var pinned = new GCHandle[families.Length];
        var ptrs = new nint[families.Length];
        try
        {
            for (var i = 0; i < families.Length; i++)
            {
                pinned[i] = GCHandle.Alloc(families[i], GCHandleType.Pinned);
                ptrs[i] = pinned[i].AddrOfPinnedObject();
            }
            fixed (nint* p = ptrs)
                builder.AddMapping(ranges, (uint)ranges.Length, (nint)p, (uint)families.Length, null!, null!, null!, 1);
        }
        finally
        {
            foreach (var handle in pinned)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }
    }
}

/// <summary>One DirectWrite layout: metrics, caret positions, and the draw into a Skia canvas.</summary>
internal sealed class DirectWriteLayout : IDisposable
{
    private readonly record struct Range(int Start, int Length, TextSpan Span);

    private IDWriteTextLayout? _layout;
    private IDWriteInlineObject? _ellipsis;
    private int _reportedLines;
    private readonly List<(int Start, int Length, QColor Color)> _colors = [];
    private readonly float _builtWidth;
    private readonly bool _wraps;
    private TextAlignment _alignment = TextAlignment.Leading;
    private float _maxWidth;

    private DirectWriteLayout(IDWriteTextLayout layout, IDWriteInlineObject? ellipsis, string source)
    {
        _layout = layout;
        _ellipsis = ellipsis;
        Source = source;
        _builtWidth = _maxWidth = layout.MaxWidth;
        _wraps = layout.WordWrapping != WordWrapping.NoWrap;
    }

    public string Source { get; }

    public double Width { get; private set; }

    public double Height { get; private set; }

    public bool Truncated { get; private set; }

    public List<TextLayout.Line> Lines { get; } = [];

    public IReadOnlyList<FontRun> FontRuns { get; private set; } = [];

    public static DirectWriteLayout Create(IReadOnlyList<TextSpan> spans, double maxWidth, TextLayout.Wrap wrap, TextLayout.Elide elide, int maxLines, double lineHeight, bool proportional)
    {
        var (text, ranges) = Assemble(spans);
        var truncated = false;
        var finite = maxWidth > 0 && !double.IsNaN(maxWidth) && !double.IsPositiveInfinity(maxWidth);
        if (elide is TextLayout.Elide.Left or TextLayout.Elide.Middle && finite && wrap == TextLayout.Wrap.NoWrap && text.Length > 0 && ranges.Count > 0)
        {
            var font = ranges[0].Span.Font;
            if (Measure(text, font) > maxWidth)
            {
                text = CutToWidth(text, font, maxWidth, elide);
                ranges = [new Range(0, text.Length, new TextSpan(text, font))];
                truncated = true;
            }
        }
        var primary = ranges.Count > 0 ? ranges[0].Span.Font : FontSpec.Default;
        var wrapping = wrap switch
        {
            TextLayout.Wrap.WrapAnywhere => WordWrapping.Character,
            TextLayout.Wrap.WrapAtWordBoundaryOrAnywhere => WordWrapping.EmergencyBreak,
            TextLayout.Wrap.WordWrap => WordWrapping.Wrap,
            _ => WordWrapping.NoWrap,
        };
        var width = finite && (wrap != TextLayout.Wrap.NoWrap || elide == TextLayout.Elide.Right) ? (float)maxWidth : 1_000_000f;
        var created = DirectWriteServices.Current.CreateLayout(text.Length == 0 ? " " : text, primary, wrapping, width, 1_000_000f, elide == TextLayout.Elide.Right && finite, lineHeight, proportional);
        var result = new DirectWriteLayout(created.Layout, created.Ellipsis, text);
        try
        {
            result.ApplyRanges(ranges);
            if (maxLines > 0 && result.LimitLines(maxLines, elide != TextLayout.Elide.None))
                truncated = true;
            result.ReadMetrics(wrap == TextLayout.Wrap.NoWrap);
            result.Truncated |= truncated;
            if (text.Length > 0)
                result.FontRuns = CollectRuns(created.Layout);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public double CaretX(int position)
    {
        if (_layout is null || Source.Length == 0)
            return 0;
        position = Math.Clamp(position, 0, Source.Length);
        return HitX((uint)position, trailing: false);
    }

    public (double X, double Y, double Height) CaretBox(int position)
    {
        if (_layout is null || Source.Length == 0)
            return (0, 0, 0);
        var hit = _layout.HitTestTextPosition((uint)Math.Clamp(position, 0, Source.Length), false, out var point);
        return (point.X, point.Y, hit.Height);
    }

    public int PositionAt(double x, double y)
    {
        if (_layout is null || Source.Length == 0)
            return 0;
        var hit = _layout.HitTestPoint((float)x, (float)Math.Max(0, y), out var trailing, out _);
        var index = (int)hit.TextPosition;
        if (trailing)
            index += Math.Max(1, (int)hit.Length);
        return Math.Clamp(index, 0, Source.Length);
    }

    public void UnderlineRange(int start, int length)
    {
        if (_layout is null || length <= 0 || start >= Source.Length)
            return;
        start = Math.Max(0, start);
        var count = Math.Min(length, Source.Length - start);
        if (count > 0)
            _layout.SetUnderline(true, new TextRange((uint)start, (uint)count));
    }

    /// <summary>
    /// Draws the text, and for <c>Text.Outline</c>, <c>Raised</c> or <c>Sunken</c> its style copies first, in one Direct2D draw.
    /// The layout keeps the alignment and box width it was last drawn with, so hit tests match what is on screen.
    /// </summary>
    public void Draw(SKCanvas canvas, double boxWidth, int horizontalAlignment, QColor color, double offsetY, bool antialias, PaintContext? paint, int textStyle, QColor? styleColor)
    {
        if (_layout is null)
            return;
        var inLayer = (paint?.LayerDepth ?? 0) > 0;
        var mode = TextRaster.Choose((paint?.TextOnBackdrop ?? false) || inLayer, antialias);
        var hasBox = boxWidth > 0 && !double.IsNaN(boxWidth);
        // A wrap with no width is laid out at the unbounded sentinel. Centre and right alignment
        // would then paint around the middle of that width, off the item. Align inside the box.
        Arrange(Alignment(horizontalAlignment), hasBox && (!_wraps || _builtWidth >= 999_999f) ? (float)boxWidth : _builtWidth);
        var width = (float)(hasBox ? boxWidth : Width);
        var y = (float)offsetY;
        var offsets = Offsets(textStyle);
        Span<TextPass> passes = stackalloc TextPass[offsets.Length + 1];
        var shadow = styleColor ?? QColor.Black;
        for (var i = 0; i < offsets.Length; i++)
            passes[i] = new TextPass(offsets[i].X, y + offsets[i].Y, shadow, UseSpans: false);
        passes[^1] = new TextPass(0, y, color, UseSpans: true);
        // The 8 DIP margin holds the 1 DIP style offsets too. Growing the box for them would move
        // the text off the pixel grid when the box is scaled.
        var box = new SKRect(0, y, Math.Max(width, 1), y + (float)Math.Max(Height, 1));
        box.Inflate(8, 8);
        DirectWriteServices.Current.Blit(_layout, _colors, canvas, box, passes, mode, readRoot: !inLayer);
    }

    /// <summary>
    /// How many times a draw changed the layout's alignment or width. DirectWrite lays the text out again
    /// after each change, so a repaint with the same box changes nothing.
    /// </summary>
    internal int Relayouts { get; private set; }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~DirectWriteLayout() => Release();

    private void Arrange(TextAlignment alignment, float maxWidth)
    {
        if (alignment != _alignment)
        {
            _layout!.TextAlignment = alignment;
            _alignment = alignment;
            Relayouts++;
        }
        if (maxWidth != _maxWidth)
        {
            _layout!.MaxWidth = maxWidth;
            _maxWidth = maxWidth;
            Relayouts++;
        }
    }

    private void ApplyRanges(List<Range> ranges)
    {
        if (_layout is null)
            return;
        using var spacing = _layout.QueryInterfaceOrNull<IDWriteTextLayout1>();
        foreach (var range in ranges)
        {
            if (range.Length <= 0)
                continue;
            var textRange = new TextRange((uint)range.Start, (uint)range.Length);
            var font = range.Span.Font;
            try
            {
                _layout.SetFontFamilyName(DirectWriteServices.ResolveFamily(font.Family), textRange);
            }
            catch (SharpGenException)
            {
            }
            _layout.SetFontWeight((FontWeight)Math.Clamp(font.Weight, 1, 999), textRange);
            _layout.SetFontStyle(font.Italic ? FontStyle.Italic : FontStyle.Normal, textRange);
            var size = (float)font.PixelSize;
            if (!float.IsNaN(size) && size >= 1)
                _layout.SetFontSize(size, textRange);
            if (font.Underline)
                _layout.SetUnderline(true, textRange);
            if (font.Strikeout)
                _layout.SetStrikethrough(true, textRange);
            if (spacing is not null && font.LetterSpacing != 0)
                spacing.SetCharacterSpacing(0, (float)font.LetterSpacing, 0, textRange);
            if (range.Span.Color is { } color)
                _colors.Add((range.Start, range.Length, color));
        }
    }

    private bool LimitLines(int maxLines, bool ellipsis)
    {
        if (_layout is null)
            return false;
        var raw = LineMetricsOf(_layout);
        if (raw.Length <= maxLines)
            return false;
        var height = 0f;
        for (var i = 0; i < maxLines; i++)
            height += raw[i].Height;
        if (ellipsis)
            EnsureEllipsis();
        else
        {
            // DirectWrite keeps drawing the extra lines unless trimming is on. A cap with no ellipsis still trims, and draws no sign.
            _layout.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, null);
        }
        _layout.MaxHeight = Math.Max(1, height);
        // Line metrics still list the hidden lines, so the reported height stops at the cap.
        _reportedLines = maxLines;
        return true;
    }

    private void EnsureEllipsis()
    {
        if (_layout is null || _ellipsis is not null)
            return;
        var created = DirectWriteServices.Current.CreateLayout(" ", FontSpec.Default, WordWrapping.NoWrap, 10, 10, ellipsis: true, 1, true);
        created.Layout.Dispose();
        _ellipsis = created.Ellipsis;
        if (_ellipsis is not null)
            _layout.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, _ellipsis);
    }

    private void ReadMetrics(bool includeTrailingWhitespace)
    {
        if (_layout is null)
            return;
        var raw = LineMetricsOf(_layout);
        uint pos = 0;
        double y = 0;
        var seen = 0;
        foreach (var rawLine in raw)
        {
            if (_reportedLines > 0 && seen >= _reportedLines)
                break;
            seen++;
            var content = rawLine.Length - rawLine.NewlineLength;
            var ink = includeTrailingWhitespace ? content : content - Math.Min(content, rawLine.TrailingWhitespaceLength);
            var width = ink > 0 ? Extent(pos, ink) : 0;
            Lines.Add(new TextLayout.Line
            {
                Width = width,
                Ascent = rawLine.Baseline,
                Descent = Math.Max(0, rawLine.Height - rawLine.Baseline),
                Y = y,
                Height = rawLine.Height,
            });
            y += rawLine.Height;
            pos += rawLine.Length;
            if (rawLine.IsTrimmed)
                Truncated = true;
        }
        if (Lines.Count == 0)
        {
            var height = Math.Max(1, _layout.Metrics.Height);
            Lines.Add(new TextLayout.Line { Height = height, Ascent = height * 0.8, Descent = height * 0.2 });
            y = height;
        }
        Height = y;
        Width = Lines.Max(line => line.Width);
        if (Source.Length == 0)
        {
            Width = 0;
            Lines[0].Width = 0;
        }
    }

    /// <summary>
    /// The width a range covers. A right-to-left run starts on its right edge, so the width comes from
    /// the leftmost and rightmost edges of the boxes the range hits, not from its first and last caret.
    /// </summary>
    private double Extent(uint position, uint length)
    {
        var boxes = ArrayPool<HitTestMetrics>.Shared.Rent((int)length);
        try
        {
            _layout!.HitTestTextRange(position, length, 0, 0, boxes, out var count);
            var left = float.MaxValue;
            var right = float.MinValue;
            for (var i = 0; i < count; i++)
            {
                left = Math.Min(left, boxes[i].Left);
                right = Math.Max(right, boxes[i].Left + boxes[i].Width);
            }
            return count == 0 ? 0 : Math.Max(0, right - left);
        }
        finally
        {
            ArrayPool<HitTestMetrics>.Shared.Return(boxes);
        }
    }

    private float HitX(uint position, bool trailing)
    {
        _layout!.HitTestTextPosition(position, trailing, out var point);
        return point.X;
    }

    private void Release()
    {
        var layout = Interlocked.Exchange(ref _layout, null);
        var sign = Interlocked.Exchange(ref _ellipsis, null);
        layout?.Dispose();
        sign?.Dispose();
    }

    private static double Measure(string text, FontSpec font)
    {
        using var layout = Create([new TextSpan(text, font with { Capitalization = 0 })], double.NaN, TextLayout.Wrap.NoWrap, TextLayout.Elide.None, 0, 1, true);
        return layout.Width;
    }

    private static string CutToWidth(string text, FontSpec font, double maxWidth, TextLayout.Elide mode)
    {
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Measure(Cut(text, mid, mode), font) <= maxWidth)
                lo = mid;
            else
                hi = mid - 1;
        }
        return Cut(text, lo, mode);
    }

    private static string Cut(string text, int keep, TextLayout.Elide mode)
    {
        const string ellipsis = "…";
        keep = Math.Clamp(keep, 0, text.Length);
        return mode switch
        {
            TextLayout.Elide.Left => ellipsis + text[TextClusters.Ceil(text, text.Length - keep)..],
            TextLayout.Elide.Middle => text[..TextClusters.Snap(text, (keep + 1) / 2)] + ellipsis + text[TextClusters.Ceil(text, text.Length - keep / 2)..],
            _ => text[..TextClusters.Snap(text, keep)].TrimEnd() + ellipsis,
        };
    }

    private static (string Text, List<Range> Ranges) Assemble(IReadOnlyList<TextSpan> spans)
    {
        var sb = new System.Text.StringBuilder();
        var ranges = new List<Range>();
        foreach (var span in spans)
        {
            var text = Capitalize(span.Text ?? "", span.Font.Capitalization).Replace('\t', ' ');
            if (text.Length == 0)
                continue;
            ranges.Add(new Range(sb.Length, text.Length, span with { Text = text }));
            sb.Append(text);
        }
        return (sb.ToString(), ranges);
    }

    private static string Capitalize(string text, int mode) => mode switch
    {
        1 => text.ToUpperInvariant(),
        2 => text.ToLowerInvariant(),
        4 => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text),
        _ => text,
    };

    private static TextAlignment Alignment(int horizontal)
    {
        if ((horizontal & QtEnums.AlignRight) != 0)
            return TextAlignment.Trailing;
        if ((horizontal & QtEnums.AlignHCenter) != 0)
            return TextAlignment.Center;
        return TextAlignment.Leading;
    }

    private static readonly (float X, float Y)[] Outline = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];
    private static readonly (float X, float Y)[] Raised = [(0, 1)];
    private static readonly (float X, float Y)[] Sunken = [(0, -1)];

    private static (float X, float Y)[] Offsets(int style) => style switch
    {
        1 => Outline,
        2 => Raised,
        3 => Sunken,
        _ => [],
    };

    private static List<FontRun> CollectRuns(IDWriteTextLayout layout)
    {
        var collector = new Collector();
        layout.Draw(0, collector, 0, 0);
        return collector.Runs;
    }

    private static LineMetrics[] LineMetricsOf(IDWriteTextLayout layout)
    {
        var count = (int)layout.Metrics.LineCount;
        if (count <= 0)
            return [];
        var lines = new LineMetrics[count];
        layout.GetLineMetrics(lines, out var actual);
        return actual == (uint)count ? lines : lines[..(int)actual];
    }

    private static string LocalizedName(IDWriteLocalizedStrings names) => names.Count == 0 ? "" : names.GetString(0);

    private sealed class Collector : TextRendererBase
    {
        public List<FontRun> Runs { get; } = [];

        public override RawBool IsPixelSnappingDisabled(nint clientDrawingContext) => false;

        public override Matrix3x2 GetCurrentTransform(nint clientDrawingContext) => Matrix3x2.Identity;

        public override float GetPixelsPerDip(nint clientDrawingContext) => 1;

        public override void DrawGlyphRun(nint clientDrawingContext, float baselineOriginX, float baselineOriginY, MeasuringMode measuringMode, GlyphRun glyphRun, GlyphRunDescription glyphRunDescription, IUnknown clientDrawingEffect)
        {
            var text = glyphRunDescription.Text ?? "";
            var family = "";
            var optical = 0f;
            if (glyphRun.FontFace is { } face)
            {
                try
                {
                    using var face3 = face.QueryInterfaceOrNull<IDWriteFontFace3>();
                    if (face3?.FamilyNames is { } names)
                    {
                        using (names)
                            family = LocalizedName(names);
                    }
                }
                catch (SharpGenException)
                {
                }
                try
                {
                    using var face5 = face.QueryInterfaceOrNull<IDWriteFontFace5>();
                    if (face5 is not null && face5.FontAxisValueCount > 0)
                    {
                        var axes = new FontAxisValue[face5.FontAxisValueCount];
                        face5.GetFontAxisValues(axes, face5.FontAxisValueCount);
                        foreach (var axis in axes)
                        {
                            if (axis.AxisTag == FontAxisTag.OpticalSize)
                                optical = axis.Value;
                        }
                    }
                }
                catch (SharpGenException)
                {
                }
            }
            Runs.Add(new FontRun(family, text, optical));
        }

        public override void DrawInlineObject(nint clientDrawingContext, float originX, float originY, IDWriteInlineObject inlineObject, RawBool isSideways, RawBool isRightToLeft, IUnknown clientDrawingEffect)
        {
        }

        public override void DrawStrikethrough(nint clientDrawingContext, float baselineOriginX, float baselineOriginY, ref Strikethrough strikethrough, IUnknown clientDrawingEffect)
        {
        }

        public override void DrawUnderline(nint clientDrawingContext, float baselineOriginX, float baselineOriginY, ref Underline underline, IUnknown clientDrawingEffect)
        {
        }
    }
}
