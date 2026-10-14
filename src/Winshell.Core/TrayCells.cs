namespace Winshell.Core;

/// <summary>
/// The hidden-icon grid under the tray chevron. The panel walks the visual tree.
/// This is only which cell a point lands on, in device-independent pixels from the grid's top left.
/// </summary>
public static class TrayCells
{
    /// <summary>Icon button edge. Matches the icons on the bar.</summary>
    public const int Hit = 32;

    /// <summary>Space between icon buttons.</summary>
    public const int Gap = 4;

    /// <summary>Icons in one row before the grid wraps.</summary>
    public const int MaxColumns = 4;

    /// <summary>The automation id of the grid the panel hit-tests.</summary>
    public const string AutomationId = "tray-overflow";

    /// <summary>
    /// The cell a drop at <paramref name="x"/>, <paramref name="y"/> lands on, among <paramref name="count"/> icons.
    /// A point past the last icon lands one past the end, which appends.
    /// </summary>
    /// <param name="x">Distance from the grid's left edge.</param>
    /// <param name="y">Distance from the grid's top edge.</param>
    /// <param name="count">Icons in the grid. Zero is the empty panel, and the drop lands at the start.</param>
    /// <returns>The index to land on.</returns>
    public static int Cell(double x, double y, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var stride = Hit + Gap;
        var columns = Math.Max(1, ColumnCount(count));
        var column = (int)Math.Floor(x / stride);
        var row = (int)Math.Floor(y / stride);
        if (column < 0)
        {
            column = 0;
        }
        else if (column >= columns)
        {
            column = columns - 1;
        }

        if (row < 0)
        {
            row = 0;
        }

        var index = (row * columns) + column;
        return index >= count ? count : index;
    }

    /// <summary>
    /// The icon under <paramref name="x"/>, <paramref name="y"/>, or -1 when the point misses every icon.
    /// A point in the gap between icons misses.
    /// </summary>
    /// <param name="x">Distance from the grid's left edge.</param>
    /// <param name="y">Distance from the grid's top edge.</param>
    /// <param name="count">Icons in the grid.</param>
    /// <returns>The icon index, or -1.</returns>
    public static int IconAt(double x, double y, int count)
    {
        if (count <= 0 || x < 0 || y < 0)
        {
            return -1;
        }

        var stride = Hit + Gap;
        var columns = Math.Max(1, ColumnCount(count));
        var column = (int)Math.Floor(x / stride);
        var row = (int)Math.Floor(y / stride);
        if (column < 0 || column >= columns || row < 0)
        {
            return -1;
        }

        if ((x - (column * stride)) >= Hit || (y - (row * stride)) >= Hit)
        {
            return -1;
        }

        var index = (row * columns) + column;
        return index < count ? index : -1;
    }

    /// <summary>Columns for <paramref name="icons"/> icons. Zero when there are none.</summary>
    /// <param name="icons">Icons in the grid.</param>
    /// <returns>The column count.</returns>
    public static int ColumnCount(int icons)
    {
        if (icons <= 0)
        {
            return 0;
        }

        return Math.Min(MaxColumns, icons);
    }

    /// <summary>Rows for <paramref name="icons"/> icons.</summary>
    /// <param name="icons">Icons in the grid.</param>
    /// <returns>The row count.</returns>
    public static int RowCount(int icons)
    {
        var columns = ColumnCount(icons);
        return columns == 0 ? 0 : (icons + columns - 1) / columns;
    }

    /// <summary>Panel width: the grid, and <paramref name="gutter"/> on its left and its right.</summary>
    /// <param name="icons">Icons in the grid.</param>
    /// <param name="gutter">The space the panel keeps between its edge and its content.</param>
    /// <returns>The width, or zero for no icons.</returns>
    public static int Width(int icons, double gutter) => Outer(ColumnCount(icons), gutter);

    /// <summary>Panel height: the grid, and <paramref name="gutter"/> above and below it.</summary>
    /// <param name="icons">Icons in the grid.</param>
    /// <param name="gutter">The space the panel keeps between its edge and its content.</param>
    /// <returns>The height, or zero for no icons.</returns>
    public static int Height(int icons, double gutter) => Outer(RowCount(icons), gutter);

    private static int Outer(int cells, double gutter)
    {
        if (cells <= 0)
        {
            return 0;
        }

        return (cells * Hit) + ((cells - 1) * Gap) + (int)Math.Ceiling(Math.Max(0, gutter) * 2);
    }
}
