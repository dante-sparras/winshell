namespace Winshell.Core;

/// <summary>
/// The durations every bar, panel, and pill animation uses. They are the Windows control animation durations,
/// so the bar moves at the pace of the shell around it.
/// </summary>
public static class UiMotion
{
    /// <summary>A hover or press wash.</summary>
    public static readonly TimeSpan Faster = TimeSpan.FromMilliseconds(83);

    /// <summary>A small change in place: a fade, a turn, an exit.</summary>
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(167);

    /// <summary>An entrance, such as a panel sliding out of the bar.</summary>
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(250);

    /// <summary>Content replaced in place, such as the calendar turning a month.</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(333);

    /// <summary>
    /// How long <paramref name="duration"/> runs. Zero when Windows animation effects are off,
    /// so the change lands at once.
    /// </summary>
    /// <param name="duration">One of the durations above.</param>
    /// <param name="animations">Windows animation effects.</param>
    /// <returns><paramref name="duration"/>, or zero.</returns>
    public static TimeSpan For(TimeSpan duration, bool animations) => animations ? duration : TimeSpan.Zero;
}
