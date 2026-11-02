namespace Winshell.DefaultShell.Taskbar;

/// <summary>What a row of the Win+X menu does.</summary>
public enum PowerCommandKind
{
    Url,
    Explorer,
    Search,
    Desktop,
    Settings,
}

/// <summary>One row of the Win+X menu. It does not shut down or open Task Manager.</summary>
public sealed record PowerCommand(string Name, string Url, PowerCommandKind Kind);

/// <summary>Where a Win+X row is carried out. The switcher decides which one; this does it.</summary>
public interface IDesktopCommandTarget
{
    void OpenUrl(string url);

    void OpenExplorer();

    void OpenSearch();

    void OpenSettings();
}

/// <summary>
/// Alt+Tab, Win+Tab, Win+D and Win+X. The cards follow window-list order, the order windows opened. Letting go of
/// Alt does not activate a window: the chord is already up when Tab comes up. Win+D draws nothing.
/// </summary>
public sealed class DesktopSwitcher
{
    private readonly List<IOpenWindow> _parked = [];

    public static IReadOnlyList<PowerCommand> Commands { get; } =
    [
        new("Installed apps", "ms-settings:appsfeatures", PowerCommandKind.Url),
        new("System", "ms-settings:system", PowerCommandKind.Url),
        new("Network", "ms-settings:network", PowerCommandKind.Url),
        new("Settings", "ms-settings:", PowerCommandKind.Url),
        new("File Explorer", "", PowerCommandKind.Explorer),
        new("Search", "", PowerCommandKind.Search),
        new("Desktop", "", PowerCommandKind.Desktop),
        new("Taskbar settings", "", PowerCommandKind.Settings),
    ];

    public bool SwitcherOpen { get; private set; }

    public bool PowerOpen { get; private set; }

    public int Selected { get; private set; }

    /// <summary>The windows the last Win+D minimized, so the next one can put them back.</summary>
    public IReadOnlyList<IOpenWindow> Parked => _parked;

    public event EventHandler? Changed;

    /// <summary>The window the switcher would activate. Null when nothing is open.</summary>
    public IOpenWindow? Chosen(IReadOnlyList<IOpenWindow> windows) =>
        windows.Count == 0 ? null : windows[Mod(Selected, windows.Count)];

    /// <summary>Alt+Tab moves forward and Alt+Shift+Tab moves back. The first press leaves the window in front.</summary>
    public void Cycle(int step, IReadOnlyList<IOpenWindow> windows, IOpenWindow? active)
    {
        if (windows.Count == 0)
            Selected = 0;
        else if (!SwitcherOpen)
        {
            var start = IndexOf(windows, active);
            Selected = start < 0 ? (step > 0 ? 0 : windows.Count - 1) : Mod(start + step, windows.Count);
        }
        else
            Selected = Mod(Selected + step, windows.Count);
        OpenSwitcher();
    }

    /// <summary>Win+Tab opens on the window in front, or closes the switcher.</summary>
    public void ToggleTaskView(IReadOnlyList<IOpenWindow> windows, IOpenWindow? active)
    {
        if (SwitcherOpen)
        {
            SwitcherOpen = false;
            Touch();
            return;
        }
        var start = IndexOf(windows, active);
        Selected = start >= 0 ? start : 0;
        OpenSwitcher();
    }

    public void CloseSwitcher()
    {
        if (!SwitcherOpen)
            return;
        SwitcherOpen = false;
        Touch();
    }

    /// <summary>Enter, Space or a click. The switcher closes and that window comes forward.</summary>
    public void Choose(IOpenWindow? window)
    {
        window?.Activate();
        SwitcherOpen = false;
        Touch();
    }

    /// <summary>
    /// The first press minimizes every window that is open and remembers those. The next press restores them.
    /// A window that has since closed is skipped.
    /// </summary>
    public void ShowDesktop(IReadOnlyList<IOpenWindow> windows)
    {
        SwitcherOpen = false;
        PowerOpen = false;
        var open = new List<IOpenWindow>();
        foreach (var window in windows)
        {
            if (!window.Minimized)
                open.Add(window);
        }
        if (open.Count > 0)
        {
            _parked.Clear();
            _parked.AddRange(open);
            foreach (var window in open)
                window.Minimize();
            Touch();
            return;
        }
        var still = new List<IOpenWindow>();
        foreach (var parked in _parked)
        {
            if (IndexOf(windows, parked) >= 0)
                still.Add(parked);
        }
        foreach (var window in still)
        {
            if (window.Minimized)
                window.Restore();
        }
        _parked.Clear();
        Touch();
    }

    /// <summary>Win+X. With no taskbar to anchor to, the menu stays closed.</summary>
    public void TogglePower(bool hasTaskbar)
    {
        SwitcherOpen = false;
        if (PowerOpen || !hasTaskbar)
        {
            PowerOpen = false;
            Touch();
            return;
        }
        PowerOpen = true;
        Touch();
    }

    public void ClosePower()
    {
        if (!PowerOpen)
            return;
        PowerOpen = false;
        Touch();
    }

    /// <summary>A row of the menu. The menu closes, then the row runs. Desktop minimizes, as Win+D does.</summary>
    public void Run(PowerCommand command, IDesktopCommandTarget target, IReadOnlyList<IOpenWindow> windows)
    {
        PowerOpen = false;
        Touch();
        switch (command.Kind)
        {
            case PowerCommandKind.Url:
                target.OpenUrl(command.Url);
                break;
            case PowerCommandKind.Explorer:
                target.OpenExplorer();
                break;
            case PowerCommandKind.Search:
                target.OpenSearch();
                break;
            case PowerCommandKind.Desktop:
                ShowDesktop(windows);
                break;
            case PowerCommandKind.Settings:
                target.OpenSettings();
                break;
        }
    }

    public void CloseAll()
    {
        if (!SwitcherOpen && !PowerOpen)
            return;
        SwitcherOpen = false;
        PowerOpen = false;
        Touch();
    }

    private void OpenSwitcher()
    {
        PowerOpen = false;
        SwitcherOpen = true;
        Touch();
    }

    private void Touch() => Changed?.Invoke(this, EventArgs.Empty);

    private static int IndexOf(IReadOnlyList<IOpenWindow> windows, IOpenWindow? window)
    {
        if (window is null)
            return -1;
        for (var i = 0; i < windows.Count; i++)
        {
            if (Equals(windows[i], window))
                return i;
        }
        return -1;
    }

    private static int Mod(int value, int count) => (value % count + count) % count;
}

/// <summary>Where the switcher and the Win+X menu sit above the main taskbar.</summary>
public static class DesktopChordLayout
{
    /// <summary>The desktop row is drawn when virtual desktops are supported and there is one to show.</summary>
    public static bool ShowDesktops(bool supported, int count) => supported && count > 0;

    /// <summary>Centered above the taskbar, one gap up. The width stops short of the screen edges.</summary>
    public static QRect Switcher(QRect bar, double screenWidth, int windows, bool desktops)
    {
        var room = Math.Max(200, screenWidth - 48);
        var cards = windows == 0 ? 200 : Math.Min(Math.Max(windows * 208 - 8, 1), room);
        var height = 16 + (windows == 0 ? 24 : 140);
        if (desktops)
            height += 40;
        var width = cards + 16;
        var x = bar.X + Math.Max(0, (bar.Width - width) / 2);
        return new QRect(x, bar.Y - height - 12, width, height);
    }

    /// <summary>The top left of the taskbar, opening upward.</summary>
    public static QRect Power(QRect bar, int commands)
    {
        var height = 8 + Math.Max(commands, 1) * 36;
        return new QRect(bar.X, bar.Y - height - 8, 280, height);
    }
}
