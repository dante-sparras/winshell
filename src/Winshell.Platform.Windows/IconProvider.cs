using System.Runtime.InteropServices;
using SkiaSharp;
using Winshell.Native;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows;

/// <summary>
/// Icons for <c>image://icon/name</c>: a file or program's icon from the Windows shell, an image file, or a
/// freedesktop icon name drawn with the matching Segoe Fluent Icons glyph.
/// </summary>
public sealed class IconProvider : IImageProvider, IIconLookup
{
    /// <summary>How many drawn icons stay cached. A long session asks for a few sizes of many apps, then the least recently used ones go.</summary>
    private const int CacheCapacity = 256;

    public static readonly IconProvider Instance = new();

    private readonly IconCache _cache;

    private IconProvider()
        : this(CacheCapacity)
    {
        Theme.Changed += () => _cache.Clear();
    }

    internal IconProvider(int capacity) => _cache = new IconCache(capacity);

    internal static IconProvider CreateForTests(int capacity) => new(capacity);

    internal int CachedCount => _cache.Count;

    internal void DropCache() => _cache.Clear();

    /// <summary>Freedesktop icon names, and the Segoe Fluent Icons glyph that means the same.</summary>
    private static readonly Dictionary<string, char> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio-volume-high"] = '', ["audio-volume-medium"] = '', ["audio-volume-low"] = '',
        ["audio-volume-muted"] = '', ["audio-volume-off"] = '', ["audio-speakers"] = '',
        ["audio-headphones"] = '', ["audio-headset"] = '', ["audio-card"] = '',
        ["audio-input-microphone"] = '', ["microphone-sensitivity-muted"] = '', ["microphone-disabled"] = '',
        ["battery"] = '', ["battery-full"] = '', ["battery-good"] = '', ["battery-medium"] = '',
        ["battery-low"] = '', ["battery-caution"] = '', ["battery-empty"] = '', ["battery-missing"] = '',
        ["battery-full-charging"] = '', ["battery-good-charging"] = '', ["battery-medium-charging"] = '',
        ["battery-low-charging"] = '', ["battery-caution-charging"] = '', ["battery-empty-charging"] = '',
        ["ac-adapter"] = '',
        ["network-wireless"] = '', ["network-wireless-signal-excellent"] = '', ["network-wireless-signal-good"] = '',
        ["network-wireless-signal-ok"] = '', ["network-wireless-signal-weak"] = '', ["network-wireless-signal-none"] = '',
        ["network-wireless-offline"] = '', ["network-wireless-disconnected"] = '', ["network-wired"] = '',
        ["network-offline"] = '', ["network-error"] = '', ["airplane-mode"] = '', ["network-vpn"] = '',
        ["bluetooth"] = '', ["bluetooth-active"] = '', ["bluetooth-disabled"] = '',
        ["system-shutdown"] = '', ["system-reboot"] = '', ["system-restart"] = '', ["system-lock-screen"] = '',
        ["system-log-out"] = '', ["system-suspend"] = '', ["system-hibernate"] = '', ["system-search"] = '',
        ["system-run"] = '', ["system-file-manager"] = '', ["system-settings"] = '', ["preferences-system"] = '',
        ["applications-system"] = '', ["configure"] = '', ["settings"] = '',
        ["edit-copy"] = '', ["edit-paste"] = '', ["edit-cut"] = '', ["edit-delete"] = '',
        ["edit-clear"] = '', ["edit-find"] = '', ["edit-undo"] = '', ["edit-redo"] = '',
        ["window-close"] = '', ["window-minimize"] = '', ["window-maximize"] = '', ["window-restore"] = '',
        ["go-next"] = '', ["go-previous"] = '', ["go-up"] = '', ["go-down"] = '', ["go-home"] = '',
        ["media-playback-start"] = '', ["media-playback-pause"] = '', ["media-playback-stop"] = '',
        ["media-skip-forward"] = '', ["media-skip-backward"] = '', ["media-seek-forward"] = '',
        ["media-seek-backward"] = '', ["media-playlist-shuffle"] = '', ["media-playlist-repeat"] = '',
        ["media-playlist-repeat-song"] = '', ["media-record"] = '', ["multimedia-player"] = '',
        ["folder"] = '', ["folder-open"] = '', ["user-home"] = '', ["user-desktop"] = '',
        ["user-trash"] = '', ["document"] = '', ["text-x-generic"] = '', ["image-x-generic"] = '',
        ["user"] = '', ["avatar-default"] = '', ["user-available"] = '',
        ["x-office-calendar"] = '', ["calendar"] = '', ["appointment"] = '', ["clock"] = '',
        ["preferences-system-time"] = '', ["alarm"] = '',
        ["video-display"] = '', ["computer"] = '', ["input-keyboard"] = '', ["input-mouse"] = '',
        ["input-gaming"] = '', ["printer"] = '', ["camera-photo"] = '', ["camera-web"] = '',
        ["phone"] = '', ["drive-harddisk"] = '', ["drive-removable-media"] = '', ["media-optical"] = '',
        ["mail"] = '', ["mail-unread"] = '', ["internet-mail"] = '', ["web-browser"] = '',
        ["internet-web-browser"] = '', ["help-browser"] = '', ["help"] = '',
        ["display-brightness"] = '', ["brightness"] = '', ["night-light"] = '', ["weather-clear"] = '',
        ["weather-clear-night"] = '', ["weather-few-clouds"] = '', ["weather-overcast"] = '',
        ["weather-showers"] = '', ["weather-snow"] = '', ["weather-storm"] = '',
        ["dialog-information"] = '', ["dialog-warning"] = '', ["dialog-error"] = '', ["dialog-question"] = '',
        ["emblem-ok"] = '', ["object-select"] = '', ["checkbox-checked"] = '',
        ["list-add"] = '', ["list-remove"] = '', ["view-more"] = '', ["view-more-horizontal"] = '',
        ["view-refresh"] = '', ["view-grid"] = '', ["view-list"] = '', ["open-menu"] = '',
        ["application-menu"] = '', ["view-pin"] = '', ["pin"] = '', ["window-pin"] = '',
        ["notifications"] = '', ["notification"] = '', ["preferences-desktop-notification"] = '',
        ["notifications-disabled"] = '', ["start-here"] = '', ["applications-all"] = '',
        ["system-users"] = '', ["security-high"] = '', ["security-medium"] = '', ["security-low"] = '',
        ["changes-prevent"] = '', ["changes-allow"] = '', ["accessories-calculator"] = '',
        ["accessories-text-editor"] = '', ["utilities-terminal"] = '', ["terminal"] = '',
        ["image-missing"] = '', ["application-x-executable"] = '', ["package"] = '', ["download"] = '',
        ["upload"] = '', ["send"] = '', ["star"] = '', ["starred"] = '', ["favorite"] = '',
        ["power-profile-performance"] = '', ["power-profile-balanced"] = '', ["power-profile-power-saver"] = '',
    };

    private static SKTypeface? _fluent;

    private static SKTypeface Fluent => _fluent ??= SKFontManager.Default.MatchFamily("Segoe Fluent Icons") ?? SKFontManager.Default.MatchFamily("Segoe MDL2 Assets") ?? SKTypeface.Default;

    public bool HasIcon(string name)
    {
        var key = Normalize(name);
        var path = Expand(name);
        return Glyphs.ContainsKey(key) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || File.Exists(path) || IsApplication(name) || AppIconLookup?.Invoke(name) is not null;
    }

    private static string Normalize(string name)
    {
        var n = name.EndsWith("-symbolic", StringComparison.OrdinalIgnoreCase) ? name[..^9] : name;
        return n;
    }

    private static string Expand(string name) => Environment.ExpandEnvironmentVariables(Uri.UnescapeDataString(name).Replace("file://", "", StringComparison.OrdinalIgnoreCase).TrimStart('/'));

    private static bool IsApplication(string name) => ProcessLauncher.Which(name) is not null;

    public SKImage? Load(string id, int requestedWidth, int requestedHeight)
    {
        var size = Math.Max(16, Math.Max(requestedWidth, requestedHeight));
        if (requestedWidth <= 0 && requestedHeight <= 0)
            size = 64;
        var key = (id, size);
        if (_cache.TryGet(key, out var cached))
            return cached;
        var loaded = LoadUncached(id, size);
        if (loaded is null)
            return null;
        return _cache.Add(key, loaded);
    }

    private static SKImage? LoadUncached(string id, int size)
    {
        // "name?path=..." and "name?fallback=..." from tray items; the part before the query names the icon.
        var query = id.IndexOf('?');
        var name = query >= 0 ? id[..query] : id;
        var path = Expand(name);
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return ShellIcon(path, size);
        if (path.Length > 2 && (File.Exists(path) || Directory.Exists(path)))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" or ".bmp" or ".gif")
                return ImageLoader.Load(path, null, size, size, cache: false);
            return ShellIcon(path, size);
        }
        var key = Normalize(name);
        if (Glyphs.TryGetValue(key, out var glyph))
            return GlyphImage(glyph, size);
        if (ProcessLauncher.Which(name) is { } program)
            return ShellIcon(program, size);
        // A name like "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" or "notepad": the Start menu's apps.
        if (AppIconLookup?.Invoke(name) is { } app)
            return ShellIcon(app, size);
        return null;
    }

    /// <summary>Set by the app index: the file or shell path for an app id, so icons follow the Start menu.</summary>
    public static Func<string, string?>? AppIconLookup { get; set; }

    private static SKImage GlyphImage(char glyph, int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var font = new SKFont(Fluent, size * 0.8f);
        using var paint = new SKPaint { IsAntialias = true, Color = Theme.Current.TextFillColorPrimary.ToSk() };
        var text = glyph.ToString();
        var width = font.MeasureText(text, paint);
        font.GetFontMetrics(out var m);
        var x = (size - width) / 2;
        var y = (size - (m.Descent - m.Ascent)) / 2 - m.Ascent;
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        return surface.Snapshot();
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int Cx;
        public int Cy;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    /// <summary>The icon the Windows shell shows for a file or program, at the size asked.</summary>
    public static SKImage? ShellIcon(string path, int size)
    {
        try
        {
            SHCreateItemFromParsingName(path, 0, typeof(IShellItemImageFactory).GUID, out var factory);
            // SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK | SIIGBF_SCALEUP. Size is in pixels, so a 1.5x display asking for 32 logical pixels passes 48.
            if (factory.GetImage(new SIZE { Cx = size, Cy = size }, 0x4 | 0x1 | 0x100, out var hbitmap) != 0 || hbitmap == 0)
                return null;
            try
            {
                if (Win32.GetObjectW(hbitmap, Marshal.SizeOf<BITMAP>(), out var bmp) == 0 || bmp.bmBits == 0 || bmp.bmBitsPixel != 32)
                    return null;
                var info = new SKImageInfo(bmp.bmWidth, Math.Abs(bmp.bmHeight), SKColorType.Bgra8888, SKAlphaType.Premul);
                using var bitmap = new SKBitmap(info);
                var rows = Math.Abs(bmp.bmHeight);
                unsafe
                {
                    var dst = (byte*)bitmap.GetPixels();
                    var src = (byte*)bmp.bmBits;
                    for (var y = 0; y < rows; y++)
                    {
                        // The bitmap is stored bottom up.
                        var from = src + (bmp.bmHeight > 0 ? rows - 1 - y : y) * bmp.bmWidthBytes;
                        Buffer.MemoryCopy(from, dst + y * bitmap.RowBytes, bitmap.RowBytes, Math.Min(bitmap.RowBytes, bmp.bmWidthBytes));
                    }
                }
                using var raw = SKImage.FromBitmap(bitmap);
                using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
                surface.Canvas.Clear(SKColors.Transparent);
                surface.Canvas.DrawImage(raw, SKRect.Create(0, 0, size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
                return surface.Snapshot();
            }
            finally
            {
                Win32.DeleteObject(hbitmap);
            }
        }
        catch (COMException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>A small least-recently-used cache of icons. Clearing it disposes the native bitmaps.</summary>
internal sealed class IconCache
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<(string Id, int Size), LinkedListNode<Entry>> _nodes = new();
    private readonly LinkedList<Entry> _order = new();

    internal IconCache(int capacity) => _capacity = Math.Max(1, capacity);

    internal int Count
    {
        get { lock (_gate) return _nodes.Count; }
    }

    internal bool TryGet((string Id, int Size) key, out SKImage? image)
    {
        lock (_gate)
        {
            if (!_nodes.TryGetValue(key, out var node))
            {
                image = null;
                return false;
            }
            _order.Remove(node);
            _order.AddFirst(node);
            image = node.Value.Image;
            return true;
        }
    }

    internal SKImage Add((string Id, int Size) key, SKImage image)
    {
        lock (_gate)
        {
            if (_nodes.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _order.AddFirst(existing);
                if (!ReferenceEquals(existing.Value.Image, image))
                    image.Dispose();
                return existing.Value.Image;
            }
            var node = new LinkedListNode<Entry>(new Entry(key, image));
            _order.AddFirst(node);
            _nodes.Add(key, node);
            while (_nodes.Count > _capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _nodes.Remove(last.Value.Key);
                last.Value.Image.Dispose();
            }
            return image;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            foreach (var entry in _order)
                entry.Image.Dispose();
            _order.Clear();
            _nodes.Clear();
        }
    }

    private sealed class Entry((string Id, int Size) key, SKImage image)
    {
        public (string Id, int Size) Key { get; } = key;
        public SKImage Image { get; } = image;
    }
}
