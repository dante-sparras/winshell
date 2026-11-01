using SharpGen.Runtime;
using SkiaSharp;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows;

/// <summary>The Direct3D device, DXGI factory and DirectComposition device every window shares.</summary>
internal sealed class GraphicsDevice : IDisposable
{
    private static GraphicsDevice? _shared;

    private GraphicsDevice()
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        var result = D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out ID3D11Device? device, out ID3D11DeviceContext? context);
        if (result.Failure)
        {
            // No usable GPU, as in a remote session without one: draw with WARP.
            D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out device, out context).CheckError();
        }
        Device = device!;
        Context = context!;
        DxgiDevice = Device.QueryInterface<IDXGIDevice>();
        using var adapter = DxgiDevice.GetAdapter();
        Factory = adapter.GetParent<IDXGIFactory2>();
        Composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(DxgiDevice);
    }

    public static GraphicsDevice Shared => _shared ??= new GraphicsDevice();

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    public IDXGIDevice DxgiDevice { get; }

    public IDXGIFactory2 Factory { get; }

    public IDCompositionDevice Composition { get; }

    public void Dispose()
    {
        Composition.Dispose();
        Factory.Dispose();
        DxgiDevice.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}

/// <summary>
/// What a window shows: a swap chain in a DirectComposition visual. Frames are drawn with Skia into memory and
/// copied to the swap chain's back buffer. The chain is premultiplied unless the window asked for an opaque one.
/// </summary>
internal sealed class CompositionSurface : IDisposable
{
    private readonly GraphicsDevice _gpu = GraphicsDevice.Shared;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual _visual;
    private AlphaMode _alpha;
    private IDCompositionEffectGroup? _fade;
    private IDXGISwapChain1? _swapChain;
    private SKBitmap? _bitmap;
    private SKSurface? _canvas;
    private int _width;
    private int _height;

    public CompositionSurface(nint hwnd, bool? opaque)
    {
        _alpha = AlphaModeFor(opaque);
        IDCompositionTarget? target = null;
        IDCompositionVisual? visual = null;
        try
        {
            _gpu.Composition.CreateTargetForHwnd(hwnd, true, out target).CheckError();
            visual = _gpu.Composition.CreateVisual();
            target.SetRoot(visual);
            _target = target;
            _visual = visual;
        }
        catch (Exception e)
        {
            visual?.Dispose();
            target?.Dispose();
            if (e is SharpGenException)
                throw new SurfaceResourcesException("The composition surface could not be created.", e);
            throw;
        }
    }

    /// <summary>
    /// <c>DXGI_ALPHA_MODE_IGNORE</c> (3) when the window asked to be opaque, and premultiplied (1) when it asked
    /// not to be or did not say. An unset format stays premultiplied, which is the chain every window has used.
    /// </summary>
    internal static int DxgiAlphaMode(bool? opaque) => (int)AlphaModeFor(opaque);

    private static AlphaMode AlphaModeFor(bool? opaque) =>
        opaque == true ? AlphaMode.Ignore : AlphaMode.Premultiplied;

    /// <summary>
    /// Uses the window's surface format for the next swap chain. A chain that is already showing is dropped when
    /// the mode changes, and the next frame makes it again. Unset and an explicit false are both premultiplied.
    /// </summary>
    public void ApplyFormat(bool? opaque)
    {
        var mode = AlphaModeFor(opaque);
        if (mode == _alpha)
            return;
        _alpha = mode;
        if (_swapChain is not null)
            Release();
    }

    /// <summary>Draws a frame of the given size in physical pixels.</summary>
    public void Render(int width, int height, Action<SKCanvas> paint)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var created = _swapChain is null;
        EnsureSwapChain(width, height);
        if (_bitmap is null || _canvas is null || _bitmap.Width != width || _bitmap.Height != height)
        {
            _canvas?.Dispose();
            _canvas = null;
            _bitmap?.Dispose();
            _bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            _canvas = SKSurface.Create(_bitmap.Info, _bitmap.GetPixels(), _bitmap.RowBytes)
                ?? throw new InvalidOperationException("Skia could not draw into the window bitmap.");
        }
        var canvas = _canvas.Canvas;
        canvas.Clear(SKColors.Transparent);
        paint(canvas);
        canvas.Flush();
        CopyFrame();
        if (created)
        {
            // The visual takes a new swap chain once it holds a frame, so a window shown next never shows it empty.
            try
            {
                _visual.SetContent(_swapChain);
                _gpu.Composition.Commit();
            }
            catch (SharpGenException e) when (SurfaceLoss.Is(e.ResultCode))
            {
                throw new SurfaceResourcesException("The composition surface could not show the frame.", e);
            }
        }
    }

    private void EnsureSwapChain(int width, int height)
    {
        if (_swapChain is null)
        {
            try
            {
                var desc = new SwapChainDescription1
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    Format = Format.B8G8R8A8_UNorm,
                    BufferCount = 2,
                    BufferUsage = Usage.RenderTargetOutput,
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = SwapEffect.FlipSequential,
                    AlphaMode = _alpha,
                    Scaling = Scaling.Stretch,
                };
                _swapChain = _gpu.Factory.CreateSwapChainForComposition(_gpu.Device, desc);
                _width = width;
                _height = height;
            }
            catch (SharpGenException e)
            {
                throw new SurfaceResourcesException("The swap chain could not be created.", e);
            }
            return;
        }
        if (width == _width && height == _height)
            return;
        try
        {
            _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
            _width = width;
            _height = height;
        }
        catch (SharpGenException e)
        {
            throw new SurfaceResourcesException("The swap chain could not be resized.", e);
        }
    }

    private void CopyFrame()
    {
        ID3D11Texture2D buffer;
        try
        {
            buffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
        }
        catch (SharpGenException e) when (SurfaceLoss.Is(e.ResultCode))
        {
            throw new SurfaceResourcesException("The swap chain could not provide a frame.", e);
        }
        try
        {
            using (buffer)
            {
                // The scene reports no dirty rectangle, so each frame uploads the whole bitmap.
                _gpu.Context.UpdateSubresource(buffer, 0, null, _bitmap!.GetPixels(), (uint)_bitmap.RowBytes, 0);
            }
        }
        catch (SharpGenException e) when (SurfaceLoss.Is(e.ResultCode))
        {
            throw new SurfaceResourcesException("The frame could not be copied.", e);
        }
        // Present(0) does not wait on vblank: an occluded or hidden window still draws, and Present(1) would stall it.
        // The loop paces a requested frame with DwmFlush instead. Present returns its result and does not throw.
        SurfaceLoss.ThrowIf(_swapChain.Present(0, PresentFlags.None), "The swap chain could not present a frame.");
    }

    /// <summary>
    /// Fades the visual that holds the swap chain. This device's visuals have no opacity of their own, so an effect
    /// group does it, and 1 removes the group, because an effect draws through an intermediate surface.
    /// The frame path commits only when the chain is first created, so a later change commits itself.
    /// </summary>
    public void SetOpacity(float opacity)
    {
        if (opacity == 1f)
        {
            _visual.SetEffect(null).CheckError();
        }
        else
        {
            _fade ??= _gpu.Composition.CreateEffectGroup();
            _fade.SetOpacity(opacity).CheckError();
            _visual.SetEffect(_fade).CheckError();
        }
        _gpu.Composition.Commit();
    }

    /// <summary>Clips the visual to one rectangle in physical pixels.</summary>
    public void SetClip(float left, float top, float right, float bottom)
    {
        _visual.SetClip(new RawRectF(left, top, right, bottom)).CheckError();
        _gpu.Composition.Commit();
    }

    /// <summary>Removes the clip. A null clip is the default and is slightly cheaper than a rectangle.</summary>
    public void ClearClip()
    {
        _visual.SetClip((IDCompositionClip?)null).CheckError();
        _gpu.Composition.Commit();
    }

    /// <summary>Lets go of the swap chain and the bitmap, as for a hidden window. The next frame makes them again.</summary>
    public void Release()
    {
        if (_swapChain is null)
            return;
        _visual.SetContent(null);
        _gpu.Composition.Commit();
        _swapChain.Dispose();
        _swapChain = null;
        // The surface draws into the bitmap's pixels, so it goes first.
        _canvas?.Dispose();
        _canvas = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public void Dispose()
    {
        _canvas?.Dispose();
        _bitmap?.Dispose();
        _swapChain?.Dispose();
        _fade?.Dispose();
        _visual.Dispose();
        _target.Dispose();
        _gpu.Composition.Commit();
    }
}

/// <summary>
/// Whether a DXGI result means the window's device is gone. Creating or resizing a surface fails on any error.
/// Copying or presenting a frame fails only for a removed device, a reset device, or an out-of-memory result.
/// </summary>
internal static class SurfaceLoss
{
    internal static bool Is(Result result) => IsDeviceLoss(result.Code);

    /// <summary>
    /// <paramref name="code"/> is <c>DXGI_ERROR_DEVICE_REMOVED</c>, <c>DXGI_ERROR_DEVICE_RESET</c>, or <c>E_OUTOFMEMORY</c>.
    /// </summary>
    internal static bool IsDeviceLoss(int code) =>
        code == Vortice.DXGI.ResultCode.DeviceRemoved.Code
        || code == Vortice.DXGI.ResultCode.DeviceReset.Code
        || code == Result.OutOfMemory.Code;

    internal static void ThrowIf(Result result, string message)
    {
        if (Is(result))
            throw new SurfaceResourcesException(message, new SharpGenException(result, result.ToString(), null));
    }
}
