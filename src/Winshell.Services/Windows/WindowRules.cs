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
    /// A path the icon provider draws. A packaged app's id contains <c>!</c>, and its icon is the one Start shows
    /// for that id. Every other window uses its program's icon.
    /// </summary>
    public static string IconPath(string appUserModelId, string executable)
    {
        return appUserModelId.Contains('!') ? @"shell:AppsFolder\" + appUserModelId : executable;
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
        return (style & Win32.WS_CAPTION) != Win32.WS_CAPTION
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

    /// <summary>
    /// The part of a window a preview shows, relative to the window's corner: its visible frame, without the
    /// invisible borders Windows puts around it for resizing. The whole window when the frame is unknown.
    /// </summary>
    public static RECT PreviewSource(RECT window, RECT frame)
    {
        var whole = new RECT(0, 0, Math.Max(0, window.Width), Math.Max(0, window.Height));
        if (frame.Width <= 0 || frame.Height <= 0 || window.Width <= 0 || window.Height <= 0)
            return whole;
        var left = Math.Clamp(frame.Left - window.Left, 0, window.Width);
        var top = Math.Clamp(frame.Top - window.Top, 0, window.Height);
        var right = Math.Clamp(frame.Right - window.Left, left, window.Width);
        var bottom = Math.Clamp(frame.Bottom - window.Top, top, window.Height);
        return right - left <= 0 || bottom - top <= 0 ? whole : new RECT(left, top, right, bottom);
    }

    /// <summary>
    /// Where a preview of a source this many pixels in size goes in a box: as large as fits without being enlarged,
    /// keeping its shape, and centered. Empty when either has no area.
    /// </summary>
    public static RECT FitPreview(RECT box, int sourceWidth, int sourceHeight)
    {
        if (box.Width <= 0 || box.Height <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
            return default;
        var scale = Math.Min(1, Math.Min((double)box.Width / sourceWidth, (double)box.Height / sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var left = box.Left + (box.Width - width) / 2;
        var top = box.Top + (box.Height - height) / 2;
        return new RECT(left, top, left + width, top + height);
    }
}
