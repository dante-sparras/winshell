using System.Runtime.InteropServices;
using SkiaSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Capture;

/// <summary>
/// A two-buffer WGC pool on a private D3D device. The engine samples the latest frame, so slow consumers never
/// queue frames or UI callbacks. Only a consumed frame crosses to the current raster Skia scene.
/// </summary>
internal sealed class WindowsCaptureBackend : ICaptureBackend
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
        var monitor = target.Window == 0 ? Screens.Find(target.MonitorId)?.Handle ?? 0 : 0;
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
        using var dxgi = _device!.QueryInterface<IDXGIDevice>();
        _winrtDevice = CaptureInterop.CreateDevice(dxgi.NativePointer);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
        _session = _pool.CreateCaptureSession(_item);
        // Leave the system's capture border enabled. No borderless capability or consent bypass.
        _session.IsCursorCaptureEnabled = includeCursor;
        _item.Closed += OnClosed;
        _session.StartCapture();
    }

    private void OnClosed(GraphicsCaptureItem sender, object args) => Volatile.Read(ref _closed)?.Invoke();

    public SKImage? ReadLatest()
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
            using var texture = CaptureInterop.GetTexture(latest.Surface);
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
                new Vortice.Mathematics.Box(0, 0, 0, size.Width, size.Height, 1));
            _readbackPending = true;
            _context.Flush();
            return ready ?? Readback();
        }
        catch
        {
            ready?.Dispose();
            throw;
        }
        finally
        {
            latest?.Dispose();
        }
    }

    private SKImage? Readback()
    {
        var result = _context!.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait, out var mapped);
        if (result.Code == unchecked((int)0x887a000a)) // DXGI_ERROR_WAS_STILL_DRAWING
            return null;
        result.CheckError();
        _readbackPending = false;
        try
        {
            using var bitmap = new SKBitmap(new SKImageInfo(_size.Width, _size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            CopyRows(mapped.DataPointer, checked((int)mapped.RowPitch), bitmap.GetPixels(), bitmap.RowBytes, _size.Width, _size.Height);
            return SKImage.FromBitmap(bitmap);
        }
        finally { _context.Unmap(_staging!, 0); }
    }

    internal static unsafe void CopyRows(nint source, int sourcePitch, nint destination, int destinationPitch, int width, int height)
    {
        var bytes = checked(width * 4);
        if (width <= 0 || height <= 0 || sourcePitch < bytes || destinationPitch < bytes)
            throw new ArgumentOutOfRangeException(nameof(width));
        for (var row = 0; row < height; row++)
            Buffer.MemoryCopy((byte*)source + (long)row * sourcePitch, (byte*)destination + (long)row * destinationPitch, destinationPitch, bytes);
    }

    private void RecreatePool(SizeInt32 size, bool cursor)
    {
        var session = _session;
        var pool = _pool;
        _session = null;
        _pool = null;
        Interlocked.Increment(ref _poolGeneration);
        ReleaseSessionAndPool(session, pool, () => pool?.TryGetNextFrame());
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        _session = _pool.CreateCaptureSession(_item!);
        _session.IsCursorCaptureEnabled = cursor;
        _session.StartCapture();
    }

    /// <summary>
    /// Stops capture, releases frames the pool still holds, then disposes the pool.
    /// A free-threaded pool can still be producing a buffer after the UI thread decides to drop it.
    /// </summary>
    internal static void ReleaseSessionAndPool(IDisposable? session, IDisposable? pool, Func<IDisposable?>? nextFrame)
    {
        try
        {
            try
            {
                session?.Dispose();
            }
            catch (Exception e) when (IsTeardown(e))
            {
                Log.Default.Warn("capture", $"Capture teardown: {e.Message}");
            }
            if (nextFrame is not null)
            {
                for (var i = 0; i < 2; i++)
                {
                    var frame = nextFrame();
                    if (frame is null)
                        break;
                    frame.Dispose();
                }
            }
        }
        finally
        {
            try
            {
                pool?.Dispose();
            }
            catch (Exception e) when (IsTeardown(e))
            {
                Log.Default.Warn("capture", $"Capture teardown: {e.Message}");
            }
        }
    }

    private static bool IsTeardown(Exception e) => e is COMException or SharpGen.Runtime.SharpGenException;

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
            ReleaseSessionAndPool(session, pool, () => pool?.TryGetNextFrame());
            foreach (var resource in resources)
            {
                try { resource?.Dispose(); }
                catch (Exception e) when (IsTeardown(e))
                {
                    Log.Default.Warn("capture", $"Capture teardown: {e.Message}");
                }
            }
        }
    }
}

/// <summary>The desktop capture and DXGI WinRT bridges. Every ABI pointer has one explicit release.</summary>
internal static unsafe partial class CaptureInterop
{
    public static GraphicsCaptureItem CreateItem(nint window, nint monitor)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var name));
        nint factory = 0;
        nint item = 0;
        try
        {
            var interopId = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, in interopId, out factory));
            var itemId = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            var table = *(nint**)factory;
            var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)table[window != 0 ? 3 : 4];
            Marshal.ThrowExceptionForHR(create(factory, window != 0 ? window : monitor, &itemId, &item));
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            Win32.WindowsDeleteString(name);
        }
    }

    public static IDirect3DDevice CreateDevice(nint dxgi)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var pointer));
        try { return WinRT.MarshalInspectable<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        using var marshaler = WinRT.MarshalInspectable<IDirect3DSurface>.CreateMarshaler(surface);
        var accessId = new Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(marshaler.ThisPtr, in accessId, out var access));
        try
        {
            var textureId = typeof(ID3D11Texture2D).GUID;
            nint texture = 0;
            var get = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3];
            Marshal.ThrowExceptionForHR(get(access, &textureId, &texture));
            return new ID3D11Texture2D(texture);
        }
        finally { Marshal.Release(access); }
    }

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, int length, out nint value);
    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);
}
