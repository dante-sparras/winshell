using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Winshell.Kit;

/// <summary>
/// The chrome brushes, one for each <see cref="KitToken"/>. The palette in effect fills them (<see cref="Apply"/>): Windows' own
/// until a Theme is chosen, then the Theme's. A control that holds one follows a change of palette. A plugin reads these instead of making a color.
/// </summary>
public static class Paint
{
    /// <summary>The bar and panel surface.</summary>
    public static Brush Background => Token(KitToken.Background);

    /// <summary>Primary text and glyphs.</summary>
    public static Brush Foreground => Token(KitToken.Foreground);

    /// <summary>Secondary text.</summary>
    public static Brush Muted => Token(KitToken.Muted);

    /// <summary>A control under the pointer, and the faint layer a card sits on.</summary>
    public static Brush Hover => Token(KitToken.Hover);

    /// <summary>A control being pressed.</summary>
    public static Brush Pressed => Token(KitToken.Pressed);

    /// <summary>The chosen item in a list or a group.</summary>
    public static Brush Selected => Token(KitToken.Selected);

    /// <summary>A hairline: a separator, a card's edge.</summary>
    public static Brush Stroke => Token(KitToken.Stroke);

    /// <summary>A surface that takes the pointer but draws nothing.</summary>
    public static Brush Hit => Token(KitToken.Hit);

    /// <summary>The accent, for a fill.</summary>
    public static Brush Accent => Token(KitToken.Accent);

    /// <summary>The accent, for text and glyphs on the surface.</summary>
    public static Brush AccentText => Token(KitToken.AccentText);

    /// <summary>Text and glyphs on an accent fill.</summary>
    public static Brush OnAccent => Token(KitToken.OnAccent);

    /// <summary>True when the surface is light, so dark ink reads on it.</summary>
    public static bool LightSurface => Tokens.Light;

    /// <summary>A faint wash of the accent, such as behind a heading tile's glyph.</summary>
    public static Brush Tint => Token(KitToken.Tint);

    /// <summary>An accent fill under the pointer: the accent, a little let through, as a Windows accent button dims.</summary>
    public static Brush AccentHover => Token(KitToken.AccentHover);

    /// <summary>An accent fill being pressed: dimmer than <see cref="AccentHover"/>.</summary>
    public static Brush AccentPressed => Token(KitToken.AccentPressed);

    /// <summary>The foreground at about half strength, such as a day from the next month.</summary>
    public static Brush Faded => Token(KitToken.Faded);

    /// <summary>The light of a scannable code, white in every palette, so a camera reads it.</summary>
    public static Brush CodeLight => Token(KitToken.CodeLight);

    /// <summary>The dark of a scannable code, near black in every palette.</summary>
    public static Brush CodeDark => Token(KitToken.CodeDark);

    /// <summary>The empty part of a slider or meter track: the foreground, faint.</summary>
    public static Brush Track => Token(KitToken.Track);

    /// <summary>A fill that draws nothing, such as behind a text box that shows the panel through.</summary>
    public static Brush Clear => Token(KitToken.Clear);

    /// <summary>A fill in <paramref name="tone"/>: the accent for normal, else the palette's green, yellow, or red.</summary>
    /// <param name="tone">The tone.</param>
    /// <returns>The brush. One brush per tone, recolored when the palette changes.</returns>
    public static Brush Fill(PanelTone tone) => tone switch
    {
        PanelTone.Healthy => Token(KitToken.Healthy),
        PanelTone.Warn => Token(KitToken.Warn),
        PanelTone.Critical => Token(KitToken.Critical),
        _ => Accent,
    };

    /// <summary>Text in <paramref name="tone"/>: muted for normal, else the tone's color.</summary>
    /// <param name="tone">The tone.</param>
    /// <returns>The brush.</returns>
    public static Brush Ink(PanelTone tone) => tone == PanelTone.Normal ? Muted : Fill(tone);

    /// <summary>
    /// What a window of Winoma's dresses in: light or dark, and the colors of its frame and its backdrop.
    /// It is the palette the brushes show, so a window and the controls in it never disagree.
    /// </summary>
    public static Chrome Chrome
    {
        get
        {
            var tokens = Tokens;
            return new Chrome(tokens.Light, tokens.HighContrast, Effects, ColorOf(KitToken.Background), ColorOf(KitToken.Foreground), ColorOf(KitToken.Stroke));
        }
    }

    /// <summary>Paints every brush from <paramref name="tokens"/>. Controls that hold a brush change with it.</summary>
    /// <param name="tokens">Every token's color, from the palette in effect.</param>
    /// <param name="effects">True when Windows transparency effects are on.</param>
    /// <returns>False when the brushes already show <paramref name="tokens"/>, so nothing was painted and nothing was raised.</returns>
    public static bool Apply(KitTokens tokens, bool effects)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        // Windows reports a theme change for more than a change of color, such as a new wallpaper with the same accent.
        if (tokens.Equals(Applied) && effects == Effects)
        {
            return false;
        }

        Applied = tokens;
        Effects = effects;
        foreach (var token in Enum.GetValues<KitToken>())
        {
            // A brush nothing has read yet takes its color when it is first read.
            if ((Brushes[(int)token] ??= Declared(token)) is { } brush)
            {
                brush.Color = ColorOf(token);
            }
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Raised after <see cref="Apply"/> repaints the brushes. A drawing that made a brush of its own, such as a window's frame,
    /// draws it again. One that holds the brushes above follows on its own.
    /// </summary>
    public static event Action? Changed;

    /// <summary>A color worked out from other colors, such as a window outline. Not for a color a plugin picks.</summary>
    /// <param name="a">Alpha.</param>
    /// <param name="r">Red.</param>
    /// <param name="g">Green.</param>
    /// <param name="b">Blue.</param>
    /// <returns>The color.</returns>
    public static Color Derived(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    /// <summary>A brush of <see cref="Derived"/> color.</summary>
    /// <param name="color">The color.</param>
    /// <returns>A new brush.</returns>
    public static Brush Solid(Color color) => new SolidColorBrush(color);

    /// <summary>A named style from the app's resources, such as <c>PanelTextStyle</c>.</summary>
    /// <param name="key">The style's key.</param>
    /// <returns>The style.</returns>
    public static Style Style(string key) =>
        Styles.TryGetValue(key, out var style) ? style : Styles[key] = (Style)Application.Current.Resources[key];

    /// <summary>Lets <c>Paint.Style</c> read the app's styles again, after <see cref="Fonts.Use"/> made them anew.</summary>
    internal static void ForgetStyles() => Styles.Clear();

    // One brush for each token, found or made the first time it is read and kept: a row reads about ten of them, and each read
    // was a lookup through the resource dictionary. Apply recolors the same brush, so a control that holds one follows the palette.
    private static readonly SolidColorBrush?[] Brushes = new SolidColorBrush?[Enum.GetValues<KitToken>().Length];
    private static readonly Dictionary<string, Style> Styles = new(StringComparer.Ordinal);
    private static KitTokens? Applied;
    private static KitTokens? Unthemed;
    private static bool Effects;

    /// <summary>
    /// Tokens for the brushes before the first <see cref="Apply"/>. The desktop sets this to the Windows chrome, with no Theme and no Look.
    /// Unset, the brushes stay <see cref="KitTokens.Blank"/> until <see cref="Apply"/>.
    /// </summary>
    internal static Func<KitTokens>? UnthemedTokens { get; set; }

    /// <summary>The tokens the brushes show. Before the first <see cref="Apply"/> they are <see cref="UnthemedTokens"/>, or blank when that is unset.</summary>
    private static KitTokens Tokens =>
        Applied ?? (Unthemed ??= UnthemedTokens?.Invoke() ?? KitTokens.Blank);

    private static SolidColorBrush Token(KitToken token) =>
        Brushes[(int)token] ??= Declared(token) ?? new SolidColorBrush(ColorOf(token));

    /// <summary>
    /// The brush the app's resources declare for <paramref name="token"/>, which its XAML styles hold too. Null for a token only code reads.
    /// </summary>
    private static SolidColorBrush? Declared(KitToken token) => token switch
    {
        KitToken.Background or KitToken.Foreground or KitToken.Muted or KitToken.Hover or KitToken.Pressed or KitToken.Selected
            or KitToken.Stroke or KitToken.Hit or KitToken.Accent or KitToken.AccentText or KitToken.OnAccent =>
            (SolidColorBrush)Application.Current.Resources["Bar" + token + "Brush"],
        _ => null,
    };

    private static Color ColorOf(KitToken token)
    {
        var color = Tokens[token];
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }
}

/// <summary>What a window of Winoma's dresses in (<see cref="Paint.Chrome"/>).</summary>
/// <param name="Light">True when the surface is light.</param>
/// <param name="HighContrast">True when a Windows contrast theme is on. A window then draws no acrylic and no mica.</param>
/// <param name="Effects">True when Windows transparency effects are on.</param>
/// <param name="Background">The surface: a backdrop's tint and a frame's color.</param>
/// <param name="Foreground">Text on the surface.</param>
/// <param name="Stroke">A hairline.</param>
public readonly record struct Chrome(bool Light, bool HighContrast, bool Effects, Color Background, Color Foreground, Color Stroke)
{
    /// <summary>Background as a GDI color reference for the window frame.</summary>
    public int FrameColor => Background.R | (Background.G << 8) | (Background.B << 16);
}
