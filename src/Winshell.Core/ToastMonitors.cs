namespace Winshell.Core;

/// <summary>One monitor's stack of toasts, as the monitor overflow reads it.</summary>
/// <param name="Heights">Each toast's height on this monitor in its own pixels, the newest first. Zero for a toast this monitor does not draw.</param>
/// <param name="Room">The pixels the stack may take on this monitor.</param>
/// <param name="Gap">Pixels between toasts on this monitor, and before the first and after the last.</param>
public readonly record struct ToastColumn(IReadOnlyList<int> Heights, int Room, int Gap);

/// <summary>
/// Toasts are one list, drawn on every monitor: the same toasts at each Bar's outer corner. A click, a dismissal, and a toast's time
/// act on the list, so a toast leaves every monitor together. What differs from monitor to monitor is only what the monitor is:
/// its scale, its room, and whether a window covers it.
/// </summary>
public static class ToastMonitors
{
    /// <summary>
    /// Which monitors draw a toast that just arrived. Every monitor does, except one a window covers, which Windows also keeps
    /// its banners off for a game or a video. A toast that stays until dismissed, such as a call (ADR 0057), is drawn over that window too.
    /// </summary>
    /// <param name="covered">For each Bar, in the order the Bars were made, whether a window covers its monitor.</param>
    /// <param name="stays">True for a toast that stays until it is dismissed.</param>
    /// <returns>One answer for each Bar, in the same order.</returns>
    public static IReadOnlyList<bool> Draws(IReadOnlyList<bool> covered, bool stays)
    {
        ArgumentNullException.ThrowIfNull(covered);
        var draws = new bool[covered.Count];
        for (var i = 0; i < draws.Length; i++)
        {
            draws[i] = stays || !covered[i];
        }

        return draws;
    }

    /// <summary>False when no monitor draws the toast. It is then held back, and its plugin is told it was never shown.</summary>
    /// <param name="draws">What <see cref="Draws"/> answered.</param>
    /// <returns>True when at least one monitor draws it.</returns>
    public static bool Shown(IReadOnlyList<bool> draws)
    {
        ArgumentNullException.ThrowIfNull(draws);
        for (var i = 0; i < draws.Count; i++)
        {
            if (draws[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Which toast leaves when the stack is taller than its room on any monitor. The list is the same on every monitor, so a toast
    /// that does not fit the smallest leaves them all. Which one goes on a monitor is <see cref="Overflowing(IReadOnlyList{int}, IReadOnlyList{bool}, int, int)"/>.
    /// </summary>
    /// <param name="columns">Each monitor's stack. Every one lists the same toasts, the newest first.</param>
    /// <param name="stays">True for each toast that stays until dismissed, in the same order.</param>
    /// <returns>The index of the toast that leaves, or -1 when the stack fits everywhere. Ask again after it has left.</returns>
    public static int Overflowing(IReadOnlyList<ToastColumn> columns, IReadOnlyList<bool> stays)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(stays);
        var heights = new List<int>();
        var staying = new List<bool>();
        var from = new List<int>();
        foreach (var column in columns)
        {
            heights.Clear();
            staying.Clear();
            from.Clear();
            for (var i = 0; i < column.Heights.Count; i++)
            {
                if (column.Heights[i] <= 0)
                {
                    continue;
                }

                heights.Add(column.Heights[i]);
                staying.Add(i < stays.Count && stays[i]);
                from.Add(i);
            }

            var leaves = Overflowing(heights, staying, column.Room, column.Gap);
            if (leaves >= 0)
            {
                return from[leaves];
            }
        }

        return -1;
    }

    /// <summary>
    /// Which toast leaves when the stack is taller than its room. A toast may be as tall as a hero image and four lines make it,
    /// and three of those are more than a small screen holds: the oldest then sat past the edge, still counted as shown.
    /// The oldest toast that does not stay leaves first. A toast that stays until dismissed leaves only
    /// when every older toast stays too. The newest never leaves, so one toast taller than the room is shown as it is.
    /// </summary>
    /// <param name="heights">Each toast's height in pixels, the newest first.</param>
    /// <param name="stays">True for each toast that stays until dismissed, in the same order.</param>
    /// <param name="room">The pixels the stack may take.</param>
    /// <param name="gap">Pixels between toasts, and before the first and after the last.</param>
    /// <returns>The index of the toast that leaves, or -1 when the stack fits. Ask again after it has left.</returns>
    public static int Overflowing(IReadOnlyList<int> heights, IReadOnlyList<bool> stays, int room, int gap)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(stays);
        gap = Math.Max(0, gap);
        long taken = gap;
        for (var index = 0; index < heights.Count; index++)
        {
            taken += Math.Max(1, heights[index]) + gap;
        }

        if (heights.Count < 2 || taken <= room)
        {
            return -1;
        }

        for (var index = heights.Count - 1; index > 0; index--)
        {
            if (index >= stays.Count || !stays[index])
            {
                return index;
            }
        }

        return heights.Count - 1;
    }
}
