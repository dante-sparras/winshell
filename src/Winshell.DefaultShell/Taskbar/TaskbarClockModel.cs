using System.Globalization;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>The time and date the taskbar clock shows. Seconds follow <see cref="Preferences.ClockSeconds"/>.</summary>
public sealed class TaskbarClockModel : ObservableObject
{
    private readonly TimeProvider _time;
    private readonly Preferences _preferences;
    private readonly CultureInfo _culture;
    private string _text = "";
    private string _accessibleName = "";

    public TaskbarClockModel(TimeProvider time, Preferences preferences, CultureInfo? culture = null)
    {
        _time = time;
        _preferences = preferences;
        _culture = culture ?? CultureInfo.CurrentCulture;
    }

    /// <summary>The time, then the short date, on two lines.</summary>
    public string Text => _text;

    /// <summary>What Narrator reads: the time without seconds, then the long date.</summary>
    public string AccessibleName => _accessibleName;

    public void Refresh()
    {
        var local = _time.GetLocalNow().LocalDateTime;
        var time = local.ToString(_preferences.ClockSeconds ? "T" : "t", _culture);
        Set(ref _text, time + "\n" + local.ToString("d", _culture));
        Set(ref _accessibleName, local.ToString("t", _culture) + ", " + local.ToString("D", _culture), nameof(AccessibleName));
    }

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
