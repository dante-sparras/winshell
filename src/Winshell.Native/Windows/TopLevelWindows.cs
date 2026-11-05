using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winshell.Native;

/// <summary>
/// Reads and asks things of top-level windows, including other programs'. Each request that changes a window is
/// posted without waiting, so a hung program cannot hold up the caller.
/// </summary>
internal static unsafe class TopLevelWindows
{
    /// <summary>The longest title read, in characters, so a program cannot make the caller allocate without bound.</summary>
    private const int MaxTitle = 4096;

    private const int ClassNameCapacity = 256;

    [ThreadStatic]
    private static List<nint>? _found;

    /// <summary>Every top-level window, front to back.</summary>
    internal static List<nint> List()
    {
        var found = new List<nint>();
        _found = found;
        try
        {
            Win32.EnumWindows(&Collect, 0);
        }
        finally
        {
            _found = null;
        }
        return found;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint data)
    {
        _found?.Add(hwnd);
        return 1;
    }

    /// <summary>The window's title, cut at <see cref="MaxTitle"/> characters. Empty when it has none.</summary>
    internal static string Title(nint hwnd)
    {
        var length = Math.Min(PInvoke.GetWindowTextLength((HWND)hwnd) + 1, MaxTitle);
        if (length <= 1)
            return "";
        var buffer = stackalloc char[length];
        var got = PInvoke.GetWindowText((HWND)hwnd, buffer, length);
        return new string(buffer, 0, Math.Clamp(got, 0, length - 1));
    }

    internal static bool HasTitle(nint hwnd) => PInvoke.GetWindowTextLength((HWND)hwnd) > 0;

    internal static string ClassName(nint hwnd)
    {
        var buffer = stackalloc char[ClassNameCapacity];
        var got = Win32.GetClassNameW(hwnd, buffer, ClassNameCapacity);
        return new string(buffer, 0, Math.Clamp(got, 0, ClassNameCapacity - 1));
    }

    /// <summary>Whether this is the desktop window Explorer draws the wallpaper and icons in.</summary>
    internal static bool IsShellWindow(nint hwnd) => hwnd == (nint)PInvoke.GetShellWindow();

    /// <summary>Whether DWM hides the window, as it does a window on another virtual desktop or a suspended app.</summary>
    internal static bool IsCloaked(nint hwnd)
    {
        int cloaked;
        return PInvoke.DwmGetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int)).Succeeded && cloaked != 0;
    }

    internal static bool HasChildStyle(nint hwnd) =>
        ((long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE) & (long)WINDOW_STYLE.WS_CHILD) != 0;

    /// <summary>The top-level window that holds this one, or 0.</summary>
    internal static nint RootOf(nint hwnd) => Win32.GetAncestor(hwnd, (uint)GET_ANCESTOR_FLAGS.GA_ROOT);

    /// <summary>The window's visible frame in screen pixels, without the invisible resize borders.</summary>
    internal static bool TryFrameBounds(nint hwnd, out RECT frame) =>
        Win32.DwmGetWindowRect(hwnd, (int)DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out frame, sizeof(RECT)) == 0;

    /// <summary>Asks the window to close, as its close button does.</summary>
    internal static void Close(nint hwnd) => Win32.PostMessageW(hwnd, Win32.WM_SYSCOMMAND, (nint)PInvoke.SC_CLOSE, 0);

    internal static void Restore(nint hwnd) => PInvoke.ShowWindowAsync((HWND)hwnd, SHOW_WINDOW_CMD.SW_RESTORE);

    internal static void Maximize(nint hwnd) => PInvoke.ShowWindowAsync((HWND)hwnd, SHOW_WINDOW_CMD.SW_MAXIMIZE);

    internal static void Minimize(nint hwnd) => PInvoke.ShowWindowAsync((HWND)hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);

    /// <summary>Puts the window at the top of the z-order without activating it.</summary>
    internal static void Raise(nint hwnd) =>
        Win32.SetWindowPos(hwnd, Win32.HWND_TOP, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | (uint)SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    /// <summary>Moves and sizes the window to <paramref name="rect"/>, in screen pixels, without activating it.</summary>
    internal static void Move(nint hwnd, RECT rect) =>
        Win32.SetWindowPos(hwnd, 0, rect.Left, rect.Top, rect.Width, rect.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | (uint)SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    /// <summary>
    /// The window's placement, with its restored rectangle in workspace coordinates. False when Windows does not say.
    /// </summary>
    internal static bool TryPlacement(nint hwnd, out WINDOWPLACEMENT placement)
    {
        placement = new WINDOWPLACEMENT { Length = (uint)sizeof(WINDOWPLACEMENT) };
        return Win32.GetWindowPlacement(hwnd, ref placement);
    }

    /// <summary>
    /// Gives the window the restored rectangle <paramref name="normal"/>, in workspace coordinates, and shows it
    /// minimized, maximized or restored. The rest of <paramref name="placement"/> stays as read.
    /// </summary>
    internal static void Place(nint hwnd, WINDOWPLACEMENT placement, RECT normal, bool minimized, bool maximized)
    {
        placement.Normal = normal;
        placement.ShowCmd = (uint)(minimized ? Win32.SW_SHOWMINIMIZED : maximized ? (int)SHOW_WINDOW_CMD.SW_MAXIMIZE : Win32.SW_SHOWNORMAL);
        placement.Flags |= (uint)WINDOWPLACEMENT_FLAGS.WPF_ASYNCWINDOWPLACEMENT;
        Win32.SetWindowPlacement(hwnd, placement);
    }
}
