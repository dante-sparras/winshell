using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.WinRT.Direct3D11;
using Windows.Win32.System.WinRT.Graphics.Capture;
using WinRT;
using IInspectable = Windows.Win32.System.WinRT.IInspectable;

namespace Winshell.Native;

/// <summary>The desktop capture and DXGI WinRT bridges. Every ABI pointer has one explicit release.</summary>
internal static unsafe class CaptureInterop
{
    private static readonly Guid GraphicsCaptureItemId = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");

    /// <summary>A capture item for <paramref name="window"/>, or for <paramref name="monitor"/> when the window is zero.</summary>
    public static GraphicsCaptureItem CreateItem(nint window, nint monitor)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem", typeof(IGraphicsCaptureItemInterop).GUID);
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

    /// <summary>A WinRT Direct3D device over <paramref name="dxgiDevice"/>, an IDXGIDevice the caller keeps owning.</summary>
    public static IDirect3DDevice CreateDevice(nint dxgiDevice)
    {
        IInspectable* device = null;
        PInvoke.CreateDirect3D11DeviceFromDXGIDevice((IDXGIDevice*)dxgiDevice, &device).ThrowOnFailure();
        try { return MarshalInspectable<IDirect3DDevice>.FromAbi((nint)device); }
        finally { Marshal.Release((nint)device); }
    }

    /// <summary>
    /// The DXGI interface <paramref name="iid"/> behind a WinRT surface, such as its texture. The caller owns the
    /// returned reference and releases it once.
    /// </summary>
    public static nint GetDxgiInterface(IDirect3DSurface surface, Guid iid)
    {
        using var marshaler = MarshalInspectable<IDirect3DSurface>.CreateMarshaler(surface);
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(marshaler.ThisPtr, typeof(IDirect3DDxgiInterfaceAccess).GUID, out var pointer));
        var access = (IDirect3DDxgiInterfaceAccess*)pointer;
        try
        {
            void* result = null;
            access->GetInterface(&iid, &result);
            return (nint)result;
        }
        finally { access->Release(); }
    }

    /// <summary>
    /// Copies <paramref name="height"/> rows of <paramref name="width"/> 32-bit pixels from mapped memory at
    /// <paramref name="source"/> into <paramref name="destination"/>, stepping each by its own row pitch.
    /// </summary>
    public static void CopyRows(nint source, int sourcePitch, Span<byte> destination, int destinationPitch, int width, int height)
    {
        var bytes = checked(width * 4);
        if (width <= 0 || height <= 0 || sourcePitch < bytes || destinationPitch < bytes
            || destination.Length < (long)(height - 1) * destinationPitch + bytes)
            throw new ArgumentOutOfRangeException(nameof(width));
        for (var row = 0; row < height; row++)
            new ReadOnlySpan<byte>((byte*)source + (long)row * sourcePitch, bytes).CopyTo(destination.Slice(row * destinationPitch, bytes));
    }
}
