using Microsoft.UI.Xaml;

namespace Winshell.Kit;

/// <summary>
/// Space between and inside things, in device-independent pixels. The names follow Omarchy's <c>Style.spacing</c>.
/// Every row in a panel starts on one column, <see cref="PanelPadding"/> in from the panel's edge.
/// </summary>
public static class Spacing
{
    /// <summary>The space between a panel's edge and its content, on every side. The Host look's gutter.</summary>
    public static double PanelPadding => Look.Current.GutterSize;

    /// <summary>The space between a panel's groups, such as under its heading.</summary>
    public const double PanelGap = 8;

    /// <summary>The space between rows in a panel.</summary>
    public const double RowGap = 4;

    /// <summary>The space between a separator's line and what the row on either side of it draws.</summary>
    public const double SeparatorGap = PanelGap + RowGap;

    /// <summary>The space inside a row that draws its own surface, left and right, so its text clears the fill.</summary>
    public const double RowPaddingX = 12;

    /// <summary>The space inside a row that draws its own surface, top and bottom.</summary>
    public const double RowPaddingY = 8;

    /// <summary>The space inside a button or chip, left and right.</summary>
    public const double ControlPaddingX = 12;

    /// <summary>The space inside a button or chip, top and bottom.</summary>
    public const double ControlPaddingY = 6;

    /// <summary>The space between an icon and its label inside a control or row.</summary>
    public const double ControlGap = 12;

    /// <summary>The space between a label and the line or control under it.</summary>
    public const double LabelGap = 2;

    /// <summary>The space inside a floating card that is not a panel, such as a toast or the pill.</summary>
    public const double PopupPadding = 12;

    /// <summary>The edge of a heading tile: the icon or mark at the top of a panel.</summary>
    public const double Tile = 44;

    /// <summary>The mark that starts a row in a card: a glyph, a logo, or a check.</summary>
    public const double Mark = 20;

    /// <summary>The height of a bar item on a horizontal bar, and the width of a glyph item.</summary>
    public const double BarItem = 40;

    /// <summary>The smallest edge of a bar item on a vertical bar, and of a workspace chip there.</summary>
    public const double BarItemThin = 32;

    /// <summary>The space between the bar's edge and its items: less across the bar than along it.</summary>
    /// <param name="vertical">True for a bar on the left or right edge.</param>
    /// <returns>The bar's padding.</returns>
    public static Thickness Bar(bool vertical) => vertical ? new(RowGap, PanelGap, RowGap, PanelGap) : new(PanelGap, RowGap, PanelGap, RowGap);

    /// <summary>The space inside a bar item around what it draws: along a vertical bar, or beside text on a horizontal one.</summary>
    /// <param name="vertical">True for a bar on the left or right edge.</param>
    /// <returns>The item's padding.</returns>
    public static Thickness BarItemPadding(bool vertical) => vertical ? new(0, RowGap, 0, RowGap) : new(10, 0, 10, 0);

    /// <summary>The padding inside a toast: the gutter on the outer sides, less where its close button sits.</summary>
    public static Thickness Toast => new(16, PopupPadding, PopupPadding, 16);

    /// <summary>The one-pixel edge a card, a box, or a chip draws.</summary>
    public static Thickness Hairline => new(1);

    /// <summary>The padding inside a row that draws its own surface.</summary>
    public static Thickness Row => new(RowPaddingX, RowPaddingY, RowPaddingX, RowPaddingY);

    /// <summary>The padding inside a card: a group of figures, a settings card, an app's volume.</summary>
    public static Thickness Card => new(RowPaddingX, 10, RowPaddingX, RowPaddingX);
}
