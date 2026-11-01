using Microsoft.Win32;
using Winshell.Native;

namespace Winshell;

/// <summary>What the shell redoes after a system change.</summary>
[Flags]
internal enum SystemChange
{
    None = 0,
    Theme = 1,
    Text = 2,
    Screens = 4,
    WorkArea = 8,
}

/// <summary>
/// Windows delivers one settings broadcast to every top-level window, and <see cref="SystemEvents"/> reports it too.
/// The copies add up here until the event loop follows them, once.
/// </summary>
internal sealed class SystemChanges
{
    private readonly object _gate = new();
    private SystemChange _pending;

    public static SystemChange From(uint message, nint wParam) => message switch
    {
        Win32.WM_DISPLAYCHANGE => SystemChange.Screens,
        // The taskbar moved, resized, or started or stopped hiding.
        Win32.WM_SETTINGCHANGE when wParam == Win32.SPI_SETWORKAREA => SystemChange.Text | SystemChange.WorkArea | SystemChange.Theme,
        Win32.WM_SETTINGCHANGE => SystemChange.Text | SystemChange.Screens | SystemChange.Theme,
        _ => SystemChange.Theme,
    };

    public static SystemChange From(UserPreferenceCategory category) =>
        category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility
            ? SystemChange.Theme
            : SystemChange.None;

    /// <summary>Adds a change. True when nothing was pending, so the caller has to post a <see cref="Take"/>.</summary>
    public bool Add(SystemChange change)
    {
        lock (_gate)
        {
            var first = _pending == SystemChange.None;
            _pending |= change;
            return first && change != SystemChange.None;
        }
    }

    public SystemChange Take()
    {
        lock (_gate)
        {
            var pending = _pending;
            _pending = SystemChange.None;
            return pending;
        }
    }
}
