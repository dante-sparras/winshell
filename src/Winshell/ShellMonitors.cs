namespace Winshell;

/// <summary>Which bars there are for the monitors Windows lists. One on each, and the first bar stays.</summary>
internal static class ShellMonitors
{
    /// <summary>What becomes of the bars when the monitors are <paramref name="screens"/>.</summary>
    /// <param name="bars">The monitor each bar is on now, in the order the bars were made.</param>
    /// <param name="screens">The monitors Windows lists now. Empty changes nothing.</param>
    /// <returns>The monitor each existing bar keeps, or null when that bar is removed, and the monitors that get a new bar.</returns>
    public static ShellPlan Follow(IReadOnlyList<ShellRect> bars, IReadOnlyList<ShellRect> screens)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(screens);
        var kept = new ShellRect?[bars.Count];
        if (screens.Count == 0)
        {
            for (var i = 0; i < kept.Length; i++)
            {
                kept[i] = bars[i];
            }

            return new ShellPlan(kept, [], Changed: false);
        }

        var stay = Math.Min(bars.Count, screens.Count);
        var free = new List<ShellRect>(screens);
        var moved = false;
        for (var i = 0; i < stay; i++)
        {
            if (free.Remove(bars[i]))
            {
                kept[i] = bars[i];
            }
        }

        for (var i = 0; i < stay; i++)
        {
            if (kept[i] is not null)
            {
                continue;
            }

            var best = 0;
            long most = 0;
            for (var at = 0; at < free.Count; at++)
            {
                var shared = Shared(bars[i], free[at]);
                if (shared > most)
                {
                    most = shared;
                    best = at;
                }
            }

            kept[i] = free[best];
            free.RemoveAt(best);
            moved = true;
        }

        return new ShellPlan(kept, free, moved || free.Count > 0 || stay < bars.Count);
    }

    private static long Shared(ShellRect one, ShellRect other)
    {
        var width = Math.Min((long)one.X + one.Width, (long)other.X + other.Width) - Math.Max(one.X, other.X);
        var height = Math.Min((long)one.Y + one.Height, (long)other.Y + other.Height) - Math.Max(one.Y, other.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }
}

/// <summary>What <see cref="ShellMonitors.Follow"/> decided.</summary>
/// <param name="Kept">One entry for each bar there was. Null when that bar is removed.</param>
/// <param name="Made">The monitors that get a new bar.</param>
/// <param name="Changed">False when every bar stays where it is and none is made or removed.</param>
internal sealed record ShellPlan(IReadOnlyList<ShellRect?> Kept, IReadOnlyList<ShellRect> Made, bool Changed);

/// <summary>When the shell looks again at whether a window covers a bar.</summary>
internal static class ShellLayer
{
    /// <summary>How old the last look may be before the shell looks again with nothing covering the bar, in milliseconds.</summary>
    public const long StaleMs = 5000;

    /// <summary>True when the shell should look for a covering window now.</summary>
    /// <param name="covered">True while the bar is behind a window.</param>
    /// <param name="fullscreen">True when a fullscreen window is on the bar's monitor.</param>
    /// <param name="sinceLookMs">Milliseconds since the last look.</param>
    /// <returns>True to look now.</returns>
    public static bool LooksAgain(bool covered, bool fullscreen, long sinceLookMs) =>
        covered || fullscreen || sinceLookMs >= StaleMs;
}
