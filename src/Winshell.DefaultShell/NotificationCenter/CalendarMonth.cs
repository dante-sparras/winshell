using System.Globalization;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>A Gregorian month grid using the user's regional weekday order and week numbering rule.</summary>
public sealed class CalendarMonth : ObservableObject
{
    private readonly TimeProvider _time;
    private readonly CalendarDay[] _days = new CalendarDay[42];
    private readonly CalendarHeading[] _headings = new CalendarHeading[7];
    private readonly CalendarWeek[] _weeks = new CalendarWeek[6];
    private DateTime _date;
    private DateTime _selectedDate;
    private int _monthOffset;
    private CultureInfo _culture;

    public CalendarMonth(TimeProvider time, CultureInfo? culture = null)
    {
        _time = time;
        var local = time.GetLocalNow().LocalDateTime;
        _date = local;
        _selectedDate = local.Date;
        _culture = Gregorian(culture ?? CultureInfo.CurrentCulture);
        for (var i = 0; i < _days.Length; i++)
            _days[i] = new CalendarDay(this, i);
        for (var i = 0; i < _headings.Length; i++)
            _headings[i] = new CalendarHeading();
        for (var i = 0; i < _weeks.Length; i++)
            _weeks[i] = new CalendarWeek();
        Days = _days;
        WeekdayLabels = _headings;
        WeekNumbers = _weeks;
        Rebuild();
    }

    public IReadOnlyList<CalendarDay> Days { get; }

    public IReadOnlyList<CalendarHeading> WeekdayLabels { get; }

    public IReadOnlyList<CalendarWeek> WeekNumbers { get; }

    public DayOfWeek FirstDayOfWeek => _culture.DateTimeFormat.FirstDayOfWeek;

    public string MonthLabel => Shown.ToString("Y", _culture);

    public string DateLabel => _date.ToString("D", _culture);

    public string TimeLabel => _date.ToString("t", _culture);

    public string LongTimeLabel => _date.ToString("T", _culture);

    public string ShortDateLabel => _date.ToString("d", _culture);

    public DateTime SelectedDate => _selectedDate;

    public DateTime Date
    {
        get => _date;
        set
        {
            // An unset date is not a month. The clock's binding arrives after the control does.
            if (value == DateTime.MinValue)
                return;
            var local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
            if (!Set(ref _date, local))
                return;
            Set(ref _monthOffset, Clamp(_monthOffset), nameof(MonthOffset));
            Rebuild();
        }
    }

    public int MonthOffset
    {
        get => _monthOffset;
        set
        {
            if (Set(ref _monthOffset, Clamp(value)))
                Rebuild();
        }
    }

    /// <summary>Empty means the process's current Windows regional format. An unknown name is ignored.</summary>
    public string LocaleName
    {
        get => _culture.Name;
        set
        {
            CultureInfo culture;
            try
            {
                culture = Gregorian(string.IsNullOrEmpty(value) ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo(value));
            }
            catch (CultureNotFoundException)
            {
                return;
            }

            _culture = culture;
            Changed();
            Rebuild();
        }
    }

    /// <summary>The month of <see cref="Date"/>, with that day selected.</summary>
    public void Today()
    {
        Set(ref _monthOffset, 0, nameof(MonthOffset));
        Set(ref _selectedDate, _date.Date, nameof(SelectedDate));
        Rebuild();
    }

    public void Previous() => MonthOffset--;

    public void Next() => MonthOffset++;

    public void SelectDay(int index)
    {
        if ((uint)index >= (uint)_days.Length)
            return;
        Set(ref _selectedDate, _days[index].Date, nameof(SelectedDate));
        Rebuild();
    }

    private DateTime Shown => new DateTime(_date.Year, _date.Month, 1).AddMonths(Clamp(_monthOffset));

    // Leave room for adjacent-month cells at both ends of DateTime's range.
    private int Clamp(int offset) => Math.Clamp(offset, (2 - _date.Year) * 12 - _date.Month + 1, (9998 - _date.Year) * 12 + 12 - _date.Month);

    private int WeekNumber(DateTime day) => _culture.DateTimeFormat is { CalendarWeekRule: CalendarWeekRule.FirstFourDayWeek, FirstDayOfWeek: DayOfWeek.Monday }
        ? ISOWeek.GetWeekOfYear(day)
        : _culture.DateTimeFormat.Calendar.GetWeekOfYear(day, _culture.DateTimeFormat.CalendarWeekRule, _culture.DateTimeFormat.FirstDayOfWeek);

    private void Rebuild()
    {
        var shown = Shown;
        var first = (int)FirstDayOfWeek;
        var lead = ((int)shown.DayOfWeek - first + 7) % 7;
        var start = shown.AddDays(-lead);
        for (var i = 0; i < _days.Length; i++)
            _days[i].Update(start.AddDays(i), shown.Month, _date.Date, _selectedDate.Date, _culture);
        for (var i = 0; i < _headings.Length; i++)
            _headings[i].Update(_culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((first + i) % 7)));
        for (var i = 0; i < _weeks.Length; i++)
            _weeks[i].Update(WeekNumber(_days[i * 7].Date), _culture);
        Changed(nameof(MonthLabel));
        Changed(nameof(DateLabel));
        Changed(nameof(TimeLabel));
        Changed(nameof(LongTimeLabel));
        Changed(nameof(ShortDateLabel));
        Changed(nameof(FirstDayOfWeek));
    }

    private static CultureInfo Gregorian(CultureInfo source)
    {
        var culture = (CultureInfo)source.Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return culture;
    }
}

/// <summary>One cell of the month grid.</summary>
public sealed class CalendarDay : ObservableObject
{
    private readonly CalendarMonth _month;
    private readonly int _index;
    private DateTime _date;
    private string _label = "";
    private string _fullDate = "";
    private bool _inMonth;
    private bool _isToday;
    private bool _isSelected;

    internal CalendarDay(CalendarMonth month, int index)
    {
        _month = month;
        _index = index;
    }

    public DateTime Date => _date;

    public string Label => _label;

    public string FullDate => _fullDate;

    public bool InMonth => _inMonth;

    public bool IsToday => _isToday;

    public bool IsSelected => _isSelected;

    /// <summary>Today, selected, and a day from the month before or after, in that order.</summary>
    public string Description
    {
        get
        {
            var text = "";
            if (_isToday)
                text += "Today. ";
            if (_isSelected)
                text += "Selected. ";
            if (!_inMonth)
                text += "Adjacent month.";
            return text;
        }
    }

    public void Select() => _month.SelectDay(_index);

    internal void Update(DateTime date, int month, DateTime today, DateTime selected, CultureInfo culture)
    {
        Set(ref _date, date, nameof(Date));
        Set(ref _label, date.Day.ToString(culture), nameof(Label));
        Set(ref _fullDate, date.ToString("D", culture), nameof(FullDate));
        var visual = Set(ref _inMonth, date.Month == month, nameof(InMonth))
            | Set(ref _isToday, date.Date == today.Date, nameof(IsToday))
            | Set(ref _isSelected, date.Date == selected.Date, nameof(IsSelected));
        if (visual)
            Changed(nameof(Description));
    }
}

/// <summary>One weekday initial above the grid.</summary>
public sealed class CalendarHeading : ObservableObject
{
    public string Text { get; private set => Set(ref field, value); } = "";

    internal void Update(string text) => Text = text;
}

/// <summary>The week number beside one row. <see cref="Name"/> is what Narrator reads.</summary>
public sealed class CalendarWeek : ObservableObject
{
    private int _number;
    private string _text = "";
    private string _name = "";

    public int Number => _number;

    public string Text => _text;

    public string Name => _name;

    internal void Update(int number, CultureInfo culture)
    {
        var text = number.ToString(culture);
        Set(ref _number, number, nameof(Number));
        Set(ref _text, text, nameof(Text));
        Set(ref _name, "Week " + text, nameof(Name));
    }
}
