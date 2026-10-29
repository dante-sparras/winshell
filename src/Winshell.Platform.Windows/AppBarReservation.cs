using Winshell.Platform.Windows.Native;

namespace Winshell.Platform.Windows;

/// <summary>
/// The rectangle a panel's app bar holds, and when to ask Explorer for another. Explorer announces a new work area
/// after a reservation moves, which places every panel again, so asking for what is already held would never end.
/// Explorer may also grant less than was asked, as when another app bar takes the space in between; once that bar
/// is gone, the next request differs from the grant and is asked for again.
/// </summary>
internal sealed class AppBarReservation
{
    /// <summary>What Explorer granted last, or empty before the first request.</summary>
    public RECT Granted { get; private set; }

    /// <summary>
    /// Asks for <paramref name="desired"/> with <paramref name="setPos"/> (ABM_SETPOS, which returns the grant),
    /// unless that is exactly what is held. Returns whether the grant moved, and with it the work area.
    /// </summary>
    public bool Reserve(RECT desired, Func<RECT, RECT> setPos)
    {
        if (desired.Equals(Granted))
            return false;
        var granted = setPos(desired);
        var moved = !granted.Equals(Granted);
        Granted = granted;
        return moved;
    }

    /// <summary>The app bar was removed: the next request is asked for.</summary>
    public void Clear() => Granted = default;
}
