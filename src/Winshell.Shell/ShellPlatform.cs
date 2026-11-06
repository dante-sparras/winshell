using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Security;
using Windows.UI;
using Windows.UI.ViewManagement;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Winshell.Native;
using Winshell.Tray;
using Winshell.Windows;

namespace Winshell;

/// <summary>What every native window of one process shares: the screens, the theme, and the clicks that close popups.</summary>
internal sealed class ShellPlatform : IDisposable
{
    private readonly List<HostWindow> _windows = [];
    private readonly SystemChanges _systemChanges = new();
    private readonly List<ScreenInfo> _screens;
    private readonly List<WinEventHook> _foregroundWatches = [];
    private HashSet<string> _fullscreenScreens = [];
    private IDisposable? _relayout;
    private IDisposable? _screenRefresh;
    private UISettings? _uiSettings;
    private bool _windowsDark;
    private ColorScheme _scheme;
    private string _accent = "";
    private bool _accentPainted;

    public ShellPlatform(IEventLoop loop, Log log)
    {
        Loop = loop;
        Log = log;
        _screens = Winshell.Screens.Enumerate().Select(m => m.Info).ToList();
        Screens = new ObservableCollection<ScreenInfo>(_screens);
        ReadTheme();
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        OutsideClicks = new OutsideClicks(this);
        // Explorer tells only the app bars that reserve an edge about a fullscreen app, so this reads it from the foreground window.
        foreach (var evt in new[] { Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_OBJECT_LOCATIONCHANGE })
            if (WinEventHook.Start(evt, OnForegroundEvent, e => Log.Warn("shell", e.Message)) is { } watch)
                _foregroundWatches.Add(watch);
        ReadFullscreen();
    }

    public IEventLoop Loop { get; }

    public Log Log { get; }

    public ObservableCollection<ScreenInfo> Screens { get; }

    internal OutsideClicks OutsideClicks { get; }

    /// <summary>The native windows that exist now.</summary>
    internal IReadOnlyList<HostWindow> Windows => _windows;

    public string PrimaryScreenId =>
        _screens.FirstOrDefault(s => s.IsPrimary)?.Id ?? _screens.FirstOrDefault()?.Id ?? "";

    /// <summary>Whether windows are dark. <see cref="ColorScheme"/> overrides Windows mode, and <see cref="ColorScheme.System"/> follows it.</summary>
    public bool IsDark => DarkFor(_scheme, _windowsDark);

    /// <summary>Whether <paramref name="scheme"/> is dark, with Windows in <paramref name="windowsDark"/>.</summary>
    internal static bool DarkFor(ColorScheme scheme, bool windowsDark)
    {
        return scheme switch
        {
            ColorScheme.Dark => true,
            ColorScheme.Light => false,
            _ => windowsDark
        };
    }

    public bool IsHighContrast { get; private set; }

    /// <summary>The color scheme every window follows.</summary>
    internal ColorScheme ColorScheme
    {
        get => _scheme;
        set
        {
            if (value == _scheme)
                return;
            var wasDark = IsDark;
            _scheme = value;
            if (wasDark != IsDark)
                ThemeChanged?.Invoke();
        }
    }

    /// <summary>The accent every window follows, as <c>#RRGGBB</c>, or <c>""</c> to follow Windows.</summary>
    internal string AccentColor
    {
        get => _accent;
        set
        {
            var next = AccentShades.Normalize(value);
            if (next == _accent)
                return;
            _accent = next;
            ApplyAccent();
        }
    }

    /// <summary>The accent Windows is using, as <c>#RRGGBB</c>. A failure follows the default blue.</summary>
    internal string SystemAccent
    {
        get
        {
            try
            {
                if (_uiSettings is null)
                    return "#0078D4";
                var color = _uiSettings.GetColorValue(UIColorType.Accent);
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            catch (COMException)
            {
                return "#0078D4";
            }
        }
    }

    public ElementTheme RequestedTheme => IsDark ? ElementTheme.Dark : ElementTheme.Light;

    public SystemBackdropTheme BackdropTheme => IsDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;

    /// <summary>Puts the tray windows behind Explorer's while an app bar changes.</summary>
    internal static IDisposable ExplorerFirst()
    {
        return SystemTray.ExplorerFirst();
    }

    public event Action? ThemeChanged;

    public HostWindow CreateWindow(WindowKind kind, IHostClient client)
    {
        var window = new HostWindow(this, kind, client, NativeMenu.Dismiss);
        _windows.Add(window);
        return window;
    }

    internal void Forget(HostWindow window)
    {
        _windows.Remove(window);
    }

    // Screens and the work area

    /// <summary>
    /// Raised on the shell's thread when a panel hears <c>TaskbarCreated</c> or the display changes. The taskbar
    /// module decides whether that hides a new taskbar window.
    /// </summary>
    internal event Action? TaskbarMayHaveChanged;

    internal void TaskbarCreated()
    {
        TaskbarMayHaveChanged?.Invoke();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Loop.Post(RefreshScreens);
        Loop.Post(TaskbarCreated);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        FollowSystem(SystemChanges.From(e.Category));
    }

    private void RefreshScreens()
    {
        var next = Winshell.Screens.Enumerate().Select(m => m.Info).ToList();
        if (next.SequenceEqual(_screens))
            return;
        _screens.Clear();
        _screens.AddRange(next);
        Screens.Sync(next, s => s.Id, s => s.Id, s => s, (_, _) => { });
        for (var i = 0; i < next.Count; i++)
            if (Screens[i] != next[i])
                Screens[i] = next[i];
        WorkAreaMayHaveChanged();
    }

    internal void SystemChanged(uint message, nint wParam)
    {
        FollowSystem(SystemChanges.From(message, wParam));
    }

    private void FollowSystem(SystemChange change)
    {
        if (_systemChanges.Add(change))
            Loop.Post(FollowPendingChanges);
    }

    private void FollowPendingChanges()
    {
        var pending = _systemChanges.Take();
        if (pending.HasFlag(SystemChange.WorkArea))
            WorkAreaMayHaveChanged();
        if (pending.HasFlag(SystemChange.Screens))
            ScreensMayHaveChanged();
        if (pending.HasFlag(SystemChange.Theme))
            ReadTheme();
    }

    /// <summary>
    /// Reads the monitors again soon, once a burst of display messages settles: a monitor was plugged in or out, or
    /// its resolution or scale changed. Panels are placed again only if something changed.
    /// </summary>
    internal void ScreensMayHaveChanged()
    {
        _screenRefresh?.Dispose();
        _screenRefresh = Loop.Schedule(TimeSpan.FromMilliseconds(200), RefreshScreens);
    }

    /// <summary>
    /// Places every panel again soon. Explorer moves the work area some time after an app bar changes, and
    /// panels that respect other bars' zones follow it.
    /// </summary>
    internal void WorkAreaMayHaveChanged()
    {
        _relayout?.Dispose();
        _relayout = Loop.Schedule(TimeSpan.FromMilliseconds(50), () =>
        {
            _relayout = null;
            RefreshScreens();
            foreach (var w in _windows.ToArray())
                if (w.Kind == WindowKind.Panel && w.Placement.IsVisible)
                    w.Placement.PlacePanel();
        });
    }

    // Fullscreen apps

    /// <summary>Whether another app is fullscreen on a screen, so panels there step back as the taskbar does.</summary>
    internal bool FullscreenOn(string screenId)
    {
        return _fullscreenScreens.Contains(screenId);
    }

    private void OnForegroundEvent(uint evt, nint hwnd, int idObject, int idChild)
    {
        // A window's move or resize sends this for every window, so only the foreground one can change the answer.
        if (hwnd == 0 || !WindowRules.IsWindowObject(idObject, idChild)
                      || (evt == Win32.EVENT_OBJECT_LOCATIONCHANGE && hwnd != Win32.GetForegroundWindow()))
            return;
        Loop.Post(ReadFullscreen);
    }

    private void ReadFullscreen()
    {
        HashSet<string> next = [];
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd != 0 && Win32.IsWindowVisible(hwnd) && !IsShellWindow(hwnd) && Win32.GetWindowRect(hwnd, out var rect))
        {
            var style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
            foreach (var monitor in Winshell.Screens.Enumerate())
                if (WindowRules.CoversMonitor(style, rect, monitor.Bounds))
                    next.Add(monitor.Info.Id);
        }

        if (next.SetEquals(_fullscreenScreens))
            return;
        _fullscreenScreens = next;
        WorkAreaMayHaveChanged();
    }

    /// <summary>The desktop, and a panel or popup of this process, which are never a fullscreen app.</summary>
    private static bool IsShellWindow(nint hwnd)
    {
        if (((uint)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0)
            return true;
        return TopLevelWindows.ClassName(hwnd) is "Progman" or "WorkerW";
    }

    // Theme

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        Loop.Post(ReadTheme);
    }

    private void ReadTheme()
    {
        ApplyAccent();
        var dark = false;
        try
        {
            using var key =
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = WindowsModeDark(key?.GetValue("SystemUsesLightTheme"), key?.GetValue("AppsUseLightTheme"));
        }
        catch (SecurityException)
        {
        }

        var contrast = ContrastTheme.IsOn();
        var contrastChanged = contrast != IsHighContrast;
        if (dark == _windowsDark && !contrastChanged)
            return;
        var wasDark = IsDark;
        _windowsDark = dark;
        IsHighContrast = contrast;
        if (wasDark != IsDark || contrastChanged)
            ThemeChanged?.Invoke();
    }

    /// <summary>
    /// The brushes that carry the accent, with the shade each takes in dark and in light. Every control's resource is
    /// this same object, so a new color reaches what is already on screen, where a new <c>SystemAccentColor</c> reaches
    /// only brushes not made yet. The contrast dictionary is not here: a contrast theme keeps the colors Windows gave it.
    /// </summary>
    private static readonly (string Key, Func<AccentShades, Color> Dark, Func<AccentShades, Color> Light)[]
        AccentBrushes =
        [
            ("AccentFillColorDefaultBrush", s => s.Light2, s => s.Dark1),
            ("AccentFillColorSecondaryBrush", s => s.Light2, s => s.Dark1),
            ("AccentFillColorTertiaryBrush", s => s.Light2, s => s.Dark1),
            ("AccentFillColorSelectedTextBackgroundBrush", s => s.Accent, s => s.Accent),
            ("AccentTextFillColorPrimaryBrush", s => s.Light3, s => s.Dark2),
            ("AccentTextFillColorSecondaryBrush", s => s.Light3, s => s.Dark3),
            ("AccentTextFillColorTertiaryBrush", s => s.Light2, s => s.Dark1),
            ("AccentAAFillColorDefaultBrush", s => s.Light2, s => s.Dark1),
            ("AccentAAFillColorSecondaryBrush", s => s.Light3, s => s.Dark2),
            ("AccentAAFillColorTertiaryBrush", s => s.Light1, s => s.Accent),
            ("SystemFillColorAttentionBrush", s => s.Light2, s => s.Accent)
        ];

    /// <summary>Paints the accent brushes. They are Windows's own until an accent is picked, and painted from then on.</summary>
    private void ApplyAccent()
    {
        if (Application.Current?.Resources.MergedDictionaries.OfType<XamlControlsResources>().FirstOrDefault() is not
            { } controls)
            return;
        if (!AccentShades.TryFrom(_accent, out var shades))
        {
            if (!_accentPainted)
                return;
            shades = WindowsShades();
        }

        _accentPainted = true;
        var dark = (ResourceDictionary)controls.ThemeDictionaries["Default"];
        var light = (ResourceDictionary)controls.ThemeDictionaries["Light"];
        foreach (var (key, inDark, inLight) in AccentBrushes)
        {
            ((SolidColorBrush)dark[key]).Color = inDark(shades);
            ((SolidColorBrush)light[key]).Color = inLight(shades);
        }
    }

    /// <summary>The shades Windows made for its accent. A failure follows the default blue.</summary>
    private AccentShades WindowsShades()
    {
        try
        {
            if (_uiSettings is { } ui)
                return new AccentShades(
                    ui.GetColorValue(UIColorType.Accent),
                    ui.GetColorValue(UIColorType.AccentLight1),
                    ui.GetColorValue(UIColorType.AccentLight2),
                    ui.GetColorValue(UIColorType.AccentLight3),
                    ui.GetColorValue(UIColorType.AccentDark1),
                    ui.GetColorValue(UIColorType.AccentDark2),
                    ui.GetColorValue(UIColorType.AccentDark3));
        }
        catch (COMException)
        {
        }

        AccentShades.TryFrom("#0078D4", out var shades);
        return shades;
    }

    /// <summary>
    /// Whether Windows mode is dark, from the Personalize values, where <c>0</c> is dark. A missing Windows-mode value
    /// follows app mode, and a missing pair is light.
    /// </summary>
    internal static bool WindowsModeDark(object? systemValue, object? appsValue)
    {
        return systemValue is int systemLight ? systemLight == 0 : appsValue is int appsLight && appsLight == 0;
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        if (_uiSettings is not null)
        {
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            _uiSettings = null;
        }

        foreach (var watch in _foregroundWatches)
            watch.Dispose();
        foreach (var w in _windows.ToArray())
            w.Dispose();
        // Last, since a window lets go of its grab as it goes.
        OutsideClicks.Dispose();
    }
}
