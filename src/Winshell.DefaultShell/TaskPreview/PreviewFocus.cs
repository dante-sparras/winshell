namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// Where the keyboard is in an open preview: a window, or that window's close button. A move past the first or the
/// last window stays where it is.
/// </summary>
internal readonly record struct PreviewFocus(int Index, bool Close)
{
    /// <summary>
    /// One step through the windows and their close buttons in order: a window, its close button, then the next window.
    /// Left and Right on cards step this way, and so does Tab on cards or a list.
    /// </summary>
    public PreviewFocus Step(int direction, int count)
    {
        var slot = Index * 2 + (Close ? 1 : 0);
        var next = slot + (direction > 0 ? 1 : -1);
        if ((uint)next >= (uint)(count * 2))
            return this;
        return new PreviewFocus(next / 2, next % 2 == 1);
    }

    /// <summary>Up and Down on a list move between rows. The close button is skipped, except Up from it returns to its row.</summary>
    public PreviewFocus Vertical(int direction, int count)
    {
        if (Close)
        {
            if (direction < 0)
                return this with { Close = false };
            return Index + 1 >= count ? this : new PreviewFocus(Index + 1, false);
        }
        var next = Index + (direction > 0 ? 1 : -1);
        return next < 0 || next >= count ? this : new PreviewFocus(next, false);
    }

    /// <summary>
    /// Where the keyboard lands when the windows are read again: on the same window when it is still there, at
    /// <paramref name="found"/>, and on its close button only when the layout did not change. Otherwise the first window.
    /// </summary>
    public static PreviewFocus Restore(int found, bool keepClose, bool layoutChanged) =>
        found >= 0 ? new PreviewFocus(found, keepClose && !layoutChanged) : default;

    /// <summary>The scroll that shows row <paramref name="index"/> of a list fully. A row already in view leaves it.</summary>
    public static double Reveal(int index, double scroll, double viewport)
    {
        var top = index * (double)TaskPreview.RowHeight;
        if (top < scroll)
            return top;
        if (top + TaskPreview.RowHeight > scroll + viewport)
            return top + TaskPreview.RowHeight - viewport;
        return scroll;
    }
}
