using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Winshell.Capture;

/// <summary>An explicitly started Windows capture. Stop and Dispose release all captured pixels.</summary>
public sealed class CaptureSession : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly Func<ICaptureBackend> _create;
    private readonly Action<PixelImage, nint> _copy;
    private ICaptureBackend? _backend;
    private IDisposable? _timer;
    private CaptureTarget? _target;
    private PixelImage? _frame;
    private bool _includeCursor;
    private int _frameInterval = 33;
    private int _generation;
    private string _error = "";
    private bool _disposed;

    public CaptureSession(IEventLoop loop)
        : this(loop, () => new WindowsCaptureBackend())
    {
    }

    internal CaptureSession(IEventLoop loop, Func<ICaptureBackend> create, Action<PixelImage, nint>? copy = null)
    {
        _loop = loop;
        _create = create;
        _copy = copy ?? CaptureClipboard.Copy;
    }

    /// <summary>A new frame arrived. <see cref="Frame"/> is that frame.</summary>
    public event Action? FrameReady;

    /// <summary>What to capture. Changing it stops capture.</summary>
    public CaptureTarget? Target
    {
        get => _target;
        set
        {
            if (_target == value)
                return;
            Stop();
            _target = value;
            Changed();
        }
    }

    public bool Running => _backend is not null;

    /// <summary>The latest frame, in physical pixels, or null before one arrives and after a stop.</summary>
    public PixelImage? Frame => _frame;

    public bool HasFrame => _frame is not null;

    /// <summary>Why the last start or export failed, or why a running capture failed. Empty after a successful start or export.</summary>
    public string Error => _error;

    /// <summary>The size of <see cref="Frame"/> in physical pixels, or zero without a frame.</summary>
    public QSize PixelSize => new(_frame?.Width ?? 0, _frame?.Height ?? 0);

    /// <summary>Applied on the next start. Previews omit the pointer by default.</summary>
    public bool IncludeCursor
    {
        get => _includeCursor;
        set => Set(ref _includeCursor, value);
    }

    /// <summary>Minimum time between frame reads; 16–1000 ms. Frames stay on D3D until consumed.</summary>
    public int FrameInterval
    {
        get => _frameInterval;
        set => Set(ref _frameInterval, Math.Clamp(value, 16, 1000));
    }

    /// <summary>Starts capturing <see cref="Target"/>, after stopping a capture in progress. On failure <see cref="Error"/> says why.</summary>
    public bool Start()
    {
        if (_disposed)
            return false;
        Stop();
        Set(ref _error, "", nameof(Error));
        try
        {
            var target = _target ?? throw new InvalidOperationException("Choose a window or a screen before starting capture.");
            var generation = _generation;
            _backend = _create();
            _backend.Start(target, _includeCursor, () => _loop.Post(() =>
            {
                if (generation == _generation)
                    Stop();
            }));
            Changed(nameof(Running));
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

    private void Schedule(int generation) => _timer = _loop.Schedule(TimeSpan.FromMilliseconds(_frameInterval), () =>
    {
        _timer = null;
        if (generation != _generation || _backend is null)
            return;
        try
        {
            if (_backend.ReadLatest() is { } frame)
            {
                SetFrame(frame);
                FrameReady?.Invoke();
            }
        }
        catch (Exception e) when (IsCaptureError(e))
        {
            Fail(e);
        }
        finally
        {
            if (generation == _generation && _backend is not null)
                Schedule(generation);
        }
    });

    /// <summary>Stops capturing and drops the current frame.</summary>
    public void Stop()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
        var backend = _backend;
        _backend = null;
        try
        {
            backend?.Dispose();
        }
        catch (Exception e) when (IsCaptureError(e))
        {
            Set(ref _error, e.Message, nameof(Error));
        }
        finally
        {
            SetFrame(null);
            Changed(backend is not null, nameof(Running));
        }
    }

    /// <summary>
    /// Saves the current frame as a PNG to a new file. Takes a local path or <c>file://</c> URL. Never overwrites a file,
    /// and does not create folders.
    /// </summary>
    public bool SaveToFile(string path)
    {
        try
        {
            if (_frame is null)
                throw new InvalidOperationException("No capture frame is available.");
            var local = LocalPath(path) ?? throw new ArgumentException("Choose a local file path.", nameof(path));
            var png = _frame.EncodePng();
            using var stream = new FileStream(local, FileMode.CreateNew, FileAccess.Write);
            stream.Write(png);
            Set(ref _error, "", nameof(Error));
            return true;
        }
        catch (Exception e) when (IsCaptureError(e))
        {
            Set(ref _error, e.Message, nameof(Error));
            return false;
        }
    }

    /// <summary>Copies the current frame to the Windows clipboard, owned by an open window of this program.</summary>
    public bool CopyToClipboard(nint owner)
    {
        try
        {
            if (_frame is null || owner == 0)
                throw new InvalidOperationException("A capture frame and an open window are required.");
            _copy(_frame, owner);
            Set(ref _error, "", nameof(Error));
            return true;
        }
        catch (Exception e) when (IsCaptureError(e))
        {
            Set(ref _error, e.Message, nameof(Error));
            return false;
        }
    }

    /// <summary>The file a path or <c>file://</c> URL names, or null for anything that is not a local file.</summary>
    internal static string? LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(path, UriKind.Absolute, out var uri) ? uri.LocalPath : null;
        return path.Contains("://", StringComparison.Ordinal) || path.StartsWith("data:", StringComparison.Ordinal) ? null : path;
    }

    private void SetFrame(PixelImage? frame)
    {
        var (hadFrame, size) = (HasFrame, PixelSize);
        Set(ref _frame, frame, nameof(Frame));
        Changed(hadFrame != HasFrame, nameof(HasFrame));
        Changed(size != PixelSize, nameof(PixelSize));
    }

    private void Fail(Exception error)
    {
        Stop();
        Set(ref _error, error.Message, nameof(Error));
    }

    private static bool IsCaptureError(Exception e) => e is COMException or UnauthorizedAccessException or
        InvalidOperationException or ArgumentException or IOException or NotSupportedException or Win32Exception or
        SharpGen.Runtime.SharpGenException;

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
