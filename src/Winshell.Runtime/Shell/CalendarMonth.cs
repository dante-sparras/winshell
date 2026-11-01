using System.Globalization;

namespace Winshell.Runtime.Shell;

/// <summary>A Gregorian month grid using the user's regional weekday order and week numbering rule.</summary>
public sealed class CalendarMonth : QObject
{
    private DateTime _date = TimeProvider.System.GetLocalNow().LocalDateTime.Date;
    private DateTime _selectedDate = TimeProvider.System.GetLocalNow().LocalDateTime.Date;
    private int _monthOffset;
    private CultureInfo _culture = GregorianCulture(CultureInfo.CurrentCulture);
    private readonly CalendarDay[] _days = Enumerable.Range(0, 42).Select(_ => new CalendarDay()).ToArray();

    public CalendarMonth()
    {
        Days = new ObjectModel();
        Days.SetOwner(this);
        foreach (var day in _days)
            day.SetOwner(this);
        Days.Set(_days);
        Rebuild();
    }

    [Qml] public ObjectModel Days { get; }
    [Qml] public string[] WeekdayLabels { get { Track(); return Enumerable.Range(0, 7).Select(i => _culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((FirstDayOfWeek + i) % 7))).ToArray(); } }
    [Qml] public int[] WeekNumbers { get { Track(); return Enumerable.Range(0, 6).Select(i => WeekNumber(_days[i * 7].Date)).ToArray(); } }
    [Qml] public int FirstDayOfWeek { get { Track(); return (int)_culture.DateTimeFormat.FirstDayOfWeek; } }
    [Qml] public string MonthLabel { get { Track(); return Shown.ToString("Y", _culture); } }
    [Qml] public string DateLabel { get { Track(); return _date.ToString("D", _culture); } }
    [Qml] public string TimeLabel { get { Track(); return _date.ToString("t", _culture); } }
    [Qml] public string LongTimeLabel { get { Track(); return _date.ToString("T", _culture); } }
    [Qml] public string ShortDateLabel { get { Track(); return _date.ToString("d", _culture); } }
    [Qml] public DateTime SelectedDate { get { Track(); return _selectedDate; } }

    [Qml]
    public DateTime Date
    {
        get { Track(); return _date; }
        set
        {
            // QML date properties begin as an invalid Date while their bindings are being installed.
            if (value == DateTime.MinValue)
                return;
            // JavaScript Date is converted to UTC by Jint. Calendar days are local civil dates.
            var local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
            if (SetField(ref _date, local))
            {
                _monthOffset = ClampOffset(_monthOffset);
                NotifyChanged("monthOffset");
                Rebuild();
            }
        }
    }

    [Qml]
    public int MonthOffset
    {
        get { Track(); return _monthOffset; }
        set { if (SetField(ref _monthOffset, ClampOffset(value))) Rebuild(); }
    }

    /// <summary>Empty means the process's current Windows regional format; invalid names are ignored.</summary>
    [Qml]
    public string LocaleName
    {
        get { Track(); return _culture.Name; }
        set
        {
            CultureInfo culture;
            try { culture = GregorianCulture(string.IsNullOrEmpty(value) ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo(value)); }
            catch (CultureNotFoundException) { return; }
            _culture = culture;
            NotifyChanged("localeName");
            Rebuild();
        }
    }

    [Qml] public void Reset()
    {
        _monthOffset = 0;
        _selectedDate = _date.Date;
        NotifyChanged("monthOffset");
        NotifyChanged("selectedDate");
        Rebuild();
    }

    [Qml] public void SelectDay(int index)
    {
        if (index < 0 || index >= _days.Length || IsDestroyed)
            return;
        SetField(ref _selectedDate, _days[index].Date, nameof(SelectedDate));
        Rebuild();
    }

    public override void ComponentComplete()
    {
        Days.Engine = Engine;
        foreach (var day in _days)
            day.Engine = Engine;
    }

    private DateTime Shown => new DateTime(_date.Year, _date.Month, 1).AddMonths(ClampOffset(_monthOffset));
    // Leave room for adjacent-month cells at both ends of DateTime's range.
    private int ClampOffset(int offset) => Math.Clamp(offset, (2 - _date.Year) * 12 - _date.Month + 1, (9998 - _date.Year) * 12 + 12 - _date.Month);
    private int WeekNumber(DateTime day) => _culture.DateTimeFormat is { CalendarWeekRule: CalendarWeekRule.FirstFourDayWeek, FirstDayOfWeek: DayOfWeek.Monday }
        ? ISOWeek.GetWeekOfYear(day)
        : _culture.DateTimeFormat.Calendar.GetWeekOfYear(day, _culture.DateTimeFormat.CalendarWeekRule, _culture.DateTimeFormat.FirstDayOfWeek);

    private void Rebuild()
    {
        var shown = Shown;
        var lead = ((int)shown.DayOfWeek - FirstDayOfWeek + 7) % 7;
        var start = shown.AddDays(-lead);
        for (var i = 0; i < _days.Length; i++)
            _days[i].Update(start.AddDays(i), shown.Month, _date.Date, _selectedDate, _culture);
        foreach (var property in new[] { "weekdayLabels", "weekNumbers", "firstDayOfWeek", "monthLabel", "dateLabel", "timeLabel", "shortDateLabel" })
            NotifyChanged(property);
    }

    private static CultureInfo GregorianCulture(CultureInfo source)
    {
        var culture = (CultureInfo)source.Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return culture;
    }
}

public sealed class CalendarDay : QObject
{
    private DateTime _date;
    private string _label = "", _fullDate = "";
    private bool _inMonth, _isToday, _isSelected;
    [Qml] public DateTime Date { get { Track(); return _date; } }
    [Qml] public string Label { get { Track(); return _label; } }
    [Qml] public string FullDate { get { Track(); return _fullDate; } }
    [Qml] public bool InMonth { get { Track(); return _inMonth; } }
    [Qml] public bool IsToday { get { Track(); return _isToday; } }
    [Qml] public bool IsSelected { get { Track(); return _isSelected; } }

    internal void Update(DateTime date, int month, DateTime today, DateTime selected, CultureInfo culture)
    {
        SetField(ref _date, date, nameof(Date));
        SetField(ref _label, date.Day.ToString(culture), nameof(Label));
        SetField(ref _fullDate, date.ToString("D", culture), nameof(FullDate));
        SetField(ref _inMonth, date.Month == month, nameof(InMonth));
        SetField(ref _isToday, date == today, nameof(IsToday));
        SetField(ref _isSelected, date == selected, nameof(IsSelected));
    }
}
