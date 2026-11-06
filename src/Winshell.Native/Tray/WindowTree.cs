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
        return [.. Callback.Collect(context => PInvoke.EnumChildWindows((HWND)parent, &Collect, context))];
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Collect(HWND hwnd, LPARAM context)
    {
        return (BOOL)Callback.Collected(context, hwnd);
    }
}
