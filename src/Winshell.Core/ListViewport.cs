namespace Winshell.Core;

/// <summary>
/// Which rows of a long list a fixed pool of rows shows. The pool draws <see cref="Capacity"/> rows starting at <see cref="Top"/>,
/// so a list of any length costs the rows on screen and no more. The top never leaves the list, and never leaves room under
/// the last row while there are rows above to fill it.
/// </summary>
public sealed class ListViewport
{
    /// <summary>The index of the first row shown.</summary>
    public int Top { get; private set; }

    /// <summary>How many rows fit. Zero until the pool has been measured.</summary>
    public int Capacity { get; private set; }

    /// <summary>Goes back to the first row, as when another list takes the pool.</summary>
    public void Reset() => Top = 0;

    /// <summary>The pool was measured. The top is held inside a list of <paramref name="count"/> rows.</summary>
    /// <param name="capacity">How many rows fit now. A negative number is none.</param>
    /// <param name="count">How many rows the list has.</param>
    public void Resize(int capacity, int count)
    {
        Capacity = Math.Max(0, capacity);
        Clamp(count);
    }

    /// <summary>Holds the top inside a list of <paramref name="count"/> rows, as when the list got shorter.</summary>
    /// <param name="count">How many rows the list has.</param>
    public void Clamp(int count) => Top = Math.Clamp(Top, 0, Last(count));

    /// <summary>Moves the top by <paramref name="rows"/>, as the wheel does.</summary>
    /// <param name="rows">Negative for up.</param>
    /// <param name="count">How many rows the list has.</param>
    public void Scroll(int rows, int count) => Top = Math.Clamp(Top + rows, 0, Last(count));

    /// <summary>Puts the top at <paramref name="index"/>, or as near as the list allows, as a drag of the scroll thumb does.</summary>
    /// <param name="index">The row to start at.</param>
    /// <param name="count">How many rows the list has.</param>
    public void Jump(int index, int count) => Top = Math.Clamp(index, 0, Last(count));

    /// <summary>Moves the top just far enough that row <paramref name="index"/> is shown, as an arrow key does.</summary>
    /// <param name="index">The row to show. A row outside the list changes nothing.</param>
    /// <param name="count">How many rows the list has.</param>
    public void Reveal(int index, int count)
    {
        if (index < 0 || index >= count)
        {
            Clamp(count);
            return;
        }

        if (index < Top)
        {
            Top = index;
        }
        else if (Capacity > 0 && index >= Top + Capacity)
        {
            Top = index - Capacity + 1;
        }

        Clamp(count);
    }

    /// <summary>
    /// Where the scroll thumb sits on its track, as shares of the track from 0 to 1.
    /// Null when every row fits, so there is nothing to scroll.
    /// </summary>
    /// <param name="count">How many rows the list has.</param>
    /// <returns>Where the thumb starts and how long it is.</returns>
    public (double Start, double Length)? Thumb(int count)
    {
        if (Capacity <= 0 || count <= Capacity)
        {
            return null;
        }

        return (Top / (double)count, Capacity / (double)count);
    }

    private int Last(int count) => Math.Max(0, count - Math.Max(1, Capacity));
}
