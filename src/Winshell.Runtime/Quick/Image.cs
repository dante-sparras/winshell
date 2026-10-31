using System.Collections.Concurrent;
using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>Finds images for <c>image://name/...</c> sources, such as icons.</summary>
public interface IImageProvider
{
    /// <summary>Returns the image for the id, at about the requested size in pixels, or null.</summary>
    SKImage? Load(string id, int requestedWidth, int requestedHeight);
}

/// <summary>Loads image sources: files, <c>file://</c> and <c>data:</c> URLs, https, SVG and image providers.</summary>
public static class ImageLoader
{
    /// <summary>How many decoded images the cache keeps. Callers hold their own copies, so a drop can be disposed.</summary>
    private const int CacheLimit = 32;

    private readonly record struct CacheKey(string Source, int Width, int Height, long Stamp);

    private static readonly ConcurrentDictionary<string, IImageProvider> Providers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static readonly Dictionary<CacheKey, SKImage> Images = [];
    private static readonly Dictionary<CacheKey, LinkedListNode<CacheKey>> Nodes = [];
    private static readonly LinkedList<CacheKey> Order = [];

    /// <summary>The most bytes an <c>https://</c> image can be.</summary>
    internal const int MaxRemoteBytes = 8 * 1024 * 1024;

    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Fetches <c>https://</c> images. A test swaps it for one with a fake handler.</summary>
    internal static HttpClient Http { get; set; } = new() { Timeout = Timeout.InfiniteTimeSpan };

    static ImageLoader() => Theme.Changed += DropProviderEntries;

    /// <summary>How many images the cache currently owns.</summary>
    internal static int CachedCount
    {
        get { lock (Gate) return Images.Count; }
    }

    /// <summary>The most pixels a decoded image has. A bigger image is decoded smaller, keeping its aspect ratio.</summary>
    internal const long MaxPixels = 64L * 1024 * 1024;

    public static void AddProvider(string name, IImageProvider provider) => Providers[name] = provider;

    public static bool IsRemote(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The local path a source names, or null when it names none.</summary>
    public static string? LocalPath(string source, string? baseDirectory)
    {
        if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.LocalPath : null;
        if (source.Contains("://", StringComparison.Ordinal) || source.StartsWith("data:", StringComparison.Ordinal))
            return null;
        if (source.StartsWith("qrc:", StringComparison.Ordinal))
            return null;
        return Path.IsPathRooted(source) ? source : Path.Combine(baseDirectory ?? Environment.CurrentDirectory, source);
    }

    public static void ClearCache()
    {
        lock (Gate)
        {
            foreach (var key in Images.Keys.ToArray())
                Drop(key);
        }
    }

    /// <summary>Loads an image. Remote sources are not loaded here; see <see cref="LoadAsync"/>.</summary>
    public static SKImage? Load(string source, string? baseDirectory, int width, int height, bool cache = true)
    {
        if (string.IsNullOrEmpty(source))
            return null;
        if (cache && TryRemembered(source, baseDirectory, width, height, out var remembered))
            return remembered;
        var image = LoadUncached(source, baseDirectory, width, height);
        // A miss, a provider image and a data URL stay out of the cache. The caller owns what comes back.
        if (!cache || image is null || !TryKey(source, baseDirectory, width, height, out var key))
            return image;
        var copy = Copy(image);
        if (copy is null)
            return image;
        Remember(key, image);
        return copy;
    }

    public static async Task<SKImage?> LoadAsync(string source, string? baseDirectory, int width, int height)
    {
        if (!IsRemote(source))
            return await Task.Run(() => Load(source, baseDirectory, width, height)).ConfigureAwait(false);
        // Cleartext is never fetched, as the updater never does. This comes first, so the cache can't answer for it.
        if (!source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;
        if (TryRemembered(source, baseDirectory, width, height, out var remembered))
            return remembered;
        try
        {
            var bytes = await ReadCappedAsync(new Uri(source)).ConfigureAwait(false);
            if (bytes is null)
                return null;
            var image = Decode(bytes, source, width, height);
            if (image is null || !TryKey(source, baseDirectory, width, height, out var key))
                return image;
            var copy = Copy(image);
            if (copy is null)
                return image;
            Remember(key, image);
            return copy;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The body at the url, or null when it is longer than <see cref="MaxRemoteBytes"/> or takes too long.</summary>
    private static async Task<byte[]?> ReadCappedAsync(Uri uri)
    {
        using var timeout = new CancellationTokenSource(RemoteTimeout);
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxRemoteBytes)
            return null;
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > MaxRemoteBytes)
                return null;
            bytes.Write(buffer, 0, read);
        }
        return bytes.ToArray();
    }

    /// <summary>
    /// A file key includes the last-write time, so a rewritten picture misses. <c>image://</c> and <c>data:</c> are
    /// never keys: providers cache themselves, and a data URL is already the bytes.
    /// </summary>
    private static bool TryKey(string source, string? baseDirectory, int width, int height, out CacheKey key)
    {
        key = default;
        if (source.StartsWith("image://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;
        if (IsRemote(source))
        {
            key = new CacheKey(source, width, height, 0);
            return true;
        }
        var path = LocalPath(source, baseDirectory);
        if (path is null || !File.Exists(path))
            return false;
        try
        {
            key = new CacheKey(path, width, height, File.GetLastWriteTimeUtc(path).Ticks);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryRemembered(string source, string? baseDirectory, int width, int height, out SKImage? copy)
    {
        copy = null;
        if (!TryKey(source, baseDirectory, width, height, out var key))
            return false;
        lock (Gate)
        {
            if (!Images.TryGetValue(key, out var image))
                return false;
            Touch(key);
            copy = Copy(image);
            return copy is not null;
        }
    }

    private static void Remember(CacheKey key, SKImage image)
    {
        lock (Gate)
        {
            foreach (var stale in Images.Keys.Where(item => item.Source == key.Source && item.Width == key.Width && item.Height == key.Height && item.Stamp != key.Stamp).ToArray())
                Drop(stale);
            if (Images.TryGetValue(key, out var previous))
            {
                previous.Dispose();
                Images[key] = image;
                Touch(key);
                return;
            }
            while (Images.Count >= CacheLimit && Order.Last is { } oldest)
                Drop(oldest.Value);
            Images[key] = image;
            Nodes[key] = Order.AddFirst(key);
        }
    }

    private static void Touch(CacheKey key)
    {
        var node = Nodes[key];
        Order.Remove(node);
        Order.AddFirst(node);
    }

    private static void Drop(CacheKey key)
    {
        if (Nodes.Remove(key, out var node))
            Order.Remove(node);
        if (Images.Remove(key, out var image))
            image.Dispose();
    }

    private static void DropProviderEntries()
    {
        lock (Gate)
        {
            foreach (var key in Images.Keys.Where(item => item.Source.StartsWith("image://", StringComparison.OrdinalIgnoreCase)).ToArray())
                Drop(key);
        }
    }

    /// <summary>A second image, so disposing the cached one leaves the caller's picture intact.</summary>
    private static SKImage? Copy(SKImage image)
    {
        if (image.Width <= 0 || image.Height <= 0)
            return null;
        using var surface = SKSurface.Create(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            return null;
        surface.Canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    private static SKImage? LoadUncached(string source, string? baseDirectory, int width, int height)
    {
        try
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
            return Decode(File.ReadAllBytes(path), path, width, height);
        }
        catch (Exception)
        {
            // A bad data: URL, an unreadable file or a broken image is an image that didn't load, not a script error.
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
        var scale = 1f;
        if (width > 0 && height > 0)
            scale = Math.Max((float)width / info.Width, (float)height / info.Height);
        else if (width > 0)
            scale = (float)width / info.Width;
        else if (height > 0)
            scale = (float)height / info.Height;
        scale = Math.Min(scale, (float)CapScale(info.Width, info.Height));
        if (scale < 1)
        {
            // Decode at a size near the one asked for, which saves memory for big pictures.
            var dims = codec.GetScaledDimensions(scale);
            info = info.WithSize(dims.Width, dims.Height);
        }
        using var bitmap = DecodeAt(codec, info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul)) ?? SKBitmap.Decode(bytes);
        if (bitmap is null)
            return null;
        // A codec that can't decode smaller, such as PNG's, gives the whole picture, so a huge one is scaled down here.
        var cap = CapScale(bitmap.Width, bitmap.Height);
        if (cap >= 1)
            return SKImage.FromBitmap(bitmap);
        using var capped = bitmap.Resize(Scaled(bitmap.Width, bitmap.Height, cap), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return capped is null ? null : SKImage.FromBitmap(capped);
    }

    /// <summary>The scale that brings a picture down to <see cref="MaxPixels"/>, or 1 when it is no bigger.</summary>
    private static double CapScale(double width, double height) => Math.Min(1, Math.Sqrt(MaxPixels / (width * height)));

    private static SKSizeI Scaled(double width, double height, double scale) =>
        new((int)Math.Max(1, Math.Floor(width * scale)), (int)Math.Max(1, Math.Floor(height * scale)));

    private static SKBitmap? DecodeAt(SKCodec codec, SKImageInfo info)
    {
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return bitmap;
        bitmap.Dispose();
        return null;
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
        double w = width > 0 ? width : Math.Ceiling(bounds.Width);
        double h = height > 0 ? height : Math.Ceiling(bounds.Height);
        if (width > 0 && height <= 0)
            h = Math.Ceiling(bounds.Height * width / bounds.Width);
        if (height > 0 && width <= 0)
            w = Math.Ceiling(bounds.Width * height / bounds.Height);
        // The document picks its own size, so cap it like a decoded picture.
        var size = Scaled(w, h, CapScale(w, h));
        using var surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(size.Width / bounds.Width, size.Height / bounds.Height);
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

    public Image() => Theme.Changed += OnTheme;

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
                if (loop is null)
                {
                    image?.Dispose();
                    return;
                }
                loop.Post(() =>
                {
                    if (generation != _loadGeneration || IsDestroyed)
                    {
                        image?.Dispose();
                        return;
                    }
                    SetImage(image, image is null ? (int)StatusValue.Error : (int)StatusValue.Ready);
                    if (image is null)
                        Engine?.Log.Warn("scene", $"QML {TypeName}: Cannot open: {source}" + (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? " (only https:// images are loaded)" : ""));
                });
            }, TaskScheduler.Default);
            return;
        }
        var loaded = ImageLoader.Load(_source, baseDir, w, h, _cache);
        SetImage(loaded, loaded is null ? (int)StatusValue.Error : (int)StatusValue.Ready);
        if (loaded is null)
            Engine?.Log.Warn("scene", $"QML {TypeName}: Cannot open: {_source}");
    }

    private void OnTheme()
    {
        if (!IsDestroyed && _source.StartsWith("image://", StringComparison.OrdinalIgnoreCase))
            Reload();
    }

    protected override void OnDestroy()
    {
        Theme.Changed -= OnTheme;
        _image?.Dispose();
        _image = null;
        base.OnDestroy();
    }

    private void SetImage(SKImage? image, int status)
    {
        if (!ReferenceEquals(_image, image))
        {
            _image?.Dispose();
            _image = image;
        }
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
