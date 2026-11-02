using System.Globalization;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>The time and date the taskbar clock shows. Seconds follow <see cref="Preferences.ClockSeconds"/>.</summary>
public sealed class TaskbarClockModel
{
    private readonly TimeProvider _time;
    private readonly Preferences _preferences;
    private readonly CultureInfo _culture;

    public TaskbarClockModel(TimeProvider time, Preferences preferences, CultureInfo? culture = null)
    {
        _time = time;
        _preferences = preferences;
        _culture = culture ?? CultureInfo.CurrentCulture;
    }

    /// <summary>The time, then the short date, on two lines.</summary>
    public string Text { get; private set; } = "";

    /// <summary>What Narrator reads: the time without seconds, the long date, and the notification count when there is one.</summary>
    public string AccessibleName { get; private set; } = "";

    public void Refresh(int notificationCount = 0)
    {
        var local = _time.GetLocalNow().LocalDateTime;
        var time = local.ToString(_preferences.ClockSeconds ? "T" : "t", _culture);
        Text = time + "\n" + local.ToString("d", _culture);
        var name = local.ToString("t", _culture) + ", " + local.ToString("D", _culture);
        if (notificationCount > 0)
            name += ", " + notificationCount.ToString(_culture) + (notificationCount == 1 ? " notification" : " notifications");
        AccessibleName = name;
    }

    /// <summary>
    /// Whether a click on the clock is the one that closed its popup. A press outside the popup dismisses it, so a
    /// press on the clock does, and that click arrives when the button comes up. It must not open the popup again.
    /// </summary>
    public static bool ClosedBySameClick(DateTimeOffset dismissed, DateTimeOffset now) =>
        now - dismissed < TimeSpan.FromMilliseconds(500);

    /// <summary>How long until the next second, or the next minute when seconds are hidden. Never zero.</summary>
    public static TimeSpan UntilNext(DateTimeOffset now, bool seconds)
    {
        var wait = seconds
            ? TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(now.Millisecond)
            : TimeSpan.FromMinutes(1) - TimeSpan.FromSeconds(now.Second) - TimeSpan.FromMilliseconds(now.Millisecond);
        var step = seconds ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
        if (wait < TimeSpan.FromMilliseconds(50))
            wait += step;
        return wait;
    }
}
