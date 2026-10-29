#pragma warning disable CA1416

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

internal sealed class DirectWriteEngine
{
    private readonly IDWriteFactory7 _factory;
    private readonly ID2D1Factory1 _d2d;
    private readonly IWICImagingFactory _wic;
    private readonly IDWriteFontFallback _fallback;
    private readonly IDWriteFontCollection? _typographic;
    private readonly IDWriteRenderingParams _rendering;

    private DirectWriteEngine(IDWriteFactory7 factory, ID2D1Factory1 d2d, IWICImagingFactory wic, IDWriteFontFallback fallback, IDWriteFontCollection? typographic, IDWriteRenderingParams rendering)
    {
        _factory = factory;
        _d2d = d2d;
        _wic = wic;
        _fallback = fallback;
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
        return new DirectWriteEngine(factory, d2d, wic, BuildFallback(factory), typographic, factory.CreateRenderingParams());
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
    /// Draws the layout into the Skia canvas. Axis-aligned text is read back in device pixels so ClearType
    /// blends with what is already painted and the scale matches the canvas. Rotated text is grayscale.
    /// </summary>
    internal void Blit(IDWriteTextLayout layout, int textLength, IReadOnlyList<(int Start, int Length, QColor Color)> spans, SKCanvas canvas, SKRect dipBox, float originX, float originY, TextPixelMode mode, QColor color, bool useSpanColors)
    {
        var matrix = canvas.TotalMatrix;
        var axis = Math.Abs(matrix.SkewX) < 1e-3f && Math.Abs(matrix.SkewY) < 1e-3f && matrix.ScaleX > 0.01f && matrix.ScaleY > 0.01f;
        if (!axis)
        {
            BlitTransformed(layout, textLength, spans, canvas, dipBox, originX, originY, mode == TextPixelMode.Aliased ? TextAntialiasMode.Aliased : TextAntialiasMode.Grayscale, color, useSpanColors);
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
        var info = new SKImageInfo(pixelW, pixelH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var read = new SKBitmap(info);
        if (!TryRead(canvas, read, left, top))
        {
            var fallback = mode == TextPixelMode.Aliased ? TextAntialiasMode.Aliased : TextAntialiasMode.Grayscale;
            BlitTransformed(layout, textLength, spans, canvas, dipBox, originX, originY, fallback, color, useSpanColors);
            return;
        }
        var dipLeft = (left - matrix.TransX) / matrix.ScaleX;
        var dipTop = (top - matrix.TransY) / matrix.ScaleY;
        DrawWic(layout, textLength, spans, read, matrix.ScaleX, matrix.ScaleY, originX - dipLeft, originY - dipTop, aa, color, useSpanColors, copyIn: true);
        canvas.Save();
        canvas.ResetMatrix();
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawBitmap(read, left, top, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
        canvas.Restore();
    }

    private static bool TryRead(SKCanvas canvas, SKBitmap destination, int x, int y)
    {
        var surface = canvas.Surface;
        return surface is not null && surface.ReadPixels(destination.Info, destination.GetPixels(), destination.RowBytes, x, y);
    }

    private void BlitTransformed(IDWriteTextLayout layout, int textLength, IReadOnlyList<(int Start, int Length, QColor Color)> spans, SKCanvas canvas, SKRect dipBox, float originX, float originY, TextAntialiasMode aa, QColor color, bool useSpanColors)
    {
        var scale = Math.Max(1f, Math.Max(Math.Abs(canvas.TotalMatrix.ScaleX), Math.Abs(canvas.TotalMatrix.ScaleY)));
        var pixelW = Math.Max(1, (int)Math.Ceiling(dipBox.Width * scale));
        var pixelH = Math.Max(1, (int)Math.Ceiling(dipBox.Height * scale));
        if ((long)pixelW * pixelH > 4096L * 4096L)
            return;
        var info = new SKImageInfo(pixelW, pixelH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        var dpiScaleX = pixelW / Math.Max(1f, dipBox.Width);
        var dpiScaleY = pixelH / Math.Max(1f, dipBox.Height);
        DrawWic(layout, textLength, spans, bitmap, dpiScaleX, dpiScaleY, originX - dipBox.Left, originY - dipBox.Top, aa, color, useSpanColors, copyIn: false);
        canvas.DrawBitmap(bitmap, dipBox, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint: null);
    }

    private void DrawWic(IDWriteTextLayout layout, int textLength, IReadOnlyList<(int Start, int Length, QColor Color)> spans, SKBitmap bitmap, float scaleX, float scaleY, float originX, float originY, TextAntialiasMode aa, QColor color, bool useSpanColors, bool copyIn)
    {
        lock (DirectWriteServices.Gate)
        {
            var wic = _wic.CreateBitmap((uint)bitmap.Width, (uint)bitmap.Height, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
            try
            {
                if (copyIn)
                    Copy(bitmap, wic, toWic: true);
                var props = new RenderTargetProperties(RenderTargetType.Default, new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96f * scaleX, 96f * scaleY, RenderTargetUsage.None, FeatureLevel.Default);
                var target = _d2d.CreateWicBitmapRenderTarget(wic, props);
                var brushes = new List<ID2D1Brush>();
                try
                {
                    target.TextAntialiasMode = aa;
                    target.TextRenderingParams = _rendering;
                    target.BeginDraw();
                    if (!copyIn)
                        target.Clear(new Color4(0, 0, 0, 0));
                    var fill = target.CreateSolidColorBrush(ToColor(color), null);
                    brushes.Add(fill);
                    if (textLength > 0)
                        layout.SetDrawingEffect(fill, new TextRange(0, (uint)textLength));
                    if (useSpanColors)
                    {
                        foreach (var (start, length, spanColor) in spans)
                        {
                            if (length <= 0)
                                continue;
                            var brush = target.CreateSolidColorBrush(ToColor(spanColor), null);
                            brushes.Add(brush);
                            layout.SetDrawingEffect(brush, new TextRange((uint)start, (uint)length));
                        }
                    }
                    target.DrawTextLayout(new Vector2(originX, originY), layout, fill, DrawTextOptions.EnableColorFont);
                    target.EndDraw().CheckError();
                }
                finally
                {
                    if (textLength > 0)
                        layout.SetDrawingEffect(null!, new TextRange(0, (uint)textLength));
                    foreach (var brush in brushes)
                        brush.Dispose();
                    target.Dispose();
                }
                Copy(bitmap, wic, toWic: false);
            }
            finally
            {
                wic.Dispose();
            }
        }
    }

    private static Color4 ToColor(QColor color) => new((float)color.R, (float)color.G, (float)color.B, (float)color.A);

    private static unsafe void Copy(SKBitmap bitmap, IWICBitmap wic, bool toWic)
    {
        using var locker = wic.Lock(BitmapLockFlags.Read | BitmapLockFlags.Write);
        var data = locker.Data;
        var width = bitmap.Width * 4;
        var height = bitmap.Height;
        var srcPitch = toWic ? bitmap.RowBytes : (int)data.Pitch;
        var dstPitch = toWic ? (int)data.Pitch : bitmap.RowBytes;
        var src = toWic ? (byte*)bitmap.GetPixels() : (byte*)data.DataPointer;
        var dst = toWic ? (byte*)data.DataPointer : (byte*)bitmap.GetPixels();
        for (var y = 0; y < height; y++)
            Buffer.MemoryCopy(src + y * srcPitch, dst + y * dstPitch, width, width);
    }

    private IDWriteTextFormat CreateFormat(FontSpec font)
    {
        var size = (float)font.PixelSize;
        if (float.IsNaN(size) || size < 1)
            size = (float)FontSpec.DefaultPixelSize;
        var weight = (FontWeight)Math.Clamp(font.Weight, 1, 999);
        var style = font.Italic ? FontStyle.Italic : FontStyle.Normal;
        var resolved = DirectWriteServices.ResolveFamily(font.Family);
        IDWriteTextFormat? format = null;
        foreach (var name in new[] { resolved, "Segoe UI Variable", "Segoe UI" })
        {
            format = TryFormat(name, _typographic, weight, style, size) ?? TryFormat(name, null, weight, style, size);
            if (format is not null)
                break;
        }
        format ??= _factory.CreateTextFormat("Segoe UI", weight, style, FontStretch.Normal, size);
        using var format1 = format.QueryInterfaceOrNull<IDWriteTextFormat1>();
        if (format1 is not null)
            format1.FontFallback = _fallback;
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

    private IDWriteTextFormat? TryFormat(string family, IDWriteFontCollection? collection, FontWeight weight, FontStyle style, float size)
    {
        try
        {
            return collection is null
                ? _factory.CreateTextFormat(family, weight, style, FontStretch.Normal, size)
                : _factory.CreateTextFormat(family, collection, weight, style, FontStretch.Normal, size, "");
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

    private static IDWriteFontFallback BuildFallback(IDWriteFactory7 factory)
    {
        try
        {
            using var builder = factory.CreateFontFallbackBuilder();
            Map(builder, [Range(0xE700, 0xF8FF)], "Segoe Fluent Icons", "Segoe MDL2 Assets");
            Map(builder, [Range(0x1F300, 0x1FAFF), Range(0x2600, 0x27BF), Range(0xFE0E, 0xFE0F), Range(0x200D, 0x200D), Range(0x20E3, 0x20E3)], "Segoe UI Emoji");
            Map(builder, [Range(0x3040, 0x30FF), Range(0x31F0, 0x31FF), Range(0x3400, 0x9FFF)], "Yu Gothic UI", "Yu Gothic", "Microsoft YaHei UI", "Meiryo");
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
    private readonly List<(int Start, int Length, QColor Color)> _colors = [];

    private DirectWriteLayout(IDWriteTextLayout layout, IDWriteInlineObject? ellipsis, string source)
    {
        _layout = layout;
        _ellipsis = ellipsis;
        Source = source;
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

    public void Draw(SKCanvas canvas, double boxWidth, int horizontalAlignment, QColor color, double offsetY, bool antialias, PaintContext? paint, int textStyle, QColor? styleColor)
    {
        if (_layout is null)
            return;
        var mode = TextRaster.Choose(paint?.TextOnBackdrop ?? false, antialias);
        var previousAlign = _layout.TextAlignment;
        var previousWidth = _layout.MaxWidth;
        var wrapping = _layout.WordWrapping != WordWrapping.NoWrap;
        try
        {
            _layout.TextAlignment = Alignment(horizontalAlignment);
            if (!wrapping && boxWidth > 0 && !double.IsNaN(boxWidth))
                _layout.MaxWidth = (float)boxWidth;
            var width = (float)(boxWidth > 0 && !double.IsNaN(boxWidth) ? boxWidth : Width);
            if (textStyle != 0)
            {
                var shadow = styleColor ?? QColor.Black;
                foreach (var (dx, dy) in Offsets(textStyle))
                    Pass(canvas, dx, (float)offsetY + dy, width, mode, shadow, useSpans: false);
            }
            Pass(canvas, 0, (float)offsetY, width, mode, color, useSpans: true);
        }
        finally
        {
            _layout.TextAlignment = previousAlign;
            _layout.MaxWidth = previousWidth;
        }
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~DirectWriteLayout() => Release();

    private void Pass(SKCanvas canvas, float dx, float originY, float width, TextPixelMode mode, QColor color, bool useSpans)
    {
        if (_layout is null)
            return;
        var box = new SKRect(dx, originY, dx + Math.Max(width, 1), originY + (float)Math.Max(Height, 1));
        box.Inflate(8, 8);
        DirectWriteServices.Current.Blit(_layout, Source.Length, _colors, canvas, box, dx, originY, mode, color, useSpans);
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
        _layout.MaxHeight = Math.Max(1, height);
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
        foreach (var rawLine in raw)
        {
            var content = rawLine.Length - rawLine.NewlineLength;
            var ink = includeTrailingWhitespace ? content : content - Math.Min(content, rawLine.TrailingWhitespaceLength);
            double width = 0;
            if (ink > 0)
            {
                var start = HitX(pos, trailing: false);
                var end = HitX(pos + ink - 1, trailing: true);
                width = Math.Max(0, end - start);
            }
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
        else if (includeTrailingWhitespace && Lines.Count == 1)
        {
            Width = CaretX(Source.Length);
            Lines[0].Width = Width;
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

    private static (float X, float Y)[] Offsets(int style) => style switch
    {
        1 => [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)],
        2 => [(0, 1)],
        3 => [(0, -1)],
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
