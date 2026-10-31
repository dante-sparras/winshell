using SkiaSharp;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;
using static Winshell.Platform.Windows.Services.WindowList.NativeMethods;

namespace Winshell.Platform.Windows.Services.WindowList;

/// <summary>
/// The <c>WindowThumbnail</c>: a live preview of an app window, as the taskbar and Alt+Tab show it. DWM draws it
/// over the shell window this item is in, so it stays live without copying pixels and puts no capture border around
/// the source. It fills the item as far as it can without being enlarged, keeping its shape, centered. It follows the item's position and opacity, and is hidden whenever the
/// item is not drawn. Items cannot draw over it, and a clipping parent does not clip it.
/// </summary>
public sealed unsafe class WindowThumbnail : Item
{
    private AppWindow? _window;
    private IDisposable? _closed;
    private Scene? _scene;
    private nint _thumbnail;
    private nint _destination;
    private bool _painted;
    private RECT _box;
    private byte _opacity;
    private RECT _insets;
    private DWM_THUMBNAIL_PROPERTIES _applied;
    private QSize _sourceSize;
    private bool _peek;

    /// <summary>The app window to preview, from <c>WindowList.windows</c>. Null shows nothing.</summary>
    [Qml]
    public AppWindow? Window
    {
        get { Track(); return _window; }
        set
        {
            if (ReferenceEquals(value, _window))
                return;
            _closed?.Dispose();
            Release();
            _window = value;
            _closed = value?.Connect("closed", _ => Window = null);
            NotifyChanged("window");
            Update();
            UpdatePeek();
        }
    }

    /// <summary>
    /// While true, Windows shows this window at full size and hides the others, as its own peek does. It stays off
    /// when the user turned peek off, and ends with this item or the window.
    /// </summary>
    [Qml]
    public bool Peek
    {
        get { Track(); return _peek; }
        set
        {
            if (SetField(ref _peek, value, nameof(Peek)))
                UpdatePeek();
        }
    }

    internal WindowPeek Peeker { get; init; } = WindowPeek.ForSystem();

    /// <summary>The previewed part of the window in physical pixels, or zero before the preview is first shown.</summary>
    [Qml] public QSize SourceSize { get { Track(); return _sourceSize; } }

    protected override void OnHostChanged()
    {
        var scene = Host as Scene;
        if (scene != _scene)
        {
            if (_scene is not null)
                _scene.Painted -= OnPainted;
            Release();
            _scene = scene;
            if (scene is not null)
                scene.Painted += OnPainted;
        }
        Update();
        UpdatePeek();
    }

    private nint HostWindow => ((Host?.WindowObject as ShellWindow)?.Backend as Win32Window)?.Handle ?? 0;

    private void UpdatePeek() => Peeker.Show(_peek && _window is { } window ? window.Hwnd : 0, HostWindow);

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        if (_window is null || Width <= 0 || Height <= 0)
            return;
        var r = canvas.TotalMatrix.MapRect(SKRect.Create((float)Width, (float)Height));
        _box = new RECT((int)Math.Round(r.Left), (int)Math.Round(r.Top), (int)Math.Round(r.Right), (int)Math.Round(r.Bottom));
        var opacity = 1.0;
        for (Item? item = this; item is not null; item = item.ParentItem)
            opacity *= item.Opacity;
        _opacity = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        _painted = true;
    }

    private void OnPainted()
    {
        var painted = _painted;
        _painted = false;
        if (painted)
            Show();
        else if (_thumbnail != 0 && _applied.Visible != 0)
            Apply(_applied with { Visible = 0 });
    }

    private void Show()
    {
        var destination = HostWindow;
        if (_window is not { } window || destination == 0)
        {
            Release();
            return;
        }
        if (_thumbnail != 0 && _destination != destination)
            Release();
        if (_thumbnail == 0)
        {
            if (DwmRegisterThumbnail(destination, window.Hwnd, out var thumbnail) != 0)
                return;
            _thumbnail = thumbnail;
            _destination = destination;
            _applied = default;
        }
        if (DwmQueryThumbnailSourceSize(_thumbnail, out var size) != 0)
            return;
        var source = Source(window.Hwnd, size);
        if (SetField(ref _sourceSize, new QSize(source.Width, source.Height), nameof(SourceSize)))
        {
            var ratio = Host?.DevicePixelRatio ?? 1;
            ImplicitWidth = source.Width / ratio;
            ImplicitHeight = source.Height / ratio;
        }
        Apply(new DWM_THUMBNAIL_PROPERTIES
        {
            Flags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY | DWM_TNP_VISIBLE,
            Destination = WindowRules.FitPreview(_box, source.Width, source.Height),
            Source = source,
            Opacity = _opacity,
            Visible = 1,
        });
    }

    /// <summary>
    /// The window without its invisible resize borders. A minimized window's rectangle is the small stub Windows
    /// keeps for it, so it uses the borders measured while it was shown.
    /// </summary>
    private RECT Source(nint hwnd, THUMBNAIL_SIZE size)
    {
        if (!Win32.IsIconic(hwnd) && Win32.GetWindowRect(hwnd, out var window)
            && DwmGetWindowRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, sizeof(RECT)) == 0)
        {
            var visible = WindowRules.PreviewSource(window, frame);
            _insets = new RECT(visible.Left, visible.Top, window.Width - visible.Right, window.Height - visible.Bottom);
        }
        var inset = new RECT(_insets.Left, _insets.Top, size.Width - _insets.Right, size.Height - _insets.Bottom);
        return inset.Width > 0 && inset.Height > 0 ? inset : new RECT(0, 0, size.Width, size.Height);
    }

    private void Apply(DWM_THUMBNAIL_PROPERTIES properties)
    {
        if (properties.Equals(_applied))
            return;
        if (DwmUpdateThumbnailProperties(_thumbnail, properties) == 0)
            _applied = properties;
    }

    private void Release()
    {
        if (_thumbnail != 0)
            DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = 0;
        _destination = 0;
        _applied = default;
    }

    protected override void OnDestroy()
    {
        _closed?.Dispose();
        Release();
        Peeker.End();
        if (_scene is not null)
            _scene.Painted -= OnPainted;
        _scene = null;
        base.OnDestroy();
    }
}
