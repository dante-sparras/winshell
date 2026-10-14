namespace Winshell.Core;

/// <summary>A color with an alpha, as a Kit brush holds one.</summary>
/// <param name="A">Alpha, 0 for clear through 255 for opaque.</param>
/// <param name="R">Red, 0 through 255.</param>
/// <param name="G">Green, 0 through 255.</param>
/// <param name="B">Blue, 0 through 255.</param>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    /// <summary>The color without its alpha.</summary>
    public RgbColor Rgb => new(R, G, B);
}

/// <summary>
/// Every color token the Kit has (ADR 0045). The Kit's <c>Paint</c> has one brush for each, and a test fails when it has
/// a brush this does not name. A new token is a value here and an arm where the product fills <see cref="KitTokens"/>.
/// </summary>
public enum KitToken
{
    /// <summary>The bar and panel surface.</summary>
    Background,

    /// <summary>Primary text and glyphs.</summary>
    Foreground,

    /// <summary>Secondary text.</summary>
    Muted,

    /// <summary>A control under the pointer, and the faint layer a card sits on.</summary>
    Hover,

    /// <summary>A control being pressed.</summary>
    Pressed,

    /// <summary>The chosen item in a list or a group.</summary>
    Selected,

    /// <summary>A hairline: a separator, a card's edge.</summary>
    Stroke,

    /// <summary>A surface that takes the pointer but draws nothing. The same in every palette.</summary>
    Hit,

    /// <summary>The accent, for a fill.</summary>
    Accent,

    /// <summary>The accent, for text and glyphs on the surface.</summary>
    AccentText,

    /// <summary>Text and glyphs on an accent fill.</summary>
    OnAccent,

    /// <summary>A faint wash of the accent, such as behind a heading tile's glyph.</summary>
    Tint,

    /// <summary>An accent fill under the pointer.</summary>
    AccentHover,

    /// <summary>An accent fill being pressed.</summary>
    AccentPressed,

    /// <summary>The foreground at about half strength, such as a day from the next month.</summary>
    Faded,

    /// <summary>The empty part of a slider or meter track.</summary>
    Track,

    /// <summary>The light of a scannable code. White in every palette, so a camera reads it.</summary>
    CodeLight,

    /// <summary>The dark of a scannable code. Near black in every palette.</summary>
    CodeDark,

    /// <summary>A fill that draws nothing. The same in every palette.</summary>
    Clear,

    /// <summary>The healthy tone: the palette's green.</summary>
    Healthy,

    /// <summary>The warning tone: the palette's yellow.</summary>
    Warn,

    /// <summary>The critical tone: the palette's red.</summary>
    Critical,
}

/// <summary>
/// The color of every Kit token, filled from one palette. The Kit paints its brushes from this and from nothing else,
/// so the Bar, the Panels, the Menu, and the Toasts are one palette.
/// </summary>
public sealed class KitTokens : IEquatable<KitTokens>
{
    internal const byte HoverAlpha = 16;
    internal const byte PressedAlpha = 24;
    internal const byte SelectedAlpha = 32;
    internal const byte TintAlpha = 40;
    internal const byte AccentHoverAlpha = 230;
    internal const byte AccentPressedAlpha = 204;
    internal const byte FadedAlpha = 140;
    internal const byte TrackAlpha = 72;

    private readonly ArgbColor[] _colors;

    /// <summary>Every token clear, on a dark surface. The Kit shows this until a product paints.</summary>
    internal static KitTokens Blank { get; } = new(light: false, highContrast: false, static _ => default);

    internal KitTokens(bool light, bool highContrast, Func<KitToken, ArgbColor> color)
    {
        Light = light;
        HighContrast = highContrast;
        var tokens = Enum.GetValues<KitToken>();
        _colors = new ArgbColor[tokens.Length];
        foreach (var token in tokens)
        {
            _colors[(int)token] = color(token);
        }
    }

    /// <summary>True when the surface is light, so dark ink reads on it.</summary>
    public bool Light { get; }

    /// <summary>True when the colors are a Windows contrast theme's. A surface then draws no acrylic and no mica.</summary>
    public bool HighContrast { get; }

    /// <summary>The color of <paramref name="token"/>.</summary>
    /// <param name="token">The token.</param>
    /// <returns>Its color.</returns>
    public ArgbColor this[KitToken token] => _colors[(int)token];

    /// <inheritdoc/>
    public bool Equals(KitTokens? other) =>
        other is not null && Light == other.Light && HighContrast == other.HighContrast && _colors.AsSpan().SequenceEqual(other._colors);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as KitTokens);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Light);
        hash.Add(HighContrast);
        foreach (var color in _colors)
        {
            hash.Add(color);
        }

        return hash.ToHashCode();
    }

    /// <summary>The tokens every palette works out the same way: the washes of the accent and of the ink, and the four that never change.</summary>
    internal static ArgbColor Shared(KitToken token, RgbColor foreground, RgbColor accent) => token switch
    {
        KitToken.Tint => Faint(accent, TintAlpha),
        KitToken.AccentHover => Faint(accent, AccentHoverAlpha),
        KitToken.AccentPressed => Faint(accent, AccentPressedAlpha),
        KitToken.Faded => Faint(foreground, FadedAlpha),
        KitToken.Track => Faint(foreground, TrackAlpha),
        KitToken.Hit => Faint(RgbColor.White, 1),
        KitToken.Clear => Faint(RgbColor.White, 0),
        KitToken.CodeLight => Opaque(RgbColor.White),
        KitToken.CodeDark => new ArgbColor(255, 16, 16, 16),
        _ => throw new InvalidOperationException("No palette fills the Kit token " + token + "."),
    };

    internal static ArgbColor Opaque(RgbColor color) => new(255, color.R, color.G, color.B);

    internal static ArgbColor Faint(RgbColor color, byte alpha) => new(alpha, color.R, color.G, color.B);
}
