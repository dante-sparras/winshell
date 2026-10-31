using System.Globalization;
using Jint;
using Jint.Native;
using SkiaSharp;

namespace Winshell.Runtime.Quick.Canvas;

/// <summary>
/// QtQuick's <c>Canvas</c>: an item scripts draw on with a 2D context, in <c>onPaint</c>. What is drawn stays
/// until it is drawn over or cleared; <c>requestPaint()</c> asks for <c>onPaint</c> again.
/// </summary>
[QmlSignal("paint", "region")]
[QmlSignal("painted")]
[QmlSignal("imageLoaded")]
public sealed class Canvas : Item
{
    private SKSurface? _surface;
    private (int W, int H, double Dpr) _surfaceKey;
    private Context2D? _context;
    private bool _paintRequested;
    private int _renderTarget;
    private int _renderStrategy;
    private string _contextType = "";
    private QSize? _canvasSize;
    private readonly Action _frame;
    private readonly Dictionary<int, JsValue> _animationCallbacks = [];
    private int _nextAnimationId = 1;
    private readonly Dictionary<string, SKImage?> _images = new(StringComparer.Ordinal);

    public Canvas()
    {
        _frame = RunPaint;
        Theme.Changed += OnTheme;
    }

    [QmlEnum]
    public enum RenderTargetValue
    {
        Image = 0,
        FramebufferObject = 1,
    }

    [QmlEnum]
    public enum RenderStrategyValue
    {
        Immediate = 0,
        Threaded = 1,
        Cooperative = 2,
    }

    [Qml] public bool Available => true;

    [Qml] public int RenderTarget { get { Track(); return _renderTarget; } set => SetField(ref _renderTarget, value); }

    [Qml] public int RenderStrategy { get { Track(); return _renderStrategy; } set => SetField(ref _renderStrategy, value); }

    [Qml] public string ContextType { get { Track(); return _contextType; } set => SetField(ref _contextType, value ?? ""); }

    [Qml] public QObject? Context { get { Track(); return _context; } }

    [Qml]
    public QSize CanvasSize
    {
        get { Track(); return _canvasSize ?? new QSize(Width, Height); }
        set { _canvasSize = value; NotifyChanged("canvasSize"); RequestPaint(); }
    }

    [Qml] public QRect CanvasWindow { get => new(0, 0, Width, Height); set { } }

    [Qml] public QSize TileSize { get => CanvasSize; set { } }

    private const int MaxSurfaceSide = 16384;
    private const double MaxSurfacePixels = 64 * 1024 * 1024;

    /// <summary>
    /// The surface size in pixels and the pixels per canvas unit: the device pixel ratio, lowered so the surface
    /// stays within 16384 pixels a side and 64 megapixels.
    /// </summary>
    private (int W, int H, double Dpr) SurfaceKey()
    {
        var size = CanvasSize;
        if (!(size.Width > 0 && size.Height > 0 && double.IsFinite(size.Width) && double.IsFinite(size.Height)))
            return default;
        var dpr = Host?.DevicePixelRatio ?? 1;
        var scale = Math.Min(Math.Min(dpr, MaxSurfaceSide / size.Width), Math.Min(MaxSurfaceSide / size.Height, Math.Sqrt(MaxSurfacePixels / (size.Width * size.Height))));
        var w = (int)Math.Clamp(Math.Ceiling(size.Width * scale), 1, MaxSurfaceSide);
        var h = (int)Math.Clamp(Math.Ceiling(size.Height * scale), 1, MaxSurfaceSide);
        return (w, h, scale);
    }

    /// <summary>The surface the context draws on, in pixels, made again when the size changes.</summary>
    internal SKSurface? Surface
    {
        get
        {
            var key = SurfaceKey();
            if (key.W <= 0)
                return null;
            if (_surfaceKey != key)
            {
                _surface?.Dispose();
                _surface = SKSurface.Create(new SKImageInfo(key.W, key.H, SKColorType.Rgba8888, SKAlphaType.Premul));
                _surfaceKey = key;
                if (key.Dpr < (Host?.DevicePixelRatio ?? 1))
                    Engine?.Log.Warn("scene", $"Canvas: {CanvasSize.Width}x{CanvasSize.Height} is over the surface cap; drawn at {key.W}x{key.H} pixels");
                if (_surface is null)
                    Engine?.Log.Warn("scene", $"Canvas: could not make a {key.W}x{key.H} surface");
                _surface?.Canvas.Clear(SKColors.Transparent);
                _context?.OnSurfaceChanged();
            }
            return _surface;
        }
    }

    /// <summary>Surface pixels per canvas unit.</summary>
    internal double PixelRatio => _surfaceKey.Dpr <= 0 ? 1 : _surfaceKey.Dpr;

    [Qml]
    public QObject? GetContext(string contextId, params object?[] args)
    {
        if (!string.Equals(contextId, "2d", StringComparison.OrdinalIgnoreCase))
            return null;
        if (_context is null)
        {
            _context = new Context2D(this) { Engine = Engine };
            _context.SetOwner(this);
            _contextType = "2d";
            NotifyChanged("contextType");
            NotifyChanged("context");
        }
        return _context;
    }

    [Qml]
    public void RequestPaint()
    {
        _paintRequested = true;
        Update();
        Engine?.Loop.RequestFrame(_frame);
    }

    [Qml]
    public void MarkDirty(object? area = null) => RequestPaint();

    [Qml]
    public int RequestAnimationFrame(JsValue callback)
    {
        var id = _nextAnimationId++;
        _animationCallbacks[id] = callback;
        Engine?.Loop.RequestFrame(RunAnimationFrames);
        return id;
    }

    [Qml]
    public void CancelRequestAnimationFrame(int handle) => _animationCallbacks.Remove(handle);

    private void RunAnimationFrames()
    {
        if (IsDestroyed || Engine is null)
            return;
        var callbacks = _animationCallbacks.Values.ToList();
        _animationCallbacks.Clear();
        var time = Engine.Loop.Now.TotalMilliseconds;
        foreach (var cb in callbacks)
        {
            try
            {
                Engine.Js.CallFunction(cb, null, [time]);
            }
            catch (Jint.Runtime.JavaScriptException e)
            {
                Engine.Log.Warn("scene", $"Canvas: {e.Message}");
            }
        }
    }

    private void RunPaint()
    {
        if (!_paintRequested || IsDestroyed)
            return;
        if (Surface is null)
            return;
        _paintRequested = false;
        Emit("paint", new QRect(0, 0, Width, Height));
        _context?.Flush();
        Emit("painted");
        Update();
    }

    // Images

    internal SKImage? ImageFor(string url)
    {
        if (_images.TryGetValue(url, out var image))
            return image;
        var baseDir = CreationContext?.Imports?.Directory ?? Engine?.ShellDir;
        image = ImageLoader.IsRemote(url) ? null : ImageLoader.Load(url, baseDir, 0, 0);
        if (_images.Remove(url, out var previous))
            previous?.Dispose();
        _images[url] = image;
        return image;
    }

    [Qml]
    public void LoadImage(string url)
    {
        if (ImageFor(url) is not null)
            Emit("imageLoaded");
    }

    [Qml]
    public void UnloadImage(string url)
    {
        if (_images.Remove(url, out var image))
            image?.Dispose();
    }

    private void OnTheme()
    {
        if (IsDestroyed)
            return;
        foreach (var url in _images.Keys.Where(item => item.StartsWith("image://", StringComparison.OrdinalIgnoreCase)).ToArray())
            UnloadImage(url);
    }

    [Qml]
    public bool IsImageLoaded(string url) => _images.TryGetValue(url, out var i) && i is not null;

    [Qml]
    public bool IsImageLoading(string url) => false;

    [Qml]
    public bool IsImageError(string url) => _images.TryGetValue(url, out var i) && i is null;

    /// <summary>Writes what has been drawn. The suffix picks the format, as Qt's <c>save</c> does. A relative path is next to the QML file.</summary>
    [Qml]
    public bool Save(string filename)
    {
        var path = ImageLoader.LocalPath(filename ?? "", CreationContext?.Imports?.Directory ?? Engine?.ShellDir);
        if (_surface is null || path is null || !TryFormat(Path.GetExtension(path).TrimStart('.'), out var format))
            return false;
        using var data = Encode(format);
        if (data is null)
            return false;
        try
        {
            File.WriteAllBytes(path, data.ToArray());
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A data URL of what has been drawn. The default type is <c>image/png</c>, as in Qt.</summary>
    [Qml]
    public string ToDataURL(string mimeType = "image/png")
    {
        if (_surface is null)
            return "data:,";
        var mime = string.IsNullOrEmpty(mimeType) ? "image/png" : mimeType;
        var slash = mime.IndexOf('/');
        if (!TryFormat(slash >= 0 ? mime[(slash + 1)..] : mime, out var format))
            return "";
        using var data = Encode(format);
        return data is null ? "" : "data:" + mime + ";base64," + Convert.ToBase64String(data.ToArray());
    }

    private SKData? Encode(SKEncodedImageFormat format)
    {
        using var image = _surface!.Snapshot();
        return image.Encode(format, 100);
    }

    /// <summary>The formats Qt's canvas writes with <c>QImage</c>: png, jpeg and bmp.</summary>
    private static bool TryFormat(string name, out SKEncodedImageFormat format)
    {
        switch (name.ToLowerInvariant())
        {
            case "png": format = SKEncodedImageFormat.Png; return true;
            case "jpg":
            case "jpeg": format = SKEncodedImageFormat.Jpeg; return true;
            case "bmp": format = SKEncodedImageFormat.Bmp; return true;
            default: format = default; return false;
        }
    }

    /// <summary>A snapshot of what has been drawn, for drawing one canvas onto another.</summary>
    internal SKImage? Snapshot() => _surface?.Snapshot();

    // Item

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        RequestPaint();
    }

    protected override void OnGeometryChanged()
    {
        base.OnGeometryChanged();
        if (IsComplete && SurfaceKey() != _surfaceKey)
            RequestPaint();
    }

    protected override void OnHostChanged()
    {
        base.OnHostChanged();
        if (IsComplete)
            RequestPaint();
    }

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        if (_paintRequested && Engine is null)
            RunPaint();
        if (_surface is null || Width <= 0 || Height <= 0)
            return;
        using var image = _surface.Snapshot();
        var size = CanvasSize;
        canvas.DrawImage(image, SKRect.Create(0, 0, (float)size.Width, (float)size.Height), new SKSamplingOptions(SKFilterMode.Linear), null);
    }

    protected override void OnDestroy()
    {
        Theme.Changed -= OnTheme;
        foreach (var url in _images.Keys.ToArray())
            UnloadImage(url);
        _surface?.Dispose();
        _surface = null;
        base.OnDestroy();
    }
}

/// <summary>A gradient made by a 2D context, with stops added by <c>addColorStop</c>.</summary>
public sealed class CanvasGradient : QObject
{
    internal enum Kind
    {
        Linear,
        Radial,
        Conical,
    }

    private readonly List<(float Offset, SKColor Color)> _stops = [];

    internal CanvasGradient(Kind kind, float[] args)
    {
        GradientKind = kind;
        Args = args;
    }

    internal Kind GradientKind { get; }

    internal float[] Args { get; }

    [Qml]
    public void AddColorStop(double offset, object? color)
    {
        if (CssColor.TryParse(color, out var c))
            _stops.Add(((float)Math.Clamp(offset, 0, 1), c.ToSk()));
    }

    internal SKShader? CreateShader()
    {
        if (_stops.Count == 0)
            return null;
        var stops = _stops.OrderBy(s => s.Offset).ToList();
        var colors = stops.Select(s => s.Color).ToArray();
        var positions = stops.Select(s => s.Offset).ToArray();
        if (colors.Length == 1)
        {
            colors = [colors[0], colors[0]];
            positions = [0, 1];
        }
        var a = Args;
        return GradientKind switch
        {
            Kind.Linear => SKShader.CreateLinearGradient(new SKPoint(a[0], a[1]), new SKPoint(a[2], a[3]), colors, positions, SKShaderTileMode.Clamp),
            Kind.Radial => SKShader.CreateTwoPointConicalGradient(new SKPoint(a[0], a[1]), a[2], new SKPoint(a[3], a[4]), a[5], colors, positions, SKShaderTileMode.Clamp),
            _ => SKShader.CreateSweepGradient(new SKPoint(a[0], a[1]), colors, positions,
                SKMatrix.CreateRotationDegrees(-a[2] * 180 / MathF.PI, a[0], a[1]).PreConcat(SKMatrix.CreateScale(1, -1, a[0], a[1]))),
        };
    }
}

/// <summary>What <c>measureText</c> returns.</summary>
public sealed class TextMetrics : QObject
{
    [Qml] public double Width { get; init; }

    [Qml] public double ActualBoundingBoxLeft { get; init; }

    [Qml] public double ActualBoundingBoxRight { get; init; }

    [Qml] public double ActualBoundingBoxAscent { get; init; }

    [Qml] public double ActualBoundingBoxDescent { get; init; }

    [Qml] public double FontBoundingBoxAscent { get; init; }

    [Qml] public double FontBoundingBoxDescent { get; init; }
}

/// <summary>Parses the color strings HTML canvas code uses: names, #hex, rgb(), rgba(), hsl(), hsla().</summary>
public static class CssColor
{
    public static bool TryParse(object? value, out QColor color)
    {
        value = ValueConverter.Unwrap(value);
        switch (value)
        {
            case QColor c:
                color = c;
                return true;
            case string s:
                return TryParse(s, out color);
            default:
                color = QColor.Invalid;
                return false;
        }
    }

    public static bool TryParse(string text, out QColor color)
    {
        if (QColor.TryParse(text, out color))
            return true;
        var s = text.Trim().ToLowerInvariant();
        var open = s.IndexOf('(');
        if (open < 0 || !s.EndsWith(')'))
            return false;
        var fn = s[..open].Trim();
        var parts = s[(open + 1)..^1].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return false;
        double Num(string p, double scale)
        {
            if (p.EndsWith('%'))
                return double.Parse(p[..^1], CultureInfo.InvariantCulture) / 100 * scale;
            return double.Parse(p.TrimEnd('d', 'e', 'g'), CultureInfo.InvariantCulture);
        }
        try
        {
            var alpha = parts.Length > 3 ? Math.Clamp(Num(parts[3], 1), 0, 1) : 1;
            if (fn is "rgb" or "rgba")
            {
                color = new QColor(Math.Clamp(Num(parts[0], 255) / 255, 0, 1), Math.Clamp(Num(parts[1], 255) / 255, 0, 1), Math.Clamp(Num(parts[2], 255) / 255, 0, 1), alpha);
                return true;
            }
            if (fn is "hsl" or "hsla")
            {
                color = QColor.FromHsl(Num(parts[0], 360) % 360 / 360, Math.Clamp(Num(parts[1], 1), 0, 1), Math.Clamp(Num(parts[2], 1), 0, 1), alpha);
                return true;
            }
        }
        catch (FormatException)
        {
        }
        return false;
    }
}

/// <summary>
/// The 2D context of a Canvas, as in HTML and Qt: paths, fills and strokes, text, images, transforms and the
/// state stack, drawn with Skia.
/// </summary>
public sealed class Context2D : QObject
{
    private readonly Canvas _canvas;
    private SKCanvas? _target;
    private State _state = new();
    private readonly Stack<State> _saved = new();
    private SKPathBuilder _path = new();
    private bool _hasCurrent;
    private SKPoint _current;
    private SKPoint _subpathStart;

    internal Context2D(Canvas canvas)
    {
        _canvas = canvas;
    }

    private sealed class State
    {
        public object? FillStyle = "#000000";
        public object? StrokeStyle = "#000000";
        public double LineWidth = 1;
        public string LineCap = "butt";
        public string LineJoin = "miter";
        public double MiterLimit = 10;
        public double GlobalAlpha = 1;
        public string Composite = "source-over";
        public string Font = "10px sans-serif";
        public string TextAlign = "start";
        public string TextBaseline = "alphabetic";
        public QColor ShadowColor = QColor.Transparent;
        public double ShadowBlur;
        public double ShadowOffsetX, ShadowOffsetY;
        public double[] LineDash = [];
        public double LineDashOffset;
        public bool Smoothing = true;

        public State Clone() => (State)MemberwiseClone();
    }

    /// <summary>The canvas to draw on: the canvas item's surface, with the pixel ratio applied.</summary>
    private SKCanvas? Target
    {
        get
        {
            if (_target is not null)
                return _target;
            var surface = _canvas.Surface;
            if (surface is null)
                return null;
            _target = surface.Canvas;
            _target.ResetMatrix();
            _target.Scale((float)_canvas.PixelRatio);
            return _target;
        }
    }

    private SKMatrix BaseMatrix => SKMatrix.CreateScale((float)_canvas.PixelRatio, (float)_canvas.PixelRatio);

    internal void OnSurfaceChanged()
    {
        _target = null;
        _saved.Clear();
        _state = new State();
        _path.Reset();
        _hasCurrent = false;
    }

    internal void Flush() => _target?.Flush();

    [Qml] public Canvas Canvas => _canvas;

    // State

    [Qml] public object? FillStyle { get => _state.FillStyle; set => _state.FillStyle = ValueConverter.Unwrap(value); }

    [Qml] public object? StrokeStyle { get => _state.StrokeStyle; set => _state.StrokeStyle = ValueConverter.Unwrap(value); }

    [Qml] public double LineWidth { get => _state.LineWidth; set { if (value > 0 && double.IsFinite(value)) _state.LineWidth = value; } }

    [Qml] public string LineCap { get => _state.LineCap; set => _state.LineCap = value ?? "butt"; }

    [Qml] public string LineJoin { get => _state.LineJoin; set => _state.LineJoin = value ?? "miter"; }

    [Qml] public double MiterLimit { get => _state.MiterLimit; set { if (value > 0) _state.MiterLimit = value; } }

    [Qml] public double GlobalAlpha { get => _state.GlobalAlpha; set { if (value >= 0 && value <= 1) _state.GlobalAlpha = value; } }

    [Qml] public string GlobalCompositeOperation { get => _state.Composite; set => _state.Composite = value ?? "source-over"; }

    [Qml] public string Font { get => _state.Font; set => _state.Font = value ?? "10px sans-serif"; }

    [Qml] public string TextAlign { get => _state.TextAlign; set => _state.TextAlign = value ?? "start"; }

    [Qml] public string TextBaseline { get => _state.TextBaseline; set => _state.TextBaseline = value ?? "alphabetic"; }

    [Qml] public object? ShadowColor { get => _state.ShadowColor; set { if (CssColor.TryParse(value, out var c)) _state.ShadowColor = c; } }

    [Qml] public double ShadowBlur { get => _state.ShadowBlur; set => _state.ShadowBlur = Math.Max(0, value); }

    [Qml] public double ShadowOffsetX { get => _state.ShadowOffsetX; set => _state.ShadowOffsetX = value; }

    [Qml] public double ShadowOffsetY { get => _state.ShadowOffsetY; set => _state.ShadowOffsetY = value; }

    [Qml] public double LineDashOffset { get => _state.LineDashOffset; set => _state.LineDashOffset = value; }

    [Qml] public bool ImageSmoothingEnabled { get => _state.Smoothing; set => _state.Smoothing = value; }

    [Qml]
    public void Save()
    {
        _saved.Push(_state.Clone());
        Target?.Save();
    }

    [Qml]
    public void Restore()
    {
        if (_saved.Count == 0)
            return;
        _state = _saved.Pop();
        Target?.Restore();
    }

    [Qml]
    public void Reset()
    {
        var c = Target;
        if (c is null)
            return;
        while (_saved.Count > 0)
        {
            _saved.Pop();
            c.Restore();
        }
        c.RestoreToCount(1);
        c.ResetMatrix();
        c.Clear(SKColors.Transparent);
        c.Scale((float)_canvas.PixelRatio);
        _state = new State();
        BeginPath();
        _canvas.Update();
    }

    [Qml]
    public void SetLineDash(object? segments)
    {
        var values = ValueConverter.ToList(segments).Select(v => ValueConverter.ToDouble(v, "real")).ToArray();
        if (values.Any(v => v < 0 || !double.IsFinite(v)))
            return;
        _state.LineDash = values.Length % 2 == 1 ? [.. values, .. values] : values;
    }

    [Qml]
    public List<object?> GetLineDash() => _state.LineDash.Select(v => (object?)v).ToList();

    // Transforms

    [Qml] public void Translate(double x, double y) => Target?.Translate((float)x, (float)y);

    [Qml] public void Rotate(double angle) => Target?.RotateRadians((float)angle);

    [Qml] public void Scale(double x, double y) => Target?.Scale((float)x, (float)y);

    [Qml] public void Shear(double sh, double sv) => Target?.Skew((float)sh, (float)sv);

    [Qml]
    public void Transform(double a, double b, double c, double d, double e, double f)
    {
        var m = new SKMatrix((float)a, (float)c, (float)e, (float)b, (float)d, (float)f, 0, 0, 1);
        Target?.Concat(in m);
    }

    [Qml]
    public void SetTransform(double a, double b, double c, double d, double e, double f)
    {
        var target = Target;
        if (target is null)
            return;
        var m = BaseMatrix.PreConcat(new SKMatrix((float)a, (float)c, (float)e, (float)b, (float)d, (float)f, 0, 0, 1));
        target.SetMatrix(in m);
    }

    [Qml]
    public void ResetTransform()
    {
        var target = Target;
        if (target is null)
            return;
        var m = BaseMatrix;
        target.SetMatrix(in m);
    }

    private SKMatrix Ctm => Target?.TotalMatrix ?? SKMatrix.Identity;

    // Paths. The path is kept in device space, so a transform applies to the points added while it is set.

    private SKPoint ToDevice(double x, double y) => Ctm.MapPoint((float)x, (float)y);

    [Qml]
    public void BeginPath()
    {
        _path.Dispose();
        _path = new SKPathBuilder();
        _hasCurrent = false;
    }

    [Qml]
    public void ClosePath()
    {
        if (!_hasCurrent)
            return;
        _path.Close();
        _current = _subpathStart;
    }

    [Qml]
    public void MoveTo(double x, double y)
    {
        var p = ToDevice(x, y);
        _path.MoveTo(p);
        _current = _subpathStart = p;
        _hasCurrent = true;
    }

    [Qml]
    public void LineTo(double x, double y)
    {
        if (!_hasCurrent)
        {
            MoveTo(x, y);
            return;
        }
        var p = ToDevice(x, y);
        _path.LineTo(p);
        _current = p;
    }

    private void EnsureStart(double x, double y)
    {
        if (!_hasCurrent)
            MoveTo(x, y);
    }

    [Qml]
    public void QuadraticCurveTo(double cpx, double cpy, double x, double y)
    {
        EnsureStart(cpx, cpy);
        var p = ToDevice(x, y);
        _path.QuadTo(ToDevice(cpx, cpy), p);
        _current = p;
    }

    [Qml]
    public void BezierCurveTo(double cp1x, double cp1y, double cp2x, double cp2y, double x, double y)
    {
        EnsureStart(cp1x, cp1y);
        var p = ToDevice(x, y);
        _path.CubicTo(ToDevice(cp1x, cp1y), ToDevice(cp2x, cp2y), p);
        _current = p;
    }

    /// <summary>Adds a path made in user space, mapped through the transform, joined to the current point or not.</summary>
    private void AddLocal(SKPath local, bool connect)
    {
        if (local.IsEmpty)
            return;
        var m = Ctm;
        using var mapped = new SKPath();
        local.Transform(in m, mapped);
        if (connect && _hasCurrent)
        {
            _path.AddPath(mapped, SKPathAddMode.Extend);
        }
        else
        {
            _path.AddPath(mapped, SKPathAddMode.Append);
            if (mapped.PointCount > 0)
                _subpathStart = mapped.GetPoint(0);
        }
        _current = mapped.LastPoint;
        _hasCurrent = true;
    }

    [Qml]
    public void Arc(double x, double y, double radius, double startAngle, double endAngle, bool anticlockwise = false)
    {
        if (radius < 0)
            return;
        using var b = new SKPathBuilder();
        var oval = SKRect.Create((float)(x - radius), (float)(y - radius), (float)(radius * 2), (float)(radius * 2));
        var sweep = Sweep(startAngle, endAngle, anticlockwise);
        var start = (float)(startAngle * 180 / Math.PI);
        if (Math.Abs(sweep) >= 360)
        {
            var half = 180f * Math.Sign(sweep);
            b.ArcTo(oval, start, half, true);
            b.ArcTo(oval, start + half, half, false);
        }
        else
        {
            b.ArcTo(oval, start, (float)sweep, true);
        }
        using var local = b.Detach();
        AddLocal(local, connect: true);
    }

    /// <summary>The sweep of an arc in degrees, as HTML canvas computes it from the angles and the direction.</summary>
    private static double Sweep(double start, double end, bool anticlockwise)
    {
        const double full = 2 * Math.PI;
        double sweep;
        if (!anticlockwise)
        {
            sweep = end - start;
            if (sweep >= full)
                sweep = full;
            else
                sweep = ((sweep % full) + full) % full;
        }
        else
        {
            sweep = start - end;
            if (sweep >= full)
                sweep = full;
            else
                sweep = ((sweep % full) + full) % full;
            sweep = -sweep;
        }
        return sweep * 180 / Math.PI;
    }

    [Qml]
    public void ArcTo(double x1, double y1, double x2, double y2, double radius)
    {
        if (!_hasCurrent)
        {
            MoveTo(x1, y1);
            return;
        }
        if (!Ctm.TryInvert(out var inverse))
            return;
        var start = inverse.MapPoint(_current);
        using var b = new SKPathBuilder();
        b.MoveTo(start);
        b.ArcTo((float)x1, (float)y1, (float)x2, (float)y2, (float)Math.Max(0, radius));
        using var local = b.Detach();
        AddLocal(local, connect: true);
    }

    [Qml]
    public void Rect(double x, double y, double w, double h)
    {
        using var b = new SKPathBuilder();
        b.AddRect(SKRect.Create((float)x, (float)y, (float)w, (float)h), SKPathDirection.Clockwise);
        using var local = b.Detach();
        AddLocal(local, connect: false);
        _current = _subpathStart = ToDevice(x, y);
    }

    [Qml]
    public void RoundedRect(double x, double y, double w, double h, double xRadius, double yRadius)
    {
        using var b = new SKPathBuilder();
        b.AddRoundRect(SKRect.Create((float)x, (float)y, (float)w, (float)h), (float)xRadius, (float)yRadius, SKPathDirection.Clockwise);
        using var local = b.Detach();
        AddLocal(local, connect: false);
    }

    [Qml]
    public void RoundRect(double x, double y, double w, double h, double radius = 0) => RoundedRect(x, y, w, h, radius, radius);

    /// <summary>Qt's <c>ellipse(x, y, w, h)</c>, or HTML's <c>ellipse(x, y, rx, ry, rotation, start, end, anticlockwise)</c>.</summary>
    [Qml]
    public void Ellipse(double x, double y, double a, double b, double rotation = double.NaN, double startAngle = 0, double endAngle = 2 * Math.PI, bool anticlockwise = false)
    {
        using var builder = new SKPathBuilder();
        if (double.IsNaN(rotation))
        {
            builder.AddOval(SKRect.Create((float)x, (float)y, (float)a, (float)b), SKPathDirection.Clockwise);
            using var oval = builder.Detach();
            AddLocal(oval, connect: false);
            return;
        }
        var rect = SKRect.Create((float)(x - a), (float)(y - b), (float)(a * 2), (float)(b * 2));
        var sweep = Sweep(startAngle, endAngle, anticlockwise);
        var start = (float)(startAngle * 180 / Math.PI);
        if (Math.Abs(sweep) >= 360)
        {
            builder.ArcTo(rect, start, 180, true);
            builder.ArcTo(rect, start + 180, 180, false);
        }
        else
        {
            builder.ArcTo(rect, start, (float)sweep, true);
        }
        using var local = builder.Detach();
        var turn = SKMatrix.CreateRotationDegrees((float)(rotation * 180 / Math.PI), (float)x, (float)y);
        local.Transform(in turn);
        AddLocal(local, connect: true);
    }

    [Qml]
    public void Text(string text, double x, double y)
    {
        var (font, _) = ResolveFont();
        using var _f = font;
        var origin = AlignedOrigin(text, x, y, font);
        using var local = font.GetTextPath(text, origin);
        AddLocal(local, connect: false);
    }

    /// <summary>The current path in user space, for drawing with the current transform.</summary>
    private SKPath? LocalPath()
    {
        if (!Ctm.TryInvert(out var inverse))
            return null;
        using var device = _path.Snapshot();
        var local = new SKPath();
        device.Transform(in inverse, local);
        return local;
    }

    [Qml]
    public void Fill(string fillRule = "nonzero")
    {
        var target = Target;
        using var path = LocalPath();
        if (target is null || path is null)
            return;
        path.FillType = fillRule == "evenodd" ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        using var paint = MakePaint(_state.FillStyle, SKPaintStyle.Fill);
        target.DrawPath(path, paint);
        Changed();
    }

    [Qml]
    public void Stroke()
    {
        var target = Target;
        using var path = LocalPath();
        if (target is null || path is null)
            return;
        using var paint = MakePaint(_state.StrokeStyle, SKPaintStyle.Stroke);
        target.DrawPath(path, paint);
        Changed();
    }

    [Qml]
    public void Clip(string fillRule = "nonzero")
    {
        var target = Target;
        using var path = LocalPath();
        if (target is null || path is null)
            return;
        path.FillType = fillRule == "evenodd" ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        target.ClipPath(path, SKClipOperation.Intersect, true);
    }

    [Qml]
    public bool IsPointInPath(double x, double y)
    {
        using var device = _path.Snapshot();
        var p = ToDevice(x, y);
        return device.Contains(p.X, p.Y);
    }

    [Qml]
    public void FillRect(double x, double y, double w, double h)
    {
        var target = Target;
        if (target is null)
            return;
        using var paint = MakePaint(_state.FillStyle, SKPaintStyle.Fill);
        target.DrawRect(SKRect.Create((float)x, (float)y, (float)w, (float)h), paint);
        Changed();
    }

    [Qml]
    public void StrokeRect(double x, double y, double w, double h)
    {
        var target = Target;
        if (target is null)
            return;
        using var paint = MakePaint(_state.StrokeStyle, SKPaintStyle.Stroke);
        target.DrawRect(SKRect.Create((float)x, (float)y, (float)w, (float)h), paint);
        Changed();
    }

    [Qml]
    public void ClearRect(double x, double y, double w, double h)
    {
        var target = Target;
        if (target is null)
            return;
        using var paint = new SKPaint { BlendMode = SKBlendMode.Clear };
        target.DrawRect(SKRect.Create((float)x, (float)y, (float)w, (float)h), paint);
        Changed();
    }

    private void Changed() => _canvas.Update();

    // Gradients and patterns

    [Qml]
    public CanvasGradient CreateLinearGradient(double x0, double y0, double x1, double y1) =>
        new(CanvasGradient.Kind.Linear, [(float)x0, (float)y0, (float)x1, (float)y1]) { Engine = Engine };

    [Qml]
    public CanvasGradient CreateRadialGradient(double x0, double y0, double r0, double x1, double y1, double r1) =>
        new(CanvasGradient.Kind.Radial, [(float)x0, (float)y0, (float)r0, (float)x1, (float)y1, (float)r1]) { Engine = Engine };

    [Qml]
    public CanvasGradient CreateConicalGradient(double x, double y, double angle) =>
        new(CanvasGradient.Kind.Conical, [(float)x, (float)y, (float)angle]) { Engine = Engine };

    [Qml]
    public object? CreatePattern(object? image, string repetition = "repeat") => CssColor.TryParse(image, out var c) ? c : null;

    // Paint

    private SKPaint MakePaint(object? style, SKPaintStyle kind)
    {
        var paint = new SKPaint { IsAntialias = true, Style = kind };
        var alpha = (byte)Math.Clamp(Math.Round(_state.GlobalAlpha * 255), 0, 255);
        if (style is CanvasGradient gradient && gradient.CreateShader() is { } shader)
        {
            paint.Shader = shader;
            paint.Color = new SKColor(255, 255, 255, alpha);
        }
        else
        {
            var color = CssColor.TryParse(style, out var c) ? c : QColor.Black;
            paint.Color = color.WithAlpha(color.A * _state.GlobalAlpha).ToSk();
        }
        if (kind == SKPaintStyle.Stroke)
        {
            paint.StrokeWidth = (float)_state.LineWidth;
            paint.StrokeMiter = (float)_state.MiterLimit;
            paint.StrokeCap = _state.LineCap switch
            {
                "round" => SKStrokeCap.Round,
                "square" => SKStrokeCap.Square,
                _ => SKStrokeCap.Butt,
            };
            paint.StrokeJoin = _state.LineJoin switch
            {
                "round" => SKStrokeJoin.Round,
                "bevel" => SKStrokeJoin.Bevel,
                _ => SKStrokeJoin.Miter,
            };
            if (_state.LineDash.Length >= 2 && _state.LineDash.Sum() > 0)
                paint.PathEffect = SKPathEffect.CreateDash(_state.LineDash.Select(v => (float)v).ToArray(), (float)_state.LineDashOffset);
        }
        paint.BlendMode = Blend(_state.Composite);
        if (_state.ShadowColor.A > 0 && (_state.ShadowBlur > 0 || _state.ShadowOffsetX != 0 || _state.ShadowOffsetY != 0))
        {
            var sigma = (float)(_state.ShadowBlur / 2);
            paint.ImageFilter = SKImageFilter.CreateDropShadow((float)_state.ShadowOffsetX, (float)_state.ShadowOffsetY, sigma, sigma, _state.ShadowColor.ToSk());
        }
        return paint;
    }

    private static SKBlendMode Blend(string op) => op switch
    {
        "source-in" => SKBlendMode.SrcIn,
        "source-out" => SKBlendMode.SrcOut,
        "source-atop" => SKBlendMode.SrcATop,
        "destination-over" => SKBlendMode.DstOver,
        "destination-in" => SKBlendMode.DstIn,
        "destination-out" => SKBlendMode.DstOut,
        "destination-atop" => SKBlendMode.DstATop,
        "lighter" or "plus-lighter" => SKBlendMode.Plus,
        "copy" => SKBlendMode.Src,
        "xor" => SKBlendMode.Xor,
        "multiply" => SKBlendMode.Multiply,
        "screen" => SKBlendMode.Screen,
        "overlay" => SKBlendMode.Overlay,
        "darken" => SKBlendMode.Darken,
        "lighten" => SKBlendMode.Lighten,
        "color-dodge" => SKBlendMode.ColorDodge,
        "color-burn" => SKBlendMode.ColorBurn,
        "hard-light" => SKBlendMode.HardLight,
        "soft-light" => SKBlendMode.SoftLight,
        "difference" => SKBlendMode.Difference,
        "exclusion" => SKBlendMode.Exclusion,
        "hue" => SKBlendMode.Hue,
        "saturation" => SKBlendMode.Saturation,
        "color" => SKBlendMode.Color,
        "luminosity" => SKBlendMode.Luminosity,
        _ => SKBlendMode.SrcOver,
    };

    // Text

    /// <summary>The font of the <c>font</c> string: style, weight, size, then families, as in CSS.</summary>
    private (SKFont Font, double Size) ResolveFont()
    {
        var weight = 400;
        var italic = false;
        var size = 10.0;
        var family = "sans-serif";
        var tokens = _state.Font.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i].ToLowerInvariant();
            if (t is "italic" or "oblique")
            {
                italic = true;
                continue;
            }
            if (t is "bold" or "bolder")
            {
                weight = 700;
                continue;
            }
            if (t is "lighter")
            {
                weight = 300;
                continue;
            }
            if (t is "normal" or "small-caps")
                continue;
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w is >= 100 and <= 900)
            {
                weight = w;
                continue;
            }
            var sizeToken = t.Split('/')[0];
            double factor = 0;
            if (sizeToken.EndsWith("px", StringComparison.Ordinal))
                factor = 1;
            else if (sizeToken.EndsWith("pt", StringComparison.Ordinal))
                factor = 96.0 / 72;
            else if (sizeToken.EndsWith("em", StringComparison.Ordinal))
                factor = 16;
            if (factor > 0 && double.TryParse(sizeToken[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            {
                size = s * factor;
                if (i + 1 < tokens.Length)
                    family = string.Join(' ', tokens[(i + 1)..]).Trim();
                break;
            }
        }
        var typeface = FontCache.Get(family, weight, italic);
        return (new SKFont(typeface, (float)size) { Subpixel = true, Edging = SKFontEdging.Antialias }, size);
    }

    private SKPoint AlignedOrigin(string text, double x, double y, SKFont font)
    {
        var width = font.MeasureText(text, out _);
        var dx = _state.TextAlign switch
        {
            "center" => -width / 2,
            "right" or "end" => -width,
            _ => 0f,
        };
        font.GetFontMetrics(out var m);
        var dy = _state.TextBaseline switch
        {
            "top" or "hanging" => -m.Ascent,
            "middle" => -(m.Ascent + m.Descent) / 2,
            "bottom" or "ideographic" => -m.Descent,
            _ => 0f,
        };
        return new SKPoint((float)x + dx, (float)y + dy);
    }

    private void DrawText(string text, double x, double y, double maxWidth, object? style, SKPaintStyle kind)
    {
        var target = Target;
        if (target is null || string.IsNullOrEmpty(text))
            return;
        var (font, _) = ResolveFont();
        using var _f = font;
        var origin = AlignedOrigin(text, x, y, font);
        using var paint = MakePaint(style, kind);
        var width = font.MeasureText(text, out _);
        target.Save();
        if (!double.IsNaN(maxWidth) && maxWidth > 0 && width > maxWidth)
        {
            target.Translate((float)x, 0);
            target.Scale((float)(maxWidth / width), 1);
            target.Translate(-(float)x, 0);
        }
        target.DrawText(text, origin.X, origin.Y, SKTextAlign.Left, font, paint);
        target.Restore();
        Changed();
    }

    [Qml]
    public void FillText(string text, double x, double y, double maxWidth = double.NaN) => DrawText(text, x, y, maxWidth, _state.FillStyle, SKPaintStyle.Fill);

    [Qml]
    public void StrokeText(string text, double x, double y, double maxWidth = double.NaN) => DrawText(text, x, y, maxWidth, _state.StrokeStyle, SKPaintStyle.Stroke);

    [Qml]
    public TextMetrics MeasureText(string text)
    {
        var (font, _) = ResolveFont();
        using var _f = font;
        var width = font.MeasureText(text ?? "", out var bounds);
        font.GetFontMetrics(out var m);
        return new TextMetrics
        {
            Engine = Engine,
            Width = width,
            ActualBoundingBoxLeft = -bounds.Left,
            ActualBoundingBoxRight = bounds.Right,
            ActualBoundingBoxAscent = -bounds.Top,
            ActualBoundingBoxDescent = bounds.Bottom,
            FontBoundingBoxAscent = -m.Ascent,
            FontBoundingBoxDescent = m.Descent,
        };
    }

    // Images

    /// <summary>The image to draw and its pixels per canvas unit: a canvas's surface is in device pixels.</summary>
    private SKImage? ImageOf(object? image, out bool owned, out float scale)
    {
        owned = false;
        scale = 1;
        switch (ValueConverter.Unwrap(image))
        {
            case string url:
                return _canvas.ImageFor(url);
            case Canvas other:
                owned = true;
                scale = (float)other.PixelRatio;
                return other.Snapshot();
            case Image item:
                return item.CurrentImage;
            default:
                return null;
        }
    }

    /// <summary>drawImage(image, dx, dy), (image, dx, dy, dw, dh) or (image, sx, sy, sw, sh, dx, dy, dw, dh).</summary>
    [Qml]
    public void DrawImage(object? image, params object?[] args)
    {
        var target = Target;
        var img = ImageOf(image, out var owned, out var scale);
        if (target is null || img is null)
            return;
        try
        {
            var n = args.Select(a => (float)ValueConverter.ToDouble(a, "real")).ToArray();
            var src = SKRect.Create(0, 0, img.Width, img.Height);
            SKRect dst;
            switch (n.Length)
            {
                case >= 8:
                    src = SKRect.Create(n[0] * scale, n[1] * scale, n[2] * scale, n[3] * scale);
                    dst = SKRect.Create(n[4], n[5], n[6], n[7]);
                    break;
                case >= 4:
                    dst = SKRect.Create(n[0], n[1], n[2], n[3]);
                    break;
                case >= 2:
                    dst = SKRect.Create(n[0], n[1], img.Width / scale, img.Height / scale);
                    break;
                default:
                    return;
            }
            using var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(_state.GlobalAlpha * 255)), BlendMode = Blend(_state.Composite) };
            var sampling = _state.Smoothing ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear) : new SKSamplingOptions(SKFilterMode.Nearest);
            target.DrawImage(img, src, dst, sampling, paint);
            Changed();
        }
        finally
        {
            if (owned)
                img.Dispose();
        }
    }

    [Qml]
    public object? GetImageData(double x, double y, double w, double h) => null;

    [Qml]
    public object? CreateImageData(params object?[] args) => null;

    [Qml]
    public void PutImageData(params object?[] args)
    {
    }

    protected override void OnDestroy()
    {
        _path.Dispose();
        base.OnDestroy();
    }
}
