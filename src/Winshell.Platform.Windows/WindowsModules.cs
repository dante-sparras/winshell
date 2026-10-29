using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows;

/// <summary>
/// The <c>WindowEffects</c> attached object of a window: the Windows 11 material behind it and the
/// shape of its corners.
/// </summary>
public sealed class WindowEffectsAttached : QObject
{
    private readonly QObject _owner;

    public WindowEffectsAttached(QObject owner)
    {
        _owner = owner;
        Theme.Changed += OnTheme;
    }

    [QmlEnum]
    public enum Material
    {
        None = 0,
        Mica = 1,
        Acrylic = 2,
        MicaAlt = 3,
    }

    [QmlEnum]
    public enum CornerStyle
    {
        DefaultCorners = 0,
        SquareCorners = 1,
        RoundCorners = 2,
        SmallRoundCorners = 3,
    }

    private ShellWindow? Window => _owner as ShellWindow;

    protected override void OnDestroy()
    {
        Theme.Changed -= OnTheme;
        base.OnDestroy();
    }

    private void OnTheme()
    {
        if (!IsDestroyed)
            NotifyChanged("backdrop");
    }

    [Qml]
    public int Backdrop
    {
        get { Track(); return (int)(Window?.Backdrop ?? WindowBackdrop.None); }
        set
        {
            if (Window is { } w)
                w.Backdrop = (WindowBackdrop)value;
            NotifyChanged("backdrop");
        }
    }

    [Qml]
    public int Corners
    {
        get { Track(); return (int)(Window?.Corners ?? WindowCorners.Default); }
        set
        {
            if (Window is { } w)
                w.Corners = (WindowCorners)value;
            NotifyChanged("corners");
        }
    }
}

/// <summary>
/// The <c>Theme</c> singleton: the Windows 11 theme as WinUI 3 has it, so a shell can look like the rest of
/// Windows. Every color token of <see cref="Theme"/> is a property with its WinUI name, first letter lowered, such
/// as <c>textFillColorPrimary</c> or <c>accentFillColorDefault</c>. With the type ramp, the corner radii, the
/// motion timings and the shadows, they follow the user's Windows settings as they change. <c>colorScheme</c> and
/// <c>accentColor</c> are the shell's own choice on top of them.
/// </summary>
public sealed class WindowsTheme : QObject
{
    private static readonly (string Name, Func<Theme, QColor> Get)[] Colors = typeof(Theme).GetProperties()
        .Where(p => p.PropertyType == typeof(QColor) && p.GetMethod is { IsStatic: false })
        .Select(p => (Names.ToQml(p.Name), (Func<Theme, QColor>)p.GetMethod!.CreateDelegate(typeof(Func<Theme, QColor>))))
        .ToArray();

    private static readonly string[] ThemeProps = ["dark", "highContrast", "animationsEnabled", "textScaleFactor", "typography", "autoHideScrollBars", "colorScheme", "accentColor", "cardShadow", "tooltipShadow", "flyoutShadow", "dialogShadow"];

    public WindowsTheme()
    {
        foreach (var (name, get) in Colors)
            AddDynamicProperty(new DynamicProperty(this, name, "color") { IsReadonly = true }).Write(get(Theme.Current));
        Theme.Changed += OnChanged;
    }

    private void OnChanged()
    {
        var theme = Theme.Current;
        foreach (var (name, get) in Colors)
            GetDynamicProperty(name)!.Write(get(theme));
        foreach (var p in ThemeProps)
            NotifyChanged(p);
    }

    [Qml] public bool Dark { get { Track(); return Theme.Current.IsDark; } }

    /// <summary>
    /// <c>"system"</c> follows Windows mode, as the taskbar and Start do; <c>"light"</c> and <c>"dark"</c> are the
    /// shell's own choice for Winshell's windows, including that window's title-bar frame. Other programs keep their own
    /// mode. A contrast theme still wins.
    /// </summary>
    [Qml]
    public string ColorScheme
    {
        get
        {
            Track();
            return _choice.Dark switch { true => "dark", false => "light", null => "system" };
        }
        set
        {
            bool? dark = value switch
            {
                "dark" => true,
                "light" => false,
                "system" => null,
                _ => _choice.Dark,
            };
            if (value is not ("dark" or "light" or "system"))
                Engine?.Log.Warn("theme", $"Theme.colorScheme is \"system\", \"light\" or \"dark\", not \"{value}\"");
            Choose(_choice with { Dark = dark });
        }
    }

    /// <summary>
    /// The shell's own accent, from which the accent tokens and their shades are made. Transparent follows the accent
    /// picked in Windows.
    /// </summary>
    [Qml]
    public QColor AccentColor
    {
        get { Track(); return _choice.Accent ?? QColor.Transparent; }
        set => Choose(_choice with { Accent = value.IsValid && value.A > 0 ? value : null });
    }

    // This generation's choice. A reload's new generation starts from Windows, not from what the old one chose.
    private ThemeChoice _choice = ThemeChoice.None;

    private void Choose(ThemeChoice choice)
    {
        _choice = choice;
        Theme.Choose(this, choice);
    }

    /// <summary>Whether a contrast theme is on. Every color token is then one of the system colors.</summary>
    [Qml] public bool HighContrast { get { Track(); return Theme.Current.IsHighContrast; } }

    /// <summary>Whether Windows animation effects are on. Shells skip motion when this is false.</summary>
    [Qml] public bool AnimationsEnabled { get { Track(); return Theme.Current.AnimationsEnabled; } }

    /// <summary>False when "Always show scrollbars" is on in Windows. Scroll bars then stay wide.</summary>
    [Qml] public bool AutoHideScrollBars { get { Track(); return Theme.Current.AutoHideScrollBars; } }

    /// <summary>
    /// Windows text size as a multiplier. 1 is the Settings default. The type ramp and text with no
    /// <c>font.pixelSize</c> of its own use it. An explicit pixel size or point size does not.
    /// </summary>
    [Qml] public double TextScaleFactor { get { Track(); return Theme.TextScale(Theme.Current.TextScaleFactor); } }

    [Qml] public string FontFamily => FontSpec.DefaultFamily;

    [Qml] public string IconFontFamily => "Segoe Fluent Icons";

    private readonly WindowsTypography _typography = new();

    /// <summary>The type ramp. Its sizes follow Windows text size.</summary>
    [Qml]
    public WindowsTypography Typography
    {
        get { Track(); return _typography; }
    }

    [Qml] public double ControlCornerRadius => Fluent.ControlCornerRadius;

    [Qml] public double OverlayCornerRadius => Fluent.OverlayCornerRadius;

    [Qml] public int ControlFasterAnimationDuration => Fluent.ControlFasterAnimationDuration;

    [Qml] public int ControlFastAnimationDuration => Fluent.ControlFastAnimationDuration;

    [Qml] public int ControlNormalAnimationDuration => Fluent.ControlNormalAnimationDuration;

    /// <summary>The curve as <c>easing.bezierCurve</c> takes it: two control points, then the end point.</summary>
    [Qml]
    public List<object?> ControlFastOutSlowInKeySpline
    {
        get
        {
            var (x1, y1, x2, y2) = Fluent.ControlFastOutSlowInKeySpline;
            return [x1, y1, x2, y2, 1.0, 1.0];
        }
    }

    [Qml] public WindowsShadow CardShadow { get { Track(); return new(Theme.Current.CardShadow); } }

    [Qml] public WindowsShadow TooltipShadow { get { Track(); return new(Theme.Current.TooltipShadow); } }

    [Qml] public WindowsShadow FlyoutShadow { get { Track(); return new(Theme.Current.FlyoutShadow); } }

    [Qml] public WindowsShadow DialogShadow { get { Track(); return new(Theme.Current.DialogShadow); } }

    protected override void OnDestroy()
    {
        Theme.Changed -= OnChanged;
        Theme.Release(this);
    }
}

/// <summary>
/// <c>Theme.typography</c>: the Windows 11 type ramp in Segoe UI Variable. Each step is a font to assign
/// whole, as in <c>font: Theme.typography.subtitle</c>.
/// </summary>
public sealed class WindowsTypography : QObject
{
    private static FontGroup Font(TypeStyle style) =>
        new(FontSpec.DefaultFamily, style.PixelSize * Theme.TextScale(Theme.Current.TextScaleFactor), style.Weight);

    [Qml] public FontGroup Caption { get { Track(); return Font(Fluent.Caption); } }

    [Qml] public FontGroup Body { get { Track(); return Font(Fluent.Body); } }

    [Qml] public FontGroup BodyStrong { get { Track(); return Font(Fluent.BodyStrong); } }

    [Qml] public FontGroup BodyLarge { get { Track(); return Font(Fluent.BodyLarge); } }

    [Qml] public FontGroup Subtitle { get { Track(); return Font(Fluent.Subtitle); } }

    [Qml] public FontGroup Title { get { Track(); return Font(Fluent.Title); } }

    [Qml] public FontGroup TitleLarge { get { Track(); return Font(Fluent.TitleLarge); } }

    [Qml] public FontGroup Display { get { Track(); return Font(Fluent.Display); } }
}

/// <summary>A shadow of the Windows 11 elevation: its blur, how far it falls, and its color.</summary>
public sealed class WindowsShadow(Elevation elevation) : QObject
{
    [Qml] public double Blur => elevation.Blur;

    [Qml] public double VerticalOffset => elevation.OffsetY;

    [Qml] public QColor Color => elevation.Color;
}

/// <summary>Registers the Windows modules and the services the shell's engine gets from Windows.</summary>
public static class WindowsModules
{
    private static bool _installed;

    /// <summary>Adds the Windows modules to every module registry from now on.</summary>
    public static void Install(WindowsPlatform platform)
    {
        ImageLoader.AddProvider("icon", IconProvider.Instance);
        if (_installed)
            return;
        _installed = true;
        StandardModules.ExtraModules = registry =>
        {
            Register(registry);
            ServiceModules.Register(registry);
        };
        _ = platform;
    }

    /// <summary>Adds <c>WindowEffects</c> to <c>Winshell</c>, and <c>Winshell.Theme</c>.</summary>
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell")
            .Uncreatable<WindowEffectsAttached>("WindowEffects", "WindowEffects is only available as an attached object", attached: o => new WindowEffectsAttached(o), attachedType: typeof(WindowEffectsAttached));
        registry.Module("Winshell.Theme")
            .Singleton(_ => new WindowsTheme(), "Theme");
    }

    /// <summary>Gives a new engine the Windows services its singletons use.</summary>
    public static void AddServices(QmlEngine engine, WindowsPlatform platform)
    {
        engine.AddService<Services.Input.IHotkeyRegistrar>(platform.Hotkeys);
        engine.AddService<Services.Session.IWorkstationLock>(Services.Session.WindowsWorkstationLock.Instance);
    }

    /// <summary>
    /// Lets <c>Startup</c> register this <c>winshell.exe</c>, with the arguments it was started with, to run when the
    /// current user signs in. Without it, <c>Startup.available</c> is false.
    /// </summary>
    public static void AddStartup(QmlEngine engine, string exe, IEnumerable<string> args) =>
        engine.AddService<Services.Session.IStartupRegistry>(new Services.Session.WindowsStartupRegistry(Services.Session.WindowsStartupRegistry.CommandFor(exe, args)));

    /// <summary>Lets <c>Updates</c> follow the process's updater. Without one, <c>Updates.available</c> is false.</summary>
    public static void AddUpdates(QmlEngine engine, Services.Updates.Updater updater) => engine.AddService(updater);
}

/// <summary>Where the service modules for Windows register.</summary>
public static class ServiceModules
{
    public static void Register(ModuleRegistry registry)
    {
        Services.Power.PowerModule.Register(registry);
        Services.Network.NetworkingModule.Register(registry);
        Services.Bluetooth.BluetoothModule.Register(registry);
        Services.Audio.AudioService.Register(registry);
        Services.Media.MediaService.Register(registry);
        Services.WindowManagerModules.Register(registry);
        Services.Session.SessionModule.Register(registry);
        Services.Tray.SystemTrayModule.Register(registry);
        Services.Notifications.NotificationModule.Register(registry);
        Services.Capture.CaptureModule.Register(registry);
        Services.Input.InputModule.Register(registry);
        Services.Updates.UpdatesService.Register(registry);
    }
}
