using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.UI;
using Windows.UI.ViewManagement;
using Winshell.Native;

namespace Winshell;

/// <summary>What every native window of one process shares: the screens, the theme, and the clicks that close popups.</summary>
internal sealed unsafe class ShellPlatform : IDisposable
{
    private static ShellPlatform? _current;

    private readonly List<HostWindow> _windows = [];
    private readonly HashSet<HostWindow> _grabbing = [];
    private readonly SystemChanges _systemChanges = new();
    private readonly List<ScreenInfo> _screens;
    private HookThread? _mouseThread;
    private IDisposable? _relayout;
    private IDisposable? _screenRefresh;
    private global::Windows.UI.ViewManagement.UISettings? _uiSettings;
    private bool _windowsDark;
    private ColorScheme _scheme;
    private string _accent = "";
    private bool _accentPainted;

    public ShellPlatform(IEventLoop loop, Log log)
    {
        Loop = loop;
        Log = log;
        _current = this;
        _screens = Winshell.Screens.Enumerate().Select(m => m.Info).ToList();
        Screens = new ObservableCollection<ScreenInfo>(_screens);
        ReadTheme();
        _uiSettings = new global::Windows.UI.ViewManagement.UISettings();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Flyouts = new FlyoutDismissal(this);
    }

    public IEventLoop Loop { get; }

    public Log Log { get; }

    public ObservableCollection<ScreenInfo> Screens { get; }

    internal FlyoutDismissal Flyouts { get; }

    /// <summary>The native windows that exist now.</summary>
    internal IReadOnlyList<HostWindow> Windows => _windows;

    public string PrimaryScreenId => _screens.FirstOrDefault(s => s.IsPrimary)?.Id ?? _screens.FirstOrDefault()?.Id ?? "";

    /// <summary>Whether windows are dark. <see cref="ColorScheme"/> overrides Windows mode, and <see cref="ColorScheme.System"/> follows it.</summary>
    public bool IsDark => Shell.DarkFor(_scheme, _windowsDark);

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
                var color = _uiSettings.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent);
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
    internal static IDisposable ExplorerFirst() => Winshell.Tray.TrayHost.ExplorerFirst();

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
        _grabbing.Remove(window);
        if (_grabbing.Count == 0)
            Unhook();
    }

    // Screens and the work area

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Loop.Post(RefreshScreens);

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => FollowSystem(SystemChanges.From(e.Category));

    private void RefreshScreens()
    {
        var next = Winshell.Screens.Enumerate().Select(m => m.Info).ToList();
        if (next.SequenceEqual(_screens))
            return;
        _screens.Clear();
        _screens.AddRange(next);
        Screens.Sync(next, s => s.Id, s => s.Id, s => s, (_, _) => { });
        for (var i = 0; i < next.Count; i++)
        {
            if (Screens[i] != next[i])
                Screens[i] = next[i];
        }
        WorkAreaMayHaveChanged();
    }

    internal void SystemChanged(uint message, nint wParam) => FollowSystem(SystemChanges.From(message, wParam));

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
            {
                if (w.Kind == WindowKind.Panel && w.IsVisible)
                    w.PlacePanel();
            }
        });
    }

    // Popups that close on a click outside them

    internal void UpdatePopupGrab(HostWindow window, bool grabbing)
    {
        if (grabbing && window.Handle != 0)
            _grabbing.Add(window);
        else
            _grabbing.Remove(window);
        if (_grabbing.Count > 0 && _mouseThread is null)
            _mouseThread = HookThread.Start(Win32.WH_MOUSE_LL, &MouseHook, "Winshell popup mouse hook");
        else if (_grabbing.Count == 0)
            Unhook();
    }

    private void Unhook()
    {
        _mouseThread?.Stop();
        _mouseThread = null;
    }

    // The procedure runs on the hook thread, so it only reads the click and posts the rest to the loop.
    [UnmanagedCallersOnly]
    private static nint MouseHook(int code, nint wParam, nint lParam)
    {
        var platform = _current;
        if (code >= 0 && platform is not null)
        {
            var msg = (uint)wParam;
            if (msg is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or Win32.WM_XBUTTONDOWN)
            {
                var pt = ((MSLLHOOKSTRUCT*)lParam)->pt;
                NativeCallback.Guard(platform.Log, "shell", () => platform.Loop.Post(() => platform.DismissOutside(pt)));
            }
        }
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    internal void DismissOutside(POINT pt)
    {
        foreach (var w in _grabbing.ToArray())
        {
            if (!w.ContainsScreenPoint(pt))
                w.Dismiss();
        }
    }

    // Theme

    private void OnColorValuesChanged(global::Windows.UI.ViewManagement.UISettings sender, object args) => Loop.Post(ReadTheme);

    private void ReadTheme()
    {
        ApplyAccent();
        var dark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = WindowsModeDark(key?.GetValue("SystemUsesLightTheme"), key?.GetValue("AppsUseLightTheme"));
        }
        catch (System.Security.SecurityException)
        {
        }
        var contrast = HighContrastOn();
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
    private static readonly (string Key, Func<AccentShades, Color> Dark, Func<AccentShades, Color> Light)[] AccentBrushes =
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
        ("SystemFillColorAttentionBrush", s => s.Light2, s => s.Accent),
    ];

    /// <summary>Paints the accent brushes. They are Windows's own until an accent is picked, and painted from then on.</summary>
    private void ApplyAccent()
    {
        if (Application.Current?.Resources.MergedDictionaries.OfType<XamlControlsResources>().FirstOrDefault() is not { } controls)
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
            {
                return new(
                    ui.GetColorValue(UIColorType.Accent),
                    ui.GetColorValue(UIColorType.AccentLight1),
                    ui.GetColorValue(UIColorType.AccentLight2),
                    ui.GetColorValue(UIColorType.AccentLight3),
                    ui.GetColorValue(UIColorType.AccentDark1),
                    ui.GetColorValue(UIColorType.AccentDark2),
                    ui.GetColorValue(UIColorType.AccentDark3));
            }
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
    internal static bool WindowsModeDark(object? systemValue, object? appsValue) =>
        systemValue is int systemLight ? systemLight == 0 : appsValue is int appsLight && appsLight == 0;

    private static bool HighContrastOn()
    {
        var hc = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        return Win32.SystemParametersInfoW(Win32.SPI_GETHIGHCONTRAST, hc.cbSize, &hc, 0) && (hc.dwFlags & Win32.HCF_HIGHCONTRASTON) != 0;
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
        Unhook();
        Flyouts.Dispose();
        foreach (var w in _windows.ToArray())
            w.Dispose();
    }
}
