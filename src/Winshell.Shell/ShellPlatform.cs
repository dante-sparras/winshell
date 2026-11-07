using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Security;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.Win32.UI.WindowsAndMessaging;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Winshell.Native;
using Winshell.Windows;

namespace Winshell;

/// <summary>What every native window of one process shares: the screens, the theme, and the clicks that close popups.</summary>
internal sealed class ShellPlatform : IDisposable
{
    /// <summary>The accent followed when Windows' cannot be read: its default blue.</summary>
    private const string DefaultAccent = "#0078D4";

    private readonly List<HostWindow> _windows = [];
    private readonly SystemChanges _systemChanges = new();
    private readonly List<ScreenInfo> _screens;
    private readonly NativeWindow? _broadcasts;
    private readonly FullscreenApps _fullscreenApps;
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
        // Display and settings broadcasts reach only top-level windows. One hidden window hears them for the shell,
        // whether or not it has a window of its own.
        _broadcasts = NativeWindow.TryCreate("WinshellSystemChanges", (uint)WINDOW_STYLE.WS_POPUP,
            (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
            OnBroadcast, e => Log.Warn("shell", e.Message), false);
        if (_broadcasts is null)
            Log.Warn("shell",
                "Could not make a window for system broadcasts; screen and theme changes will not be seen");
        OutsideClicks = new OutsideClicks(this);
        // Explorer tells only the app bars that reserve an edge about a fullscreen app, so this reads it from the foreground window.
        _fullscreenApps = new FullscreenApps(loop, log);
        _fullscreenApps.Changed += WorkAreaMayHaveChanged;
    }

    public IEventLoop Loop { get; }

    public Log Log { get; }

    public ObservableCollection<ScreenInfo> Screens { get; }

    internal OutsideClicks OutsideClicks { get; }

    /// <summary>The native windows that exist now.</summary>
    internal IReadOnlyList<HostWindow> Windows => _windows;

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
                    return DefaultAccent;
                var color = _uiSettings.GetColorValue(UIColorType.Accent);
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            catch (COMException)
            {
                return DefaultAccent;
            }
        }
    }

    public ElementTheme RequestedTheme => IsDark ? ElementTheme.Dark : ElementTheme.Light;

    public SystemBackdropTheme BackdropTheme => IsDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;

    public event Action? ThemeChanged;

    public HostWindow CreateWindow(WindowKind kind, ShellWindow client)
    {
        var window = new HostWindow(this, kind, client);
        _windows.Add(window);
        return window;
    }

    internal void Forget(HostWindow window)
    {
        _windows.Remove(window);
    }

    // Screens and the work area

    private nint? OnBroadcast(WindowMessage message)
    {
        if (_systemChanges.Add(SystemChanges.From(message.Id, message.WParam)))
            Loop.Post(FollowPendingChanges);
        return null;
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
                w.Placer.OnWorkAreaChanged();
        });
    }

    // Fullscreen apps

    /// <summary>Whether another app is fullscreen on a screen, so panels there step back as the taskbar does.</summary>
    internal bool FullscreenOn(string screenId)
    {
        return _fullscreenApps.On(screenId);
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

        AccentShades.TryFrom(DefaultAccent, out var shades);
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
        _broadcasts?.Release(true);
        _fullscreenApps.Dispose();
        if (_uiSettings is not null)
        {
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            _uiSettings = null;
        }

        try
        {
            Shell.DisposeEach(_windows.ToArray());
        }
        finally
        {
            // Last, since a window lets go of its grab as it goes.
            OutsideClicks.Dispose();
        }
    }
}
