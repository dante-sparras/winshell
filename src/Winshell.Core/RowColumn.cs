namespace Winshell.Core;

/// <summary>One edge of a row in a panel's column: what the row above or below it meets.</summary>
/// <param name="Margin">The empty space between the row's edge and what it draws there.</param>
/// <param name="Separator">True when what it draws there is a separator's line.</param>
public readonly record struct RowEdge(double Margin, bool Separator);

/// <summary>A row as a panel's column lays it out.</summary>
/// <param name="Height">Its height, its own margins included. A row that draws nothing has none.</param>
/// <param name="Top">Its top edge.</param>
/// <param name="Bottom">Its bottom edge.</param>
public readonly record struct RowBox(double Height, RowEdge Top, RowEdge Bottom);

/// <summary>Where a column put its rows.</summary>
/// <param name="Tops">The top of each row, in order.</param>
/// <param name="Height">The height of the column.</param>
public sealed record RowLayout(IReadOnlyList<double> Tops, double Height);

/// <summary>
/// Where each row of a panel's column goes. Rows sit one row gap apart. A separator's line sits one separator gap from
/// what the row beside it draws, whatever margin either of them carries, so every line in every panel has the same room.
/// </summary>
public static class RowColumn
{
    /// <summary>The space between two rows that follow each other.</summary>
    /// <param name="above">The bottom edge of the row above.</param>
    /// <param name="below">The top edge of the row below.</param>
    /// <param name="rowGap">The space between two rows.</param>
    /// <param name="separatorGap">The space between a separator's line and what the row beside it draws.</param>
    /// <returns>The space between the two rows' edges. It is negative when their margins already hold more than the separator gap.</returns>
    public static double Gap(RowEdge above, RowEdge below, double rowGap, double separatorGap) =>
        above.Separator || below.Separator ? separatorGap - above.Margin - below.Margin : rowGap;

    /// <summary>Stacks <paramref name="rows"/> top to bottom. A row with no height takes no room and no gap.</summary>
    /// <param name="rows">The rows, in order.</param>
    /// <param name="rowGap">The space between two rows.</param>
    /// <param name="separatorGap">The space between a separator's line and what the row beside it draws.</param>
    /// <returns>The top of each row and the column's height.</returns>
    public static RowLayout Lay(IReadOnlyList<RowBox> rows, double rowGap, double separatorGap)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var tops = new double[rows.Count];
        var at = 0d;
        RowEdge? above = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Height <= 0)
            {
                tops[i] = at;
                continue;
            }

            if (above is { } edge)
            {
                at += Gap(edge, row.Top, rowGap, separatorGap);
            }

            tops[i] = at;
            at += row.Height;
            above = row.Bottom;
        }

        return new RowLayout(tops, Math.Max(0, at));
    }
}
