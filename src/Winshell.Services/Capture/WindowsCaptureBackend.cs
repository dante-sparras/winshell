using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Vortice.Direct3D11;
using Vortice.Direct3D;
using Vortice.DXGI;
using Vortice.Mathematics;
using Winshell.Native;

namespace Winshell.Capture;

/// <summary>
/// A two-buffer WGC pool on a private D3D device. The session samples the latest frame, so slow consumers never
/// queue frames or UI callbacks. Only a consumed frame leaves the GPU.
/// </summary>
/// <param name="log">Where a teardown that fails is reported.</param>
internal sealed class WindowsCaptureBackend(Log log) : ICaptureBackend
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private ID3D11Texture2D? _staging;
    private SizeInt32 _size;
    private bool _readbackPending;
    private int _poolGeneration;
    private Action? _closed;

    public void Start(CaptureTarget target, bool includeCursor, Action closed)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("Windows Graphics Capture is unavailable in this session.");
        _closed = closed;
        var monitor = target.Window == 0 ? MonitorHandle(target.MonitorId) : 0;
        if (target.Window == 0 && monitor == 0)
            throw new ArgumentException("The selected monitor is no longer connected.", nameof(target));
        _item = CaptureInterop.CreateItem(target.Window, monitor);
        _size = _item.Size;
        if (_size.Width <= 0 || _size.Height <= 0)
            throw new InvalidOperationException("The selected source has no capture surface.");

        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
        var result = D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware,
            DeviceCreationFlags.BgraSupport, levels, out _device, out _context);
        if (result.Failure)
            D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Warp,
                DeviceCreationFlags.BgraSupport, levels, out _device, out _context).CheckError();
        _winrtDevice = CaptureInterop.CreateDevice(_device!);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2, _size);
        _session = _pool.CreateCaptureSession(_item);
        // Leave the system's capture border enabled. No borderless capability or consent bypass.
        _session.IsCursorCaptureEnabled = includeCursor;
        _item.Closed += OnClosed;
        _session.StartCapture();
    }

    private static nint MonitorHandle(string device)
    {
        foreach (var handle in Monitors.List())
            if (Monitors.TryRead(handle, out var monitor) && monitor.Device == device)
                return handle;
        return 0;
    }

    private void OnClosed(GraphicsCaptureItem sender, object args)
    {
        Volatile.Read(ref _closed)?.Invoke();
    }

    public PixelImage? ReadLatest()
    {
        if (_pool is null)
            return null;
        var generation = Volatile.Read(ref _poolGeneration);
        var ready = _readbackPending ? Readback() : null;
        if (_readbackPending)
            return null; // GPU work is still in flight; never stall the shell's scene thread.
        Direct3D11CaptureFrame? latest = null;
        try
        {
            // At most the pool's two buffers. Never wait for a producer or drain an unbounded queue.
            for (var i = 0; i < 2 && generation == Volatile.Read(ref _poolGeneration); i++)
            {
                var pool = _pool;
                if (pool is null)
                    break;
                var frame = pool.TryGetNextFrame();
                if (frame is null)
                    break;
                latest?.Dispose();
                latest = frame;
            }

            if (generation != Volatile.Read(ref _poolGeneration))
            {
                latest?.Dispose();
                latest = null;
                return ready;
            }

            if (latest is null)
                return ready;
            var size = latest.ContentSize;
            if (size.Width <= 0 || size.Height <= 0)
                return ready;
            // The first frame after a resize may have old-sized storage. Drop it rather than export clipped pixels.
            if (size.Width != _size.Width || size.Height != _size.Height)
            {
                latest.Dispose();
                latest = null;
                _size = size;
                _staging?.Dispose();
                _staging = null;
                // A static source may not invalidate after Recreate. Stop the old session and
                // release its frames before the pool goes away, then start a new pair.
                var cursor = _session!.IsCursorCaptureEnabled;
                RecreatePool(size, cursor);
                return ready;
            }

            using var texture = CaptureInterop.Texture(latest.Surface);
            var desc = texture.Description;
            if (desc.Width < size.Width || desc.Height < size.Height)
                return ready;
            if (_staging is null)
            {
                desc.Width = (uint)size.Width;
                desc.Height = (uint)size.Height;
                desc.Usage = ResourceUsage.Staging;
                desc.BindFlags = BindFlags.None;
                desc.CPUAccessFlags = CpuAccessFlags.Read;
                desc.MiscFlags = ResourceOptionFlags.None;
                desc.ArraySize = 1;
                desc.MipLevels = 1;
                _staging = _device!.CreateTexture2D(desc);
            }

            _context!.CopySubresourceRegion(_staging, 0, 0, 0, 0, texture, 0,
                new Box(0, 0, 0, size.Width, size.Height, 1));
            _readbackPending = true;
            _context.Flush();
            return ready ?? Readback();
        }
        finally
        {
            latest?.Dispose();
        }
    }

    private PixelImage? Readback()
    {
        if (CaptureInterop.Read(_context!, _staging!, _size.Width, _size.Height) is not { } pixels)
            return null;
        _readbackPending = false;
        return new PixelImage(_size.Width, _size.Height, pixels, true);
    }

    private void RecreatePool(SizeInt32 size, bool cursor)
    {
        var session = _session;
        var pool = _pool;
        _session = null;
        _pool = null;
        Interlocked.Increment(ref _poolGeneration);
        ReleaseSessionAndPool(session, pool);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2, size);
        _session = _pool.CreateCaptureSession(_item!);
        _session.IsCursorCaptureEnabled = cursor;
        _session.StartCapture();
    }

    /// <summary>
    /// Stops capture, releases frames the pool still holds, then disposes the pool.
    /// A free-threaded pool can still be producing a buffer after the UI thread decides to drop it.
    /// </summary>
    private void ReleaseSessionAndPool(GraphicsCaptureSession? session, Direct3D11CaptureFramePool? pool)
    {
        try
        {
            try
            {
                session?.Dispose();
            }
            catch (Exception e) when (CaptureFailure.IsExpected(e))
            {
                log.Warn("capture", $"Capture teardown: {e.Message}");
            }

            try
            {
                for (var i = 0; i < 2 && pool?.TryGetNextFrame() is { } frame; i++)
                    frame.Dispose();
            }
            catch (Exception e) when (CaptureFailure.IsExpected(e))
            {
                log.Warn("capture", $"Capture teardown: {e.Message}");
            }
        }
        finally
        {
            try
            {
                pool?.Dispose();
            }
            catch (Exception e) when (CaptureFailure.IsExpected(e))
            {
                log.Warn("capture", $"Capture teardown: {e.Message}");
            }
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _closed, null);
        Interlocked.Increment(ref _poolGeneration);
        var item = _item;
        var session = _session;
        var pool = _pool;
        IDisposable?[] resources = [_staging, _winrtDevice, _context, _device];
        _session = null;
        _pool = null;
        _staging = null;
        _readbackPending = false;
        _winrtDevice = null;
        _context = null;
        _device = null;
        _item = null;
        try
        {
            if (item is not null)
                item.Closed -= OnClosed;
        }
        finally
        {
            // A lost device must not prevent the rest of the session's resources from being released.
            ReleaseSessionAndPool(session, pool);
            foreach (var resource in resources)
                try
                {
                    resource?.Dispose();
                }
                catch (Exception e) when (CaptureFailure.IsExpected(e))
                {
                    log.Warn("capture", $"Capture teardown: {e.Message}");
                }
        }
    }
}
