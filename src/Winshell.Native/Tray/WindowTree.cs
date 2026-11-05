using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

internal static unsafe class WindowTree
{
    /// <summary>The desktop window, which top-level z-order changes name.</summary>
    internal static nint Desktop => PInvoke.GetDesktopWindow();

    /// <summary>
    /// Every window below <paramref name="parent"/>, its children's children too, each once, from a list Windows copies
    /// at once.
    /// </summary>
    internal static HashSet<nint> Descendants(nint parent)
    {
        var windows = new HashSet<nint>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            PInvoke.EnumChildWindows((HWND)parent, &Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return windows;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Collect(HWND hwnd, LPARAM data)
    {
        try
        {
            ((HashSet<nint>)GCHandle.FromIntPtr(data).Target!).Add(hwnd);
            return true;
        }
#pragma warning disable CA1031 // Windows called in; nothing above this frame can catch it.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
