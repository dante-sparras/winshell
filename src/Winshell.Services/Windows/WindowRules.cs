using Windows.Win32.UI.WindowsAndMessaging;
using Winshell.Apps;
using Winshell.Native;

namespace Winshell.Windows;

/// <summary>Decisions the window list can make without calling Win32, so the rules can be tested on their own.</summary>
internal static class WindowRules
{
    /// <summary>
    /// The window's AppUserModelID, or else its process package's application user model id, or else the program's
    /// file name in lower case without the extension.
    /// </summary>
    public static string AppId(string windowProperty, string packageAppId, string executable)
    {
        if (windowProperty.Length > 0)
            return windowProperty;
        if (packageAppId.Length > 0)
            return packageAppId;
        return executable.Length == 0 ? "" : Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
    }

    /// <summary>The AppUserModelID, or else the program's file name in lower case without the extension.</summary>
    public static string AppId(string appUserModelId, string executable)
    {
        return AppId(appUserModelId, "", executable);
    }

    /// <summary>
    /// A path the icon provider draws. A packaged app's icon is the one Start shows for its id. Every other window
    /// uses its program's icon.
    /// </summary>
    public static string IconPath(string appUserModelId, string executable)
    {
        return AppIdentity.IsPackaged(appUserModelId) ? AppIdentity.AppsFolderPath(appUserModelId) : executable;
    }

    /// <summary>
    /// Whether a WinEvent is about a window itself and not a caret, a cursor, or an object inside one. The caret and
    /// cursor send <c>EVENT_OBJECT_LOCATIONCHANGE</c> on every move.
    /// </summary>
    public static bool IsWindowObject(int idObject, int idChild)
    {
        return idObject == Win32.OBJID_WINDOW && idChild == Win32.CHILDID_SELF;
    }

    /// <summary>Whether a WinEvent names a top-level window the list should read again.</summary>
    public static bool MarksWindow(uint eventType, nint hwnd, int idObject, int idChild, bool tracked, bool isRoot)
    {
        if (eventType == Win32.EVENT_SYSTEM_FOREGROUND)
            return hwnd != 0;
        if (hwnd == 0 || !IsWindowObject(idObject, idChild))
            return false;
        // A destroyed window can no longer say whether it was top-level, so only one the list already had counts.
        return eventType == Win32.EVENT_OBJECT_DESTROY ? tracked : isRoot;
    }

    /// <summary>Whether a window with this style and rectangle is fullscreen on a monitor: no title bar, and covering all of it.</summary>
    public static bool CoversMonitor(long style, RECT window, RECT monitor)
    {
        return (style & (uint)WINDOW_STYLE.WS_CAPTION) != (uint)WINDOW_STYLE.WS_CAPTION
               && window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right &&
               window.Bottom >= monitor.Bottom;
    }

    /// <summary>Whether a WinEvent can move the foreground from one window to another.</summary>
    public static bool MarksForeground(uint eventType)
    {
        return eventType is Win32.EVENT_SYSTEM_FOREGROUND
            or Win32.EVENT_SYSTEM_MINIMIZESTART
            or Win32.EVENT_SYSTEM_MINIMIZEEND
            or Win32.EVENT_OBJECT_DESTROY
            or Win32.EVENT_OBJECT_HIDE;
    }

    /// <summary>Whether a shell hook message is <c>HSHELL_FLASH</c>: a window flashed to ask for attention.</summary>
    public static bool IsFlash(nint code)
    {
        return code == ShellHookWindow.Flash;
    }

    /// <summary>
    /// Keeps the windows that want attention current, as the taskbar does: a window wants it from when it flashes
    /// until it is activated or leaves the list.
    /// </summary>
    public static void SettleAttention(HashSet<nint> wanting, nint active, Func<nint, bool> listed)
    {
        wanting.RemoveWhere(hwnd => hwnd == active || !listed(hwnd));
    }
}
