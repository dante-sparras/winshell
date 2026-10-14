namespace Winshell.Core;

/// <summary>
/// How long the bar loop waits before its next turn.
/// Events do the urgent work as they happen: a chord, a window shown or moved, the pointer, Super going up.
/// The loop is the safety net behind them. It turns every <see cref="FastMs"/> while something is going on,
/// and once a second when the desktop is idle, just after the wall-clock second so a clock face turns on time.
/// While nobody is there it turns every <see cref="AwayMs"/>, and still just after each wall-clock minute.
/// </summary>
public sealed class BarCadence
{
    /// <summary>The wait while the desktop is busy, in milliseconds.</summary>
    public const long FastMs = 200;

    /// <summary>The longest wait while the desktop is idle, in milliseconds.</summary>
    public const long SlowMs = 1000;

    /// <summary>How long one event keeps the loop at <see cref="FastMs"/>, in milliseconds.</summary>
    public const long ActiveMs = 1500;

    /// <summary>How far past the wall-clock second an idle turn lands, in milliseconds.</summary>
    public const long SecondSkewMs = 10;

    /// <summary>The longest wait while nobody is there, in milliseconds.</summary>
    public const long AwayMs = 5000;

    /// <summary>Seconds without input after which the person counts as away.</summary>
    public const int AwayAfterSeconds = 60;

    /// <summary>How much later than asked a turn may land before the timer is set again, in milliseconds.</summary>
    public const long SlackMs = 25;

    private long _activeUntil = long.MinValue;

    /// <summary>Something happened. The loop stays fast for <see cref="ActiveMs"/> from <paramref name="now"/>.</summary>
    /// <param name="now">Tick count in milliseconds.</param>
    /// <returns>True when the loop was idle and should shorten the wait it is already in.</returns>
    public bool Activity(long now)
    {
        var wasIdle = !Active(now);
        _activeUntil = Math.Max(_activeUntil, now + ActiveMs);
        return wasIdle;
    }

    /// <summary>True while an event in the last <see cref="ActiveMs"/> keeps the loop fast.</summary>
    /// <param name="now">Tick count in milliseconds.</param>
    /// <returns>True while active.</returns>
    public bool Active(long now) => now < _activeUntil;

    /// <summary>The wait before the next turn.</summary>
    /// <param name="now">Tick count in milliseconds.</param>
    /// <param name="busy">True while something on screen needs the fast pace, such as an open panel.</param>
    /// <param name="away">
    /// True while nobody is there: the session is locked, the display is off, or no input came for <see cref="AwayAfterSeconds"/>.
    /// </param>
    /// <param name="wallSecond">The second within the current wall-clock minute, 0 to 59.</param>
    /// <param name="wallMillisecond">The millisecond within the current wall-clock second, 0 to 999.</param>
    /// <returns>
    /// Milliseconds. <see cref="FastMs"/> while busy or active. Otherwise the time to just after the next wall-clock second,
    /// or while away <see cref="AwayMs"/>, cut short so a turn still lands just after the next wall-clock minute.
    /// </returns>
    public long Next(long now, bool busy, bool away, int wallSecond, int wallMillisecond)
    {
        if (busy || Active(now))
        {
            return FastMs;
        }

        var toSecond = 1000 - Math.Clamp(wallMillisecond, 0, 999) + SecondSkewMs;
        if (!away)
        {
            return toSecond < FastMs ? toSecond + 1000 : toSecond;
        }

        var toMinute = ((59 - Math.Clamp(wallSecond, 0, 59)) * 1000L) + toSecond;
        return Math.Min(AwayMs, toMinute);
    }

    /// <summary>
    /// True when a timer that already waits <paramref name="current"/> may keep that wait in place of <paramref name="next"/>.
    /// It may when the turn would land no more than <see cref="SlackMs"/> later, and never when it would land sooner,
    /// so an idle loop does not set its timer again every turn to move it by a millisecond.
    /// </summary>
    /// <param name="current">The wait the timer has, in milliseconds.</param>
    /// <param name="next">The wait <see cref="Next"/> asked for, in milliseconds.</param>
    /// <returns>True when the timer can be left alone.</returns>
    public static bool Keeps(long current, long next) => current >= next && current - next <= SlackMs;
}
