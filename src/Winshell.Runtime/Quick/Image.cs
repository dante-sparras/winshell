using System.Collections.Concurrent;
using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>Finds images for <c>image://name/...</c> sources, such as icons.</summary>
public interface IImageProvider
{
    /// <summary>Returns the image for the id, at about the requested size in pixels, or null.</summary>
    SKImage? Load(string id, int requestedWidth, int requestedHeight);
}

/// <summary>Loads image sources: files, <c>file://</c> and <c>data:</c> URLs, http, SVG and image providers.</summary>
public static class ImageLoader
{
    private static readonly ConcurrentDictionary<string, IImageProvider> Providers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<(string, int, int), SKImage?> Cache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static void AddProvider(string name, IImageProvider provider) => Providers[name] = provider;

    public static bool IsRemote(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The local path a source names, or null when it names none.</summary>
    public static string? LocalPath(string source, string? baseDirectory)
    {
        if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var path = Uri.UnescapeDataString(source[7..]);
            if (path.Length >= 3 && path[0] == '/' && path[2] == ':')
                path = path[1..];
            return path;
        }
        if (source.Contains("://", StringComparison.Ordinal) || source.StartsWith("data:", StringComparison.Ordinal))
            return null;
        if (source.StartsWith("qrc:", StringComparison.Ordinal))
            return null;
        return Path.IsPathRooted(source) ? source : Path.Combine(baseDirectory ?? Environment.CurrentDirectory, source);
    }

    public static void ClearCache() => Cache.Clear();

    /// <summary>Loads an image. Remote sources are not loaded here; see <see cref="LoadAsync"/>.</summary>
    public static SKImage? Load(string source, string? baseDirectory, int width, int height, bool cache = true)
    {
        if (string.IsNullOrEmpty(source))
            return null;
        var key = (source, width, height);
        if (cache && Cache.TryGetValue(key, out var cached))
            return cached;
        var image = LoadUncached(source, baseDirectory, width, height);
        if (cache)
            Cache[key] = image;
        return image;
    }

    public static async Task<SKImage?> LoadAsync(string source, string? baseDirectory, int width, int height)
    {
        if (!IsRemote(source))
            return await Task.Run(() => Load(source, baseDirectory, width, height)).ConfigureAwait(false);
        var key = (source, width, height);
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        try
        {
            var bytes = await Http.GetByteArrayAsync(new Uri(source)).ConfigureAwait(false);
            var image = Decode(bytes, source, width, height);
            Cache[key] = image;
            return image;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static SKImage? LoadUncached(string source, string? baseDirectory, int width, int height)
    {
        if (source.StartsWith("image://", StringComparison.OrdinalIgnoreCase))
        {
            var rest = source[8..];
            var slash = rest.IndexOf('/');
            var name = slash < 0 ? rest : rest[..slash];
            var id = slash < 0 ? "" : Uri.UnescapeDataString(rest[(slash + 1)..]);
            return Providers.TryGetValue(name, out var provider) ? provider.Load(id, width, height) : null;
        }
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',');
            if (comma < 0)
                return null;
            var meta = source[5..comma];
            var data = source[(comma + 1)..];
            var bytes = meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(data)
                : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
            return Decode(bytes, meta.Contains("svg", StringComparison.OrdinalIgnoreCase) ? "x.svg" : "x", width, height);
        }
        var path = LocalPath(source, baseDirectory);
        if (path is null || !File.Exists(path))
            return null;
        try
        {
            return Decode(File.ReadAllBytes(path), path, width, height);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static SKImage? Decode(byte[] bytes, string name, int width, int height)
    {
        if (name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || LooksLikeSvg(bytes))
            return RenderSvg(bytes, width, height);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
            return null;
        var info = codec.Info;
        if (width > 0 && height > 0 && (width < info.Width || height < info.Height))
        {
            // Decode at a size near the one asked for, which saves memory for big pictures.
            var scale = Math.Max((float)width / info.Width, (float)height / info.Height);
            var dims = codec.GetScaledDimensions(scale);
            info = info.WithSize(dims.Width, dims.Height);
        }
        using var bitmap = new SKBitmap(info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            using var decoded = SKBitmap.Decode(bytes);
            return decoded is null ? null : SKImage.FromBitmap(decoded);
        }
        return SKImage.FromBitmap(bitmap);
    }

    private static bool LooksLikeSvg(byte[] bytes)
    {
        var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512));
        return head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static SKImage? RenderSvg(byte[] bytes, int width, int height)
    {
        using var svg = new Svg.Skia.SKSvg();
        using var stream = new MemoryStream(bytes);
        var picture = svg.Load(stream);
        if (picture is null)
            return null;
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;
        var w = width > 0 ? width : (int)Math.Ceiling(bounds.Width);
        var h = height > 0 ? height : (int)Math.Ceiling(bounds.Height);
        if (width > 0 && height <= 0)
            h = (int)Math.Ceiling(bounds.Height * width / bounds.Width);
        if (height > 0 && width <= 0)
            w = (int)Math.Ceiling(bounds.Width * height / bounds.Height);
        using var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, w), Math.Max(1, h), SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(w / bounds.Width, h / bounds.Height);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        return surface.Snapshot();
    }
}

/// <summary>QtQuick's <c>Image</c>.</summary>
[QmlSignal("statusChanged")]
public class Image : Item
{
    private string _source = "";
    private int _fillMode;
    private QSize _sourceSize;
    private bool _sourceSizeSet;
    private int _status;
    private bool _asynchronous;
    private bool _cache = true;
    private bool _mirror;
    private bool _mipmap;
    private int _horizontalAlignment = QtEnums.AlignHCenter;
    private int _verticalAlignment = QtEnums.AlignVCenter;
    private SKImage? _image;
    private double _progress;
    private int _loadGeneration;

    [QmlEnum]
    public enum FillModeValue
    {
        Stretch = 0,
        PreserveAspectFit = 1,
        PreserveAspectCrop = 2,
        Tile = 3,
        TileVertically = 4,
        TileHorizontally = 5,
        Pad = 6,
    }

    [QmlEnum]
    public enum StatusValue
    {
        Null = 0,
        Ready = 1,
        Loading = 2,
        Error = 3,
    }

    [Qml]
    public string Source
    {
        get { Track(); return _source; }
        set
        {
            if (SetField(ref _source, value ?? ""))
                Reload();
        }
    }

    [Qml]
    public int FillMode
    {
        get { Track(); return _fillMode; }
        set
        {
            if (SetField(ref _fillMode, value))
                Update();
        }
    }

    [Qml]
    public QSize SourceSize
    {
        get { Track(); return _sourceSizeSet ? _sourceSize : _image is null ? new QSize(0, 0) : new QSize(_image.Width, _image.Height); }
        set
        {
            _sourceSizeSet = value.Width > 0 || value.Height > 0;
            if (SetField(ref _sourceSize, value))
                Reload();
        }
    }

    [Qml]
    public int Status
    {
        get { Track(); return _status; }
    }

    [Qml]
    public double Progress
    {
        get { Track(); return _progress; }
    }

    [Qml]
    public bool Asynchronous
    {
        get { Track(); return _asynchronous; }
        set => SetField(ref _asynchronous, value);
    }

    [Qml]
    public bool Cache
    {
        get { Track(); return _cache; }
        set => SetField(ref _cache, value);
    }

    [Qml]
    public bool Mirror
    {
        get { Track(); return _mirror; }
        set
        {
            if (SetField(ref _mirror, value))
                Update();
        }
    }

    [Qml]
    public bool MirrorVertically { get; set; }

    [Qml]
    public bool Mipmap
    {
        get { Track(); return _mipmap; }
        set => SetField(ref _mipmap, value);
    }

    [Qml]
    public bool AutoTransform { get; set; } = true;

    [Qml]
    public int HorizontalAlignment
    {
        get { Track(); return _horizontalAlignment; }
        set
        {
            if (SetField(ref _horizontalAlignment, value))
                Update();
        }
    }

    [Qml]
    public int VerticalAlignment
    {
        get { Track(); return _verticalAlignment; }
        set
        {
            if (SetField(ref _verticalAlignment, value))
                Update();
        }
    }

    [Qml]
    public double PaintedWidth
    {
        get { Track(); return PaintRect().Width; }
    }

    [Qml]
    public double PaintedHeight
    {
        get { Track(); return PaintRect().Height; }
    }

    [Qml]
    public object? SourceClipRect { get; set; }

    public SKImage? CurrentImage => _image;

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        Reload();
    }

    protected override void OnHostChanged()
    {
        // The pixel ratio can change with the window, and an icon wants to be sharp at it.
        if (IsComplete && _image is not null && _sourceSizeSet)
            Reload();
    }

    protected virtual void Reload()
    {
        if (!IsComplete)
            return;
        var generation = ++_loadGeneration;
        if (_source.Length == 0)
        {
            SetImage(null, 0);
            return;
        }
        var dpr = Host?.DevicePixelRatio ?? 1;
        var w = _sourceSizeSet ? (int)Math.Ceiling(_sourceSize.Width * dpr) : 0;
        var h = _sourceSizeSet ? (int)Math.Ceiling(_sourceSize.Height * dpr) : 0;
        var baseDir = Engine?.ShellDir;
        if (CreationContext?.Imports is { } imports)
            baseDir = imports.Directory;
        if (_asynchronous || ImageLoader.IsRemote(_source))
        {
            SetStatus((int)StatusValue.Loading);
            var source = _source;
            var loop = Engine?.Loop;
            _ = ImageLoader.LoadAsync(source, baseDir, w, h).ContinueWith(t =>
            {
                var image = t.IsCompletedSuccessfully ? t.Result : null;
                loop?.Post(() =>
                {
                    if (generation == _loadGeneration && !IsDestroyed)
                        SetImage(image, image is null ? (int)StatusValue.Error : (int)StatusValue.Ready);
                });
            }, TaskScheduler.Default);
            return;
        }
        var loaded = ImageLoader.Load(_source, baseDir, w, h, _cache);
        SetImage(loaded, loaded is null ? (int)StatusValue.Error : (int)StatusValue.Ready);
        if (loaded is null)
            Engine?.Log.Warn("scene", $"QML {TypeName}: Cannot open: {_source}");
    }

    private void SetImage(SKImage? image, int status)
    {
        _image = image;
        var dpr = Host?.DevicePixelRatio ?? 1;
        if (image is not null)
        {
            // The implicit size is the image's size in logical pixels, or sourceSize when set.
            var w = _sourceSizeSet && _sourceSize.Width > 0 ? _sourceSize.Width : image.Width / (_sourceSizeSet ? dpr : 1);
            var h = _sourceSizeSet && _sourceSize.Height > 0 ? _sourceSize.Height : image.Height / (_sourceSizeSet ? dpr : 1);
            if (_sourceSizeSet && _sourceSize.Width > 0 && _sourceSize.Height <= 0)
                h = w * image.Height / image.Width;
            if (_sourceSizeSet && _sourceSize.Height > 0 && _sourceSize.Width <= 0)
                w = h * image.Width / image.Height;
            ImplicitWidth = w;
            ImplicitHeight = h;
        }
        else
        {
            ImplicitWidth = 0;
            ImplicitHeight = 0;
        }
        _progress = image is null ? 0 : 1;
        NotifyChanged("progress");
        NotifyChanged("sourceSize");
        NotifyChanged("paintedWidth");
        NotifyChanged("paintedHeight");
        SetStatus(status);
        Update();
    }

    private void SetStatus(int status)
    {
        if (_status == status)
            return;
        _status = status;
        NotifyChanged("status");
    }

    /// <summary>Where the image is drawn inside the item, for the fill mode.</summary>
    protected SKRect PaintRect()
    {
        if (_image is null)
            return SKRect.Empty;
        var w = (float)Width;
        var h = (float)Height;
        var iw = (float)ImplicitWidth;
        var ih = (float)ImplicitHeight;
        if (iw <= 0 || ih <= 0)
            return SKRect.Empty;
        float pw = w, ph = h;
        switch (_fillMode)
        {
            case (int)FillModeValue.PreserveAspectFit:
                {
                    var s = Math.Min(w / iw, h / ih);
                    pw = iw * s;
                    ph = ih * s;
                    break;
                }
            case (int)FillModeValue.PreserveAspectCrop:
                {
                    var s = Math.Max(w / iw, h / ih);
                    pw = iw * s;
                    ph = ih * s;
                    break;
                }
            case (int)FillModeValue.Pad:
                pw = iw;
                ph = ih;
                break;
        }
        var x = (_horizontalAlignment & QtEnums.AlignLeft) != 0 ? 0 : (_horizontalAlignment & QtEnums.AlignRight) != 0 ? w - pw : (w - pw) / 2;
        var y = (_verticalAlignment & QtEnums.AlignTop) != 0 ? 0 : (_verticalAlignment & QtEnums.AlignBottom) != 0 ? h - ph : (h - ph) / 2;
        if (_fillMode == (int)FillModeValue.Stretch)
        {
            x = 0;
            y = 0;
        }
        return SKRect.Create(x, y, pw, ph);
    }

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        var image = _image;
        if (image is null || Width <= 0 || Height <= 0)
            return;
        using var paint = new SKPaint { IsAntialias = true };
        var sampling = Smooth ? new SKSamplingOptions(SKFilterMode.Linear, _mipmap ? SKMipmapMode.Linear : SKMipmapMode.None) : new SKSamplingOptions(SKFilterMode.Nearest);
        canvas.Save();
        if (_mirror)
        {
            canvas.Translate((float)Width, 0);
            canvas.Scale(-1, 1);
        }
        if (_fillMode is (int)FillModeValue.Tile or (int)FillModeValue.TileHorizontally or (int)FillModeValue.TileVertically)
        {
            var sx = (float)(ImplicitWidth / image.Width);
            var sy = (float)(ImplicitHeight / image.Height);
            using var shader = image.ToShader(
                _fillMode == (int)FillModeValue.TileVertically ? SKShaderTileMode.Clamp : SKShaderTileMode.Repeat,
                _fillMode == (int)FillModeValue.TileHorizontally ? SKShaderTileMode.Clamp : SKShaderTileMode.Repeat,
                sampling,
                SKMatrix.CreateScale(sx, sy));
            paint.Shader = shader;
            canvas.DrawRect(0, 0, (float)Width, (float)Height, paint);
        }
        else
        {
            var rect = PaintRect();
            if (_fillMode == (int)FillModeValue.PreserveAspectCrop)
                canvas.ClipRect(SKRect.Create(0, 0, (float)Width, (float)Height));
            canvas.DrawImage(image, rect, sampling, paint);
        }
        canvas.Restore();
    }
}

/// <summary>QtQuick's <c>AnimatedImage</c>. Shows the first frame.</summary>
public sealed class AnimatedImage : Image
{
    [Qml]
    public bool Playing { get; set; } = true;

    [Qml]
    public bool Paused { get; set; }

    [Qml]
    public int CurrentFrame { get; set; }

    [Qml]
    public int FrameCount => 1;

    [Qml]
    public double Speed { get; set; } = 1;
}
