using System.Runtime.InteropServices;
using SkiaSharp;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Capture;

/// <summary>An explicitly started Windows capture. Stop, destruction and reload release all captured pixels.</summary>
[QmlSignal("frameReady")]
public sealed class CaptureSession : QObject
{
    private readonly Func<ICaptureBackend> _create;
    private ICaptureBackend? _backend;
    private IDisposable? _timer;
    private IDisposable? _targetDestroyed;
    private QObject? _target;
    private bool _includeCursor;
    private int _frameInterval = 33;
    private int _generation;
    private string _error = "";
    private SKImage? _image;

    public CaptureSession() : this(() => new WindowsCaptureBackend()) { }

    internal CaptureSession(Func<ICaptureBackend> create) => _create = create;

    /// <summary>An AppWindow from WindowList.windows, or a ShellScreen from Shell.screens. Changing it stops capture.</summary>
    [Qml]
    public QObject? Target
    {
        get { Track(); return _target; }
        set
        {
            if (ReferenceEquals(_target, value))
                return;
            Stop();
            _targetDestroyed?.Dispose();
            _target = value;
            _targetDestroyed = value?.Connect("destroyed", _ => Target = null);
            NotifyChanged("target");
        }
    }

    [Qml] public bool Running { get { Track(); return _backend is not null; } }
    [Qml] public bool HasFrame { get { Track(); return _image is not null; } }
    [Qml] public string Error { get { Track(); return _error; } }
    [Qml] public QSize PixelSize { get { Track(); return new(_image?.Width ?? 0, _image?.Height ?? 0); } }

    /// <summary>Applied on the next start. Previews omit the pointer by default.</summary>
    [Qml] public bool IncludeCursor { get { Track(); return _includeCursor; } set => SetField(ref _includeCursor, value); }

    /// <summary>Minimum time between frame reads; 16–1000 ms. Frames stay on D3D until consumed.</summary>
    [Qml] public int FrameInterval { get { Track(); return _frameInterval; } set => SetField(ref _frameInterval, Math.Clamp(value, 16, 1000)); }

    internal SKImage? Image => _image;
    internal event Action? ImageChanged;

    [Qml]
    public bool Start()
    {
        if (IsDestroyed)
            return false;
        Stop();
        SetField(ref _error, "", nameof(Error));
        try
        {
            var target = _target switch
            {
                AppWindow { IsDestroyed: false } window => new CaptureTarget(window.Hwnd, ""),
                ShellScreen { IsDestroyed: false } screen => new CaptureTarget(0, screen.Id),
                _ => throw new InvalidOperationException("Choose an AppWindow or ShellScreen before starting capture."),
            };
            var loop = Engine?.Loop ?? throw new InvalidOperationException("Capture requires an engine event loop.");
            var generation = _generation;
            _backend = _create();
            _backend.Start(target, _includeCursor, () => loop.Post(() =>
            {
                if (generation == _generation && !IsDestroyed)
                    Stop();
            }));
            NotifyChanged("running");
            if (generation == _generation && _backend is not null)
                Schedule(generation);
            return Running;
        }
        catch (Exception e) when (IsCaptureError(e))
        {
            Fail(e);
            return false;
        }
    }

    private void Schedule(int generation) => _timer = Engine!.Loop.Schedule(TimeSpan.FromMilliseconds(_frameInterval), () =>
    {
        _timer = null;
        if (IsDestroyed || generation != _generation || _backend is null)
            return;
        try
        {
            if (_backend.ReadLatest() is { } image)
            {
                SetImage(image);
                Emit("frameReady");
            }
        }
        catch (Exception e) when (IsCaptureError(e))
        {
            Fail(e);
        }
        finally
        {
            if (generation == _generation && _backend is not null && !IsDestroyed)
                Schedule(generation);
        }
    });

    [Qml]
    public void Stop()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
        var backend = _backend;
        _backend = null;
        try { backend?.Dispose(); }
        catch (Exception e) when (IsCaptureError(e))
        {
            SetField(ref _error, e.Message, nameof(Error));
        }
        finally
        {
            SetImage(null);
            if (backend is not null)
                NotifyChanged("running");
        }
    }

    /// <summary>Saves the current frame as PNG to a new local file. Never overwrites an existing file.</summary>
    [Qml]
    public bool SaveToFile(string path)
    {
        try
        {
            if (_image is null)
                throw new InvalidOperationException("No capture frame is available.");
            var local = ImageLoader.LocalPath(path, Engine?.ShellDir);
            if (string.IsNullOrWhiteSpace(path) || local is null)
                throw new ArgumentException("Choose a local file path.", nameof(path));
            using var data = _image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new FileStream(local, FileMode.CreateNew, FileAccess.Write);
            data.SaveTo(stream);
            SetField(ref _error, "", nameof(Error));
            return true;
        }
        catch (Exception e) when (IsCaptureError(e))
        {
            SetField(ref _error, e.Message, nameof(Error));
            return false;
        }
    }

    /// <summary>Copies the current frame to the Windows clipboard, owned by a visible shell window.</summary>
    [Qml]
    public bool CopyToClipboard(ShellWindow? owner)
    {
        try
        {
            if (_image is null || owner?.Backend is not Win32Window window)
                throw new InvalidOperationException("A capture frame and an open shell window are required.");
            CaptureClipboard.Copy(_image, window.Handle);
            SetField(ref _error, "", nameof(Error));
            return true;
        }
        catch (Exception e) when (IsCaptureError(e))
        {
            SetField(ref _error, e.Message, nameof(Error));
            return false;
        }
    }

    private void SetImage(SKImage? image)
    {
        var previous = _image;
        _image = image;
        previous?.Dispose();
        NotifyChanged("hasFrame");
        NotifyChanged("pixelSize");
        ImageChanged?.Invoke();
    }

    private void Fail(Exception error)
    {
        Stop();
        SetField(ref _error, error.Message, nameof(Error));
    }

    private static bool IsCaptureError(Exception e) => e is COMException or UnauthorizedAccessException or
        InvalidOperationException or ArgumentException or IOException or NotSupportedException or System.ComponentModel.Win32Exception or
        SharpGen.Runtime.SharpGenException;

    protected override void OnDestroy()
    {
        _targetDestroyed?.Dispose();
        _targetDestroyed = null;
        _target = null;
        Stop();
        ImageChanged = null;
    }
}

/// <summary>Aspect-fitted live preview in logical scene coordinates; the session owns its pixels.</summary>
public sealed class CaptureView : Item
{
    private CaptureSession? _session;
    private IDisposable? _destroyed;

    [Qml]
    public CaptureSession? Session
    {
        get { Track(); return _session; }
        set
        {
            if (ReferenceEquals(value, _session))
                return;
            if (_session is not null)
                _session.ImageChanged -= OnImageChanged;
            _destroyed?.Dispose();
            _session = value;
            if (value is not null)
                value.ImageChanged += OnImageChanged;
            _destroyed = value?.Connect("destroyed", _ => Session = null);
            NotifyChanged("session");
            OnImageChanged();
        }
    }

    private void OnImageChanged()
    {
        var ratio = Host?.DevicePixelRatio ?? 1;
        ImplicitWidth = (_session?.Image?.Width ?? 0) / ratio;
        ImplicitHeight = (_session?.Image?.Height ?? 0) / ratio;
        Update();
    }

    protected override void OnHostChanged() => OnImageChanged();

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        if (_session?.Image is not { } image || Width <= 0 || Height <= 0)
            return;
        var scale = Math.Min(Width / image.Width, Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        var rect = SKRect.Create((float)((Width - width) / 2), (float)((Height - height) / 2), (float)width, (float)height);
        canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear));
    }

    protected override void OnDestroy()
    {
        Session = null;
        base.OnDestroy();
    }
}

public static class CaptureModule
{
    public static void Register(ModuleRegistry registry) => registry.Module("Winshell.Capture")
        .Type<CaptureSession>("CaptureSession")
        .Type<CaptureView>("CaptureView");
}
