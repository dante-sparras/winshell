using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.WinRT.Graphics.Capture;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;
using IInspectable = Windows.Win32.System.WinRT.IInspectable;
using DxgiInterfaceAccess = Windows.Win32.System.WinRT.Direct3D11.IDirect3DDxgiInterfaceAccess;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace Winshell.Native;

/// <summary>The desktop capture, DXGI and Direct3D readback bridges. Every ABI pointer has one explicit release.</summary>
internal static unsafe class CaptureInterop
{
    private static readonly Guid GraphicsCaptureItemId = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private const int DXGI_ERROR_WAS_STILL_DRAWING = unchecked((int)0x887a000a);

    /// <summary>A capture item for <paramref name="window"/>, or for <paramref name="monitor"/> when the window is zero.</summary>
    public static GraphicsCaptureItem CreateItem(nint window, nint monitor)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem",
            typeof(IGraphicsCaptureItemInterop).GUID);
        var interop = (IGraphicsCaptureItemInterop*)factory.ThisPtr;
        var itemId = GraphicsCaptureItemId;
        void* item = null;
        try
        {
            if (window != 0)
                interop->CreateForWindow(new HWND((void*)window), &itemId, &item);
            else
                interop->CreateForMonitor(new HMONITOR(monitor), &itemId, &item);
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi((nint)item);
        }
        finally
        {
            if (item is not null)
                Marshal.Release((nint)item);
        }
    }

    /// <summary>A WinRT Direct3D device over <paramref name="device"/>, which the caller keeps owning.</summary>
    public static IDirect3DDevice CreateDevice(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        IInspectable* winrt = null;
        PInvoke.CreateDirect3D11DeviceFromDXGIDevice((Windows.Win32.Graphics.Dxgi.IDXGIDevice*)dxgi.NativePointer,
            &winrt).ThrowOnFailure();
        try
        {
            return MarshalInspectable<IDirect3DDevice>.FromAbi((nint)winrt);
        }
        finally
        {
            Marshal.Release((nint)winrt);
        }
    }

    /// <summary>The texture behind a capture frame's surface. The caller disposes it.</summary>
    public static ID3D11Texture2D Texture(IDirect3DSurface surface)
    {
        using var marshaler = MarshalInspectable<IDirect3DSurface>.CreateMarshaler(surface);
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(marshaler.ThisPtr, typeof(DxgiInterfaceAccess).GUID,
            out var pointer));
        var access = (DxgiInterfaceAccess*)pointer;
        try
        {
            var iid = typeof(ID3D11Texture2D).GUID;
            void* texture = null;
            access->GetInterface(&iid, &texture);
            return new ID3D11Texture2D((nint)texture);
        }
        finally
        {
            access->Release();
        }
    }

    /// <summary>
    /// The <paramref name="width"/> by <paramref name="height"/> 32-bit pixels of <paramref name="staging"/>, a CPU-readable
    /// texture, or null while the GPU is still writing it. Never waits for the GPU.
    /// </summary>
    public static byte[]? Read(ID3D11DeviceContext context, ID3D11Texture2D staging, int width, int height)
    {
        var result = context.Map(staging, 0, MapMode.Read, MapFlags.DoNotWait, out var mapped);
        if (result.Code == DXGI_ERROR_WAS_STILL_DRAWING)
            return null;
        result.CheckError();
        try
        {
            var pixels = new byte[checked(width * height * 4)];
            CopyRows(mapped.DataPointer, checked((int)mapped.RowPitch), pixels, width * 4, width, height);
            return pixels;
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }

    /// <summary>
    /// Copies <paramref name="height"/> rows of <paramref name="width"/> 32-bit pixels from mapped memory at
    /// <paramref name="source"/> into <paramref name="destination"/>, stepping each by its own row pitch.
    /// </summary>
    public static void CopyRows(nint source, int sourcePitch, Span<byte> destination, int destinationPitch, int width,
        int height)
    {
        var bytes = checked(width * 4);
        if (width <= 0 || height <= 0 || sourcePitch < bytes || destinationPitch < bytes
            || destination.Length < (long)(height - 1) * destinationPitch + bytes)
            throw new ArgumentOutOfRangeException(nameof(width));
        for (var row = 0; row < height; row++)
            new ReadOnlySpan<byte>((byte*)source + (long)row * sourcePitch, bytes).CopyTo(
                destination.Slice(row * destinationPitch, bytes));
    }
}
