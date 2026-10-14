using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Winshell;

/// <summary>Paints the look an extension handed the shell, and dresses every Bar in it.</summary>
internal sealed class BarChrome
{
    /// <summary>What the first Bar is drawn on.</summary>
    internal SurfaceMaterial Material => _material;

    /// <summary>The theme id last painted, or null when Windows chrome is in effect or nothing has been painted.</summary>
    internal string? ThemeId { get; private set; }

    private Windows.UI.ViewManagement.UISettings? _theme;
    private SurfaceMaterial _material;
    private bool _logged;

    /// <summary>Asks <paramref name="apply"/> again when Windows changes a color or its transparency effects.</summary>
    /// <param name="queue">The UI queue the burst is collapsed onto.</param>
    /// <param name="apply">Paints the current look. Called once for a burst of Windows events.</param>
    internal void Watch(Microsoft.UI.Dispatching.DispatcherQueue queue, Action apply)
    {
        _theme = new Windows.UI.ViewManagement.UISettings();

        // Windows raises this several times for one change of theme or accent. The bar is painted once for the burst.
        var changed = new UiSignal(queue, TimeSpan.FromMilliseconds(100), apply);
        _theme.ColorValuesChanged += (_, _) => changed.Signal();
        _theme.AdvancedEffectsEnabledChanged += (_, _) => changed.Signal();
        apply();
    }

    /// <summary>
    /// Fills the Kit's brushes from <paramref name="look"/> and dresses every Bar's window in it.
    /// The extension worked the theme out. High contrast and a machine that cannot draw the material still land on solid.
    /// </summary>
    /// <param name="bars">Every monitor's Bar.</param>
    /// <param name="edge">The edge every Bar is on.</param>
    /// <param name="look">The look to paint.</param>
    internal void Apply(IReadOnlyList<BarWindow> bars, BarEdge edge, SurfaceLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        Look.Use(look.BodySize, look.CaptionSize, look.CornerSize, look.ControlCorner, look.GutterSize);
        Fonts.Use(look.FontFace);
        Paint.Apply(look.Tokens, look.Effects);
        var chrome = Paint.Chrome;
        BorderlessFrame.UseTheme(chrome.Light);
        var material = SurfaceMaterial.Solid;
        for (var i = 0; i < bars.Count; i++)
        {
            var dressed = Dress(bars[i], chrome, look.Material, edge);
            material = i == 0 ? dressed : material;
        }

        if (material != _material || !_logged || look.ThemeId != ThemeId)
        {
            _material = material;
            _logged = true;
            ThemeId = look.ThemeId;
            Log.Information("Theme", (chrome.Light ? "light" : "dark") + " " + material.ToString().ToLowerInvariant() + " theme=" + (look.ThemeId ?? "none"));
        }
    }

    /// <summary>Paints one Bar's window: its backdrop, its border line, and its frame. Each window has a backdrop of its own.</summary>
    /// <param name="window">The Bar.</param>
    /// <param name="chrome">What a window dresses in now.</param>
    /// <param name="wanted">What the look asked for. It falls back to solid where the machine cannot draw it.</param>
    /// <param name="edge">The edge the Bar is on.</param>
    /// <returns>What the Bar is drawn on.</returns>
    private static SurfaceMaterial Dress(BarWindow window, Chrome chrome, SurfaceMaterial wanted, BarEdge edge)
    {
        var root = window.Content as FrameworkElement;
        var title = window.AppWindow.TitleBar;
        var hwnd = window.Hwnd;
        var drawn = wanted switch
        {
            SurfaceMaterial.Acrylic when ShellBackdrop.IsSupported => SurfaceMaterial.Acrylic,
            SurfaceMaterial.Mica when !chrome.HighContrast && ShellBackdrop.IsMicaSupported && chrome.Effects => SurfaceMaterial.Mica,
            _ => SurfaceMaterial.Solid,
        };
        var mica = drawn == SurfaceMaterial.Mica;
        var open = drawn != SurfaceMaterial.Solid;
        if (root is not null)
        {
            root.RequestedTheme = chrome.HighContrast
                ? ElementTheme.Default
                : chrome.Light ? ElementTheme.Light : ElementTheme.Dark;
        }

        try
        {
            if (drawn == SurfaceMaterial.Acrylic)
            {
                window.Acrylic.Configure(chrome.Light, chrome.Background, 0.55f, 0.85f);
                if (window.SystemBackdrop != window.Acrylic)
                {
                    window.SystemBackdrop = window.Acrylic;
                }
            }
            else if (mica)
            {
                window.Backdrop.ConfigureSystem(chrome.Light);
                if (window.SystemBackdrop != window.Backdrop)
                {
                    window.SystemBackdrop = window.Backdrop;
                }
            }
            else if (window.SystemBackdrop is not null)
            {
                window.SystemBackdrop = null;
            }
        }
        catch (Exception ex)
        {
            mica = false;
            open = false;
            drawn = SurfaceMaterial.Solid;
            Log.Warning("Theme", "bar backdrop failed", ex);
            window.SystemBackdrop = null;
        }

        if (root is Border border)
        {
            border.Background = open ? Paint.Hit : Paint.Background;
            var line = WindowBorder.For(
                chrome.Light,
                chrome.HighContrast,
                chrome.Foreground.R,
                chrome.Foreground.G,
                chrome.Foreground.B,
                edge,
                BorderlessFrame.VisiblePixels(hwnd),
                ScreenMetrics.Scale(hwnd));
            border.BorderBrush = Paint.Solid(Paint.Derived(line.A, line.R, line.G, line.B));
            border.BorderThickness = new Thickness(line.Left, line.Top, line.Right, line.Bottom);
        }

        window.FrameColor = open ? BorderlessFrame.None : chrome.FrameColor;
        var frame = open
            ? Paint.Derived(0, chrome.Background.R, chrome.Background.G, chrome.Background.B)
            : chrome.Background;
        title.BackgroundColor = frame;
        title.InactiveBackgroundColor = frame;
        title.ButtonBackgroundColor = frame;
        title.ButtonInactiveBackgroundColor = frame;
        title.ButtonForegroundColor = frame;
        title.ButtonHoverBackgroundColor = frame;
        title.ButtonPressedBackgroundColor = frame;
        BorderlessFrame.Quiet(hwnd, window.FrameColor);
        return drawn;
    }
}
