using Winshell.Platform.Windows.Native;

namespace Winshell.Platform.Windows.Services.WindowList;

/// <summary>The facts the taskbar uses to decide that a window gets a button.</summary>
internal readonly record struct TaskbarWindowFacts(
    bool Exists,
    bool Visible,
    bool Shell,
    bool Child,
    bool AppWindow,
    bool Owned,
    bool ToolWindow,
    bool NoActivate,
    bool HasTitle,
    bool Cloaked);

/// <summary>A monitor's work area in physical pixels, and the scale that turns those pixels into logical ones.</summary>
internal readonly record struct MonitorFrame(int Left, int Top, int Right, int Bottom, double Scale);

/// <summary>Decisions the window list can make without calling Win32, so the rules can be tested on their own.</summary>
internal static class WindowRules
{
    /// <summary>
    /// The taskbar's rule: a visible, uncloaked top-level window with a title that either asks for a taskbar button
    /// or has no owner and is not a tool window or a window that never activates.
    /// </summary>
    public static bool ShowsOnTaskbar(TaskbarWindowFacts window) =>
        window.Exists && window.Visible && !window.Shell && !window.Child
        && (window.AppWindow || (!window.Owned && !window.ToolWindow && !window.NoActivate))
        && window.HasTitle && !window.Cloaked;

    /// <summary>The AppUserModelID, or else the program's file name in lower case without the extension.</summary>
    public static string AppId(string appUserModelId, string executable)
    {
        if (appUserModelId.Length > 0)
            return appUserModelId;
        return executable.Length == 0 ? "" : Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
    }

    /// <summary>
    /// A path the icon provider draws. A packaged app's id contains <c>!</c>, and its icon is the one Start shows
    /// for that id. Every other window uses its program's icon.
    /// </summary>
    public static string IconPath(string appUserModelId, string executable) =>
        appUserModelId.Contains('!') ? @"shell:AppsFolder\" + appUserModelId : executable;

    /// <summary>A minimized or maximized window moves by its restored rectangle. A normal window moves by its current one.</summary>
    public static RECT RectangleToMove(bool minimized, bool maximized, RECT current, RECT restored)
    {
        if ((minimized || maximized) && restored.Width > 0 && restored.Height > 0)
            return restored;
        return current;
    }

    /// <summary>
    /// Where a window goes on another monitor: the same logical offset and size, in the target's physical pixels.
    /// A window that would hang off the target is slid back, and one that is larger is shrunk to fit. The size is
    /// rounded on its own, so it does not depend on where the window sits.
    /// </summary>
    public static RECT MoveOnto(RECT window, MonitorFrame from, MonitorFrame to)
    {
        if (from.Scale <= 0 || to.Scale <= 0 || from.Right <= from.Left || from.Bottom <= from.Top)
            return window;
        var width = (window.Right - window.Left) / from.Scale * to.Scale;
        var height = (window.Bottom - window.Top) / from.Scale * to.Scale;
        var left = to.Left + (window.Left - from.Left) / from.Scale * to.Scale;
        var top = to.Top + (window.Top - from.Top) / from.Scale * to.Scale;
        var targetWidth = to.Right - to.Left;
        var targetHeight = to.Bottom - to.Top;
        if (width > targetWidth)
            width = targetWidth;
        if (height > targetHeight)
            height = targetHeight;
        if (width < 1)
            width = 1;
        if (height < 1)
            height = 1;
        if (left < to.Left)
            left = to.Left;
        if (top < to.Top)
            top = to.Top;
        if (left + width > to.Right)
            left = to.Right - width;
        if (top + height > to.Bottom)
            top = to.Bottom - height;
        var x = (int)Math.Round(left);
        var y = (int)Math.Round(top);
        var w = Math.Max(1, (int)Math.Round(width));
        var h = Math.Max(1, (int)Math.Round(height));
        return new RECT(x, y, x + w, y + h);
    }

    /// <summary>Whether a WinEvent names a top-level window the list should read again.</summary>
    public static bool MarksWindow(uint eventType, nint hwnd, int idObject, int idChild, bool tracked, bool isRoot)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
            return hwnd != 0;
        if (hwnd == 0 || idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
            return false;
        // A destroyed window can no longer say whether it was top-level, so only one the list already had counts.
        return eventType == NativeMethods.EVENT_OBJECT_DESTROY ? tracked : isRoot;
    }

    /// <summary>Whether a WinEvent can move the foreground from one window to another.</summary>
    public static bool MarksForeground(uint eventType) =>
        eventType is NativeMethods.EVENT_SYSTEM_FOREGROUND
            or NativeMethods.EVENT_SYSTEM_MINIMIZESTART
            or NativeMethods.EVENT_SYSTEM_MINIMIZEEND
            or NativeMethods.EVENT_OBJECT_DESTROY
            or NativeMethods.EVENT_OBJECT_HIDE;

    /// <summary>Whether a shell hook message is <c>HSHELL_FLASH</c>: a window flashed to ask for attention.</summary>
    public static bool IsFlash(nint code) => code == (NativeMethods.HSHELL_REDRAW | NativeMethods.HSHELL_HIGHBIT);

    /// <summary>
    /// Keeps the windows that want attention current, as the taskbar does: a window wants it from when it flashes
    /// until it is activated or leaves the list. Returns whether the set changed.
    /// </summary>
    public static bool SettleAttention(HashSet<nint> wanting, nint active, Func<nint, bool> listed) =>
        wanting.RemoveWhere(hwnd => hwnd == active || !listed(hwnd)) > 0;
}
