namespace Winshell.Core;

/// <summary>What a bar, panel, toast, or pill is drawn on.</summary>
public enum SurfaceMaterial
{
    /// <summary>A flat fill of the background token.</summary>
    Solid,

    /// <summary>The acrylic the Host asked for, such as a transparent bar.</summary>
    Acrylic,

    /// <summary>Windows mica, when the machine can draw it.</summary>
    Mica,
}

/// <summary>
/// The look an extension hands the shell. The extension works the theme out.
/// The shell paints these tokens and this material, and does not read a theme catalog.
/// An empty <see cref="FontFace"/> is Segoe UI Variable.
/// The five sizes are what <c>Look.Use</c> draws with. The extension fills them from its look.
/// </summary>
public sealed class SurfaceLook
{
    /// <summary>Creates a look. A blank theme id is stored as none.</summary>
    /// <param name="tokens">The Kit colors.</param>
    /// <param name="material">What the windows ask to be drawn on.</param>
    /// <param name="effects">True when Windows transparency effects are on.</param>
    /// <param name="themeId">The chosen theme's id, or null when Windows chrome is in effect.</param>
    /// <param name="fontFace">The text face. Empty is Segoe UI Variable.</param>
    /// <param name="bodySize">Primary text, in device-independent pixels. The Windows size is 14.</param>
    /// <param name="captionSize">Secondary text. The Windows size is 12.</param>
    /// <param name="cornerSize">A panel corner. The Windows size is 8.</param>
    /// <param name="controlCorner">A button or a row. The Windows size is 4.</param>
    /// <param name="gutterSize">Space between a panel's edge and its content. The Windows size is 16.</param>
    public SurfaceLook(
        KitTokens tokens,
        SurfaceMaterial material,
        bool effects,
        string? themeId,
        string fontFace,
        double bodySize = 14,
        double captionSize = 12,
        double cornerSize = 8,
        double controlCorner = 4,
        double gutterSize = 16)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        Tokens = tokens;
        Material = material;
        Effects = effects;
        ThemeId = string.IsNullOrWhiteSpace(themeId) ? null : themeId;
        FontFace = fontFace ?? "";
        BodySize = bodySize;
        CaptionSize = captionSize;
        CornerSize = cornerSize;
        ControlCorner = controlCorner;
        GutterSize = gutterSize;
    }

    /// <summary>Solid, blank tokens, effects off, no theme, the default face, and the Windows sizes. A shell with no extension paints this.</summary>
    public static SurfaceLook Shell { get; } = new(KitTokens.Blank, SurfaceMaterial.Solid, effects: false, themeId: null, fontFace: "");

    /// <summary>The Kit colors.</summary>
    public KitTokens Tokens { get; }

    /// <summary>What the windows ask to be drawn on. High contrast and a machine that cannot draw it still land on solid.</summary>
    public SurfaceMaterial Material { get; }

    /// <summary>True when Windows transparency effects are on.</summary>
    public bool Effects { get; }

    /// <summary>The chosen theme's id, or null when Windows chrome is in effect.</summary>
    public string? ThemeId { get; }

    /// <summary>The text face. Empty is Segoe UI Variable.</summary>
    public string FontFace { get; }

    /// <summary>Primary text, in device-independent pixels.</summary>
    public double BodySize { get; }

    /// <summary>Secondary text, in device-independent pixels.</summary>
    public double CaptionSize { get; }

    /// <summary>A panel corner, in device-independent pixels.</summary>
    public double CornerSize { get; }

    /// <summary>A button or a row, in device-independent pixels.</summary>
    public double ControlCorner { get; }

    /// <summary>Space between a panel's edge and its content, in device-independent pixels.</summary>
    public double GutterSize { get; }
}
