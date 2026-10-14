namespace Winshell.Kit;

/// <summary>
/// Text and glyph sizes from the Windows 11 type ramp, in device-independent pixels.
/// The names are the Windows ramp's, because the chrome is Windows (ADR 0016).
/// </summary>
public static class TypeRamp
{
    /// <summary>Secondary text: a caption, a detail line, a muted label.</summary>
    public static double Caption => Look.Current.CaptionSize;

    /// <summary>Primary text in a row.</summary>
    public static double Body => Look.Current.BodySize;

    /// <summary>Primary text that leads a row, drawn semibold.</summary>
    public static double BodyStrong => Look.Current.BodySize;

    /// <summary>Text a little larger than a row's, such as what is typed into a search line.</summary>
    public const double BodyLarge = 18;

    /// <summary>A panel heading or the name at the top of a panel.</summary>
    public const double Subtitle = 20;

    /// <summary>A large figure.</summary>
    public const double Title = 28;

    /// <summary>The headline figure of a panel, such as the temperature.</summary>
    public const double TitleLarge = 40;

    /// <summary>The largest figure.</summary>
    public const double Display = 68;

    /// <summary>A Segoe Fluent glyph on the bar or in a row.</summary>
    public const double Icon = 16;

    /// <summary>The glyph in a heading tile.</summary>
    public const double IconLarge = 22;
}
