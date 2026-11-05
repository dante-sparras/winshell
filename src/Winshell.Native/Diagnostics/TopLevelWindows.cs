using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>The top-level windows on this desktop, and their class names.</summary>
internal static unsafe class TopLevelWindows
{
    private const int ClassNameCapacity = 256;

    /// <summary>Every top-level window, in z-order from the top.</summary>
    internal static List<nint> List()
    {
        var windows = new List<nint>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            Win32.EnumWindows(&Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return windows;
    }

    /// <summary>The window's class name, or empty when it cannot be read.</summary>
    internal static string ClassName(nint hwnd)
    {
        var name = stackalloc char[ClassNameCapacity];
        var length = Win32.GetClassNameW(hwnd, name, ClassNameCapacity);
        return new string(name, 0, Math.Max(0, length));
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint windows)
    {
        try
        {
            ((List<nint>)GCHandle.FromIntPtr(windows).Target!).Add(hwnd);
            return 1;
        }
        catch (OutOfMemoryException)
        {
            // An exception must not reach Windows. Stop with the windows listed so far.
            return 0;
        }
    }
}
