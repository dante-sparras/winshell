using SkiaSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;

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
/// What a window shows: a premultiplied-alpha swap chain in a DirectComposition visual. Frames are drawn with
/// Skia into memory and copied to the swap chain's back buffer.
/// </summary>
internal sealed class CompositionSurface : IDisposable
{
    private readonly GraphicsDevice _gpu = GraphicsDevice.Shared;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual _visual;
    private IDXGISwapChain1? _swapChain;
    private SKBitmap? _bitmap;
    private SKSurface? _canvas;
    private int _width;
    private int _height;

    public CompositionSurface(nint hwnd)
    {
        _gpu.Composition.CreateTargetForHwnd(hwnd, true, out _target).CheckError();
        _visual = _gpu.Composition.CreateVisual();
        _target.SetRoot(_visual);
    }

    /// <summary>Draws a frame of the given size in physical pixels.</summary>
    public void Render(int width, int height, Action<SKCanvas> paint)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var created = _swapChain is null;
        if (_swapChain is null)
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
                AlphaMode = AlphaMode.Premultiplied,
                Scaling = Scaling.Stretch,
            };
            _swapChain = _gpu.Factory.CreateSwapChainForComposition(_gpu.Device, desc);
            _width = width;
            _height = height;
        }
        else if (width != _width || height != _height)
        {
            _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
            _width = width;
            _height = height;
        }
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
        using var buffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        // The scene reports no dirty rectangle, so each frame uploads the whole bitmap.
        // Present(0) does not wait on vblank: an occluded or hidden window still draws, and Present(1) would stall it.
        // The loop paces a requested frame with DwmFlush instead.
        _gpu.Context.UpdateSubresource(buffer, 0, null, _bitmap.GetPixels(), (uint)_bitmap.RowBytes, 0);
        _swapChain.Present(0, PresentFlags.None);
        if (created)
        {
            // The visual takes a new swap chain once it holds a frame, so a window shown next never shows it empty.
            _visual.SetContent(_swapChain);
            _gpu.Composition.Commit();
        }
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
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public void Dispose()
    {
        _canvas?.Dispose();
        _bitmap?.Dispose();
        _swapChain?.Dispose();
        _visual.Dispose();
        _target.Dispose();
        _gpu.Composition.Commit();
    }
}
