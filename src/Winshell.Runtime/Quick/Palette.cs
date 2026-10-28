namespace Winshell.Runtime.Quick;

/// <summary>One group of a palette: the colors for active, inactive or disabled widgets.</summary>
public sealed class ColorGroup : QObject
{
    private readonly bool _disabled;
    private readonly Dictionary<string, QColor> _overrides = new(StringComparer.Ordinal);

    internal ColorGroup(bool disabled)
    {
        _disabled = disabled;
        Theme.Changed += OnTheme;
    }

    private void OnTheme()
    {
        foreach (var name in Names)
            NotifyChanged(name);
    }

    internal static readonly string[] Names =
    [
        "window", "windowText", "base", "alternateBase", "text", "button", "buttonText", "brightText", "highlight",
        "highlightedText", "light", "midlight", "mid", "dark", "shadow", "link", "linkVisited", "toolTipBase",
        "toolTipText", "placeholderText", "accent",
    ];

    private QColor Get(string name)
    {
        TrackQml(name);
        return _overrides.TryGetValue(name, out var c) ? c : Palette.Pick(Theme.Current, _disabled, name);
    }

    private void Set(string name, QColor value)
    {
        _overrides[name] = value;
        NotifyChanged(name);
    }

    [Qml] public QColor Window { get => Get("window"); set => Set("window", value); }

    [Qml] public QColor WindowText { get => Get("windowText"); set => Set("windowText", value); }

    [Qml] public QColor Base { get => Get("base"); set => Set("base", value); }

    [Qml] public QColor AlternateBase { get => Get("alternateBase"); set => Set("alternateBase", value); }

    [Qml] public QColor Text { get => Get("text"); set => Set("text", value); }

    [Qml] public QColor Button { get => Get("button"); set => Set("button", value); }

    [Qml] public QColor ButtonText { get => Get("buttonText"); set => Set("buttonText", value); }

    [Qml] public QColor BrightText { get => Get("brightText"); set => Set("brightText", value); }

    [Qml] public QColor Highlight { get => Get("highlight"); set => Set("highlight", value); }

    [Qml] public QColor HighlightedText { get => Get("highlightedText"); set => Set("highlightedText", value); }

    [Qml] public QColor Light { get => Get("light"); set => Set("light", value); }

    [Qml] public QColor Midlight { get => Get("midlight"); set => Set("midlight", value); }

    [Qml] public QColor Mid { get => Get("mid"); set => Set("mid", value); }

    [Qml] public QColor Dark { get => Get("dark"); set => Set("dark", value); }

    [Qml] public QColor Shadow { get => Get("shadow"); set => Set("shadow", value); }

    [Qml] public QColor Link { get => Get("link"); set => Set("link", value); }

    [Qml] public QColor LinkVisited { get => Get("linkVisited"); set => Set("linkVisited", value); }

    [Qml] public QColor ToolTipBase { get => Get("toolTipBase"); set => Set("toolTipBase", value); }

    [Qml] public QColor ToolTipText { get => Get("toolTipText"); set => Set("toolTipText", value); }

    [Qml] public QColor PlaceholderText { get => Get("placeholderText"); set => Set("placeholderText", value); }

    [Qml] public QColor Accent { get => Get("accent"); set => Set("accent", value); }

    protected override void OnDestroy() => Theme.Changed -= OnTheme;
}

/// <summary>An item's <c>palette</c>, filled from the Windows 11 theme.</summary>
public sealed class Palette : QObject
{
    public Palette()
    {
        Active = new ColorGroup(false);
        Inactive = new ColorGroup(false);
        Disabled = new ColorGroup(true);
    }

    [Qml] public ColorGroup Active { get; }

    [Qml] public ColorGroup Inactive { get; }

    [Qml] public ColorGroup Disabled { get; }

    [Qml] public QColor Window { get => Active.Window; set => Active.Window = value; }

    [Qml] public QColor WindowText { get => Active.WindowText; set => Active.WindowText = value; }

    [Qml] public QColor Base { get => Active.Base; set => Active.Base = value; }

    [Qml] public QColor AlternateBase { get => Active.AlternateBase; set => Active.AlternateBase = value; }

    [Qml] public QColor Text { get => Active.Text; set => Active.Text = value; }

    [Qml] public QColor Button { get => Active.Button; set => Active.Button = value; }

    [Qml] public QColor ButtonText { get => Active.ButtonText; set => Active.ButtonText = value; }

    [Qml] public QColor BrightText { get => Active.BrightText; set => Active.BrightText = value; }

    [Qml] public QColor Highlight { get => Active.Highlight; set => Active.Highlight = value; }

    [Qml] public QColor HighlightedText { get => Active.HighlightedText; set => Active.HighlightedText = value; }

    [Qml] public QColor Light { get => Active.Light; set => Active.Light = value; }

    [Qml] public QColor Midlight { get => Active.Midlight; set => Active.Midlight = value; }

    [Qml] public QColor Mid { get => Active.Mid; set => Active.Mid = value; }

    [Qml] public QColor Dark { get => Active.Dark; set => Active.Dark = value; }

    [Qml] public QColor Shadow { get => Active.Shadow; set => Active.Shadow = value; }

    [Qml] public QColor Link { get => Active.Link; set => Active.Link = value; }

    [Qml] public QColor ToolTipBase { get => Active.ToolTipBase; set => Active.ToolTipBase = value; }

    [Qml] public QColor ToolTipText { get => Active.ToolTipText; set => Active.ToolTipText = value; }

    [Qml] public QColor PlaceholderText { get => Active.PlaceholderText; set => Active.PlaceholderText = value; }

    [Qml] public QColor Accent { get => Active.Accent; set => Active.Accent = value; }

    /// <summary>A palette color from the Fluent tokens of the current theme.</summary>
    internal static QColor Pick(Theme t, bool disabled, string name)
    {
        var text = disabled ? t.TextFillColorDisabled : t.TextFillColorPrimary;
        var dark = t.IsDark;
        return name switch
        {
            "window" => t.SolidBackgroundFillColorBase,
            "windowText" or "text" or "buttonText" => text,
            "base" => t.IsHighContrast ? t.SystemColorWindowColor : dark ? QColor.Parse("#2b2b2b") : QColor.White,
            "alternateBase" => t.SolidBackgroundFillColorSecondary,
            "button" => t.ControlSolidFillColorDefault,
            "brightText" => QColor.White,
            "highlight" or "accent" => disabled ? t.AccentFillColorDisabled : t.AccentFillColorDefault,
            "highlightedText" => t.TextOnAccentFillColorPrimary,
            "light" => dark ? QColor.Parse("#5a5a5a") : QColor.White,
            "midlight" => dark ? QColor.Parse("#4a4a4a") : QColor.Parse("#e5e5e5"),
            "mid" => dark ? QColor.Parse("#3a3a3a") : QColor.Parse("#c8c8c8"),
            "dark" => dark ? QColor.Parse("#1c1c1c") : QColor.Parse("#a0a0a0"),
            "shadow" => QColor.Black,
            "link" => t.AccentTextFillColorPrimary,
            "linkVisited" => t.AccentTextFillColorPrimary.Darker(1.2),
            "toolTipBase" => t.AcrylicBackgroundFillColorDefault,
            "toolTipText" => t.TextFillColorPrimary,
            "placeholderText" => t.TextFillColorSecondary,
            _ => text,
        };
    }
}
