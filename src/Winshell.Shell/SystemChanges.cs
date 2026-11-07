using Winshell.Native;

namespace Winshell;

/// <summary>What the shell redoes after a system change.</summary>
[Flags]
internal enum SystemChange
{
    None = 0,
    Theme = 1,
    Screens = 2,
    WorkArea = 4
}

/// <summary>
/// One change in Windows can send several broadcasts, such as a setting change, a theme change and a color change.
/// They add up here until the event loop follows them, once.
/// </summary>
internal sealed class SystemChanges
{
    private SystemChange _pending;

    /// <summary>What a broadcast asks the shell to redo. None for any other message.</summary>
    public static SystemChange From(uint message, nint wParam)
    {
        return message switch
        {
            Win32.WM_DISPLAYCHANGE => SystemChange.Screens,
            // The taskbar moved, resized, or started or stopped hiding.
            Win32.WM_SETTINGCHANGE when wParam == Win32.SPI_SETWORKAREA => SystemChange.WorkArea | SystemChange.Theme,
            Win32.WM_SETTINGCHANGE => SystemChange.Screens | SystemChange.Theme,
            Win32.WM_DWMCOLORIZATIONCOLORCHANGED or Win32.WM_SYSCOLORCHANGE or Win32.WM_THEMECHANGED =>
                SystemChange.Theme,
            _ => SystemChange.None
        };
    }

    /// <summary>Adds a change. True when nothing was pending, so the caller has to post a <see cref="Take"/>.</summary>
    public bool Add(SystemChange change)
    {
        var first = _pending == SystemChange.None;
        _pending |= change;
        return first && change != SystemChange.None;
    }

    public SystemChange Take()
    {
        var pending = _pending;
        _pending = SystemChange.None;
        return pending;
    }
}
