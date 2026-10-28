using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows;

/// <summary>
/// The <c>WindowEffects</c> attached object of a Quickshell window: the Windows 11 material behind it and the
/// shape of its corners.
/// </summary>
public sealed class WindowEffectsAttached(QObject owner) : QObject
{
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

    private QsWindow? Window => owner as QsWindow;

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
/// The <c>WindowsTheme</c> singleton: whether Windows is in dark mode, the accent color, and the Fluent design
/// tokens, so a shell can look like the rest of Windows 11.
/// </summary>
public sealed class WindowsTheme : QObject
{
    private static readonly string[] Props =
    [
        "dark", "accent", "accentLight1", "accentLight2", "accentLight3", "accentDark1", "accentDark2", "accentDark3",
        "textPrimary", "textSecondary", "textTertiary", "textDisabled", "textOnAccent", "accentText", "controlFill",
        "controlFillSecondary", "controlFillTertiary", "controlStroke", "controlStrokeSecondary", "controlStrongFill",
        "subtleFillSecondary", "subtleFillTertiary", "accentFill", "accentFillSecondary", "accentFillTertiary", "cardBackground",
        "cardStroke", "layerFill", "solidBackground", "flyoutBackground", "dividerStroke", "surfaceStroke", "focusStroke",
        "critical", "success", "caution",
    ];

    public WindowsTheme()
    {
        Theme.Changed += OnChanged;
    }

    private void OnChanged()
    {
        foreach (var p in Props)
            NotifyChanged(p);
    }

    private Theme T
    {
        get
        {
            TrackQml("dark");
            return Theme.Current;
        }
    }

    [Qml] public bool Dark => T.IsDark;

    [Qml] public QColor Accent => T.Accent;

    [Qml] public QColor AccentLight1 => T.AccentLight1;

    [Qml] public QColor AccentLight2 => T.AccentLight2;

    [Qml] public QColor AccentLight3 => T.AccentLight3;

    [Qml] public QColor AccentDark1 => T.AccentDark1;

    [Qml] public QColor AccentDark2 => T.AccentDark2;

    [Qml] public QColor AccentDark3 => T.AccentDark3;

    [Qml] public QColor TextPrimary => T.TextColor;

    [Qml] public QColor TextSecondary => T.TextSecondary;

    [Qml] public QColor TextTertiary => T.TextTertiary;

    [Qml] public QColor TextDisabled => T.TextDisabled;

    [Qml] public QColor TextOnAccent => T.TextOnAccent;

    [Qml] public QColor AccentText => T.AccentText;

    [Qml] public QColor ControlFill => T.ControlFill;

    [Qml] public QColor ControlFillSecondary => T.ControlFillSecondary;

    [Qml] public QColor ControlFillTertiary => T.ControlFillTertiary;

    [Qml] public QColor ControlStroke => T.ControlStroke;

    [Qml] public QColor ControlStrokeSecondary => T.ControlStrokeSecondary;

    [Qml] public QColor ControlStrongFill => T.ControlStrongFill;

    [Qml] public QColor SubtleFillSecondary => T.SubtleFillSecondary;

    [Qml] public QColor SubtleFillTertiary => T.SubtleFillTertiary;

    [Qml] public QColor AccentFill => T.AccentFill;

    [Qml] public QColor AccentFillSecondary => T.AccentFillSecondary;

    [Qml] public QColor AccentFillTertiary => T.AccentFillTertiary;

    [Qml] public QColor CardBackground => T.CardBackground;

    [Qml] public QColor CardStroke => T.CardStroke;

    [Qml] public QColor LayerFill => T.LayerFill;

    [Qml] public QColor SolidBackground => T.SolidBackground;

    [Qml] public QColor FlyoutBackground => T.FlyoutBackground;

    [Qml] public QColor DividerStroke => T.DividerStroke;

    [Qml] public QColor SurfaceStroke => T.SurfaceStroke;

    [Qml] public QColor FocusStroke => T.FocusStrokeOuter;

    [Qml] public QColor Critical => T.SystemCritical;

    [Qml] public QColor Success => T.SystemSuccess;

    [Qml] public QColor Caution => T.SystemCaution;

    [Qml] public string FontFamily => FontSpec.DefaultFamily;

    [Qml] public string IconFontFamily => "Segoe Fluent Icons";

    protected override void OnDestroy() => Theme.Changed -= OnChanged;
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
        StandardModules.Registering += registry =>
        {
            var windows = registry.Module("Quickshell.Windows");
            windows.Uncreatable<WindowEffectsAttached>("WindowEffects", "WindowEffects is only available as an attached object", attached: o => new WindowEffectsAttached(o), attachedType: typeof(WindowEffectsAttached))
                .Singleton(_ => new WindowsTheme(), "WindowsTheme");
            WaylandCompat.Register(registry);
            ServiceModules.Register(registry);
        };
        _ = platform;
    }

    /// <summary>Gives a new engine the Windows services its singletons use.</summary>
    public static void AddServices(QmlEngine engine, WindowsPlatform platform)
    {
        _ = engine;
        _ = platform;
    }
}
