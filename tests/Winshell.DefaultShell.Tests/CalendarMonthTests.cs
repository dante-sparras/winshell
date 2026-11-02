using System.Globalization;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class CalendarMonthTests
{
    private static FakeTimeProvider Clock(int year = 2026, int month = 11, int day = 2, int hour = 12) 
    {
        var time = new FakeTimeProvider(new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return time;
    }

    [Theory]
    [InlineData("en-US", DayOfWeek.Sunday, 28)]
    [InlineData("sv-SE", DayOfWeek.Monday, 29)]
    [InlineData("ar-SA", DayOfWeek.Sunday, 28)]
    public void Grid_uses_regional_weekday_order_and_contains_leap_day(string locale, DayOfWeek first, int firstDate)
    {
        var month = new CalendarMonth(Clock()) { LocaleName = locale };
        month.Date = new DateTime(2024, 2, 29, 14, 30, 0);
        var days = month.Days;

        Assert.Equal(42, days.Count);
        Assert.Equal(first, month.FirstDayOfWeek);
        Assert.Equal(firstDate, days[0].Date.Day);
        Assert.Equal(first, days[0].Date.DayOfWeek);
        Assert.Equal(29, days.Count(d => d.InMonth));
        Assert.Equal(new DateTime(2024, 2, 29), Assert.Single(days, d => d.IsToday).Date);
        Assert.Equal(7, month.WeekdayLabels.Count);
        Assert.Equal(6, month.WeekNumbers.Count);
        Assert.All(days.Zip(days.Skip(1)), pair => Assert.Equal(pair.First.Date.AddDays(1), pair.Second.Date));
        Assert.Equal("Adjacent month.", days[0].Description);
        Assert.Equal("Today. ", Assert.Single(days, d => d.IsToday).Description);
        Assert.Contains(days, d => d.InMonth && !d.IsToday && d.Description == "");
    }

    [Fact]
    public void Week_numbers_follow_the_locale_rule_at_a_year_boundary()
    {
        var month = new CalendarMonth(Clock()) { LocaleName = "sv-SE" };
        month.Date = new DateTime(2021, 1, 1);
        var firstWeek = month.WeekNumbers[0];
        var firstHeading = month.WeekdayLabels[0];

        Assert.Equal(53, month.WeekNumbers[0].Number);
        Assert.Equal("53", month.WeekNumbers[0].Text);
        Assert.Equal("Week 53", month.WeekNumbers[0].Name);
        Assert.Equal(1, month.WeekNumbers[1].Number);
        Assert.Equal("M", month.WeekdayLabels[0].Text);
        month.LocaleName = "en-US";
        Assert.Equal(2, month.WeekNumbers[1].Number);
        Assert.Equal("S", month.WeekdayLabels[0].Text);
        Assert.Same(firstWeek, month.WeekNumbers[0]);
        Assert.Same(firstHeading, month.WeekdayLabels[0]);
        month.LocaleName = "sv-SE";
        month.Date = new DateTime(2018, 12, 1);
        Assert.Equal(1, month.WeekNumbers[5].Number);
        Assert.Equal("Week 1", month.WeekNumbers[5].Name);
    }

    [Fact]
    public void Navigation_selection_and_today_preserve_grid_objects_and_update_labels()
    {
        var month = new CalendarMonth(Clock()) { LocaleName = "en-US" };
        month.Date = new DateTime(2024, 12, 31);
        var first = month.Days[0];
        month.Today();
        month.Next();

        Assert.Contains("2025", month.MonthLabel);
        Assert.Equal(new DateTime(2024, 12, 29), month.Days[0].Date);
        month.SelectDay(4);
        Assert.Equal(new DateTime(2025, 1, 2), month.SelectedDate);
        Assert.True(month.Days[4].IsSelected);
        Assert.Equal("Selected. ", month.Days[4].Description);
        var offset = month.MonthOffset;
        month.Date = month.Date.AddHours(1);
        Assert.Equal(offset, month.MonthOffset);
        Assert.Equal(new DateTime(2025, 1, 2), month.SelectedDate);
        month.Previous();
        month.Today();
        Assert.Equal(0, month.MonthOffset);
        Assert.Equal(new DateTime(2024, 12, 31), month.SelectedDate);
        Assert.Same(first, month.Days[0]);
        var today = Assert.Single(month.Days, d => d.IsToday);
        Assert.Equal("Today. Selected. ", today.Description);
        month.SelectDay(-1);
        month.SelectDay(42);
        Assert.Equal(new DateTime(2024, 12, 31), month.SelectedDate);
    }

    [Fact]
    public void Culture_formats_dates_and_clock_without_changing_process_settings()
    {
        var before = CultureInfo.CurrentCulture;
        var month = new CalendarMonth(Clock()) { LocaleName = "en-US" };
        month.Date = new DateTime(2026, 10, 29, 16, 5, 0);

        Assert.Equal("4:05 PM", month.TimeLabel);
        Assert.Equal("10/29/2026", month.ShortDateLabel);
        Assert.Equal("Thursday, October 29, 2026", month.DateLabel);
        month.LocaleName = "sv-SE";
        Assert.Equal("16:05", month.TimeLabel);
        Assert.Contains("oktober", month.MonthLabel);
        month.LocaleName = "nope";
        Assert.Equal("sv-SE", month.LocaleName);
        Assert.Same(before, CultureInfo.CurrentCulture);
    }

    [Fact]
    public void Utc_dates_use_local_civil_time_and_extreme_navigation_is_bounded()
    {
        var utc = new DateTime(2024, 3, 31, 23, 30, 0, DateTimeKind.Utc);
        var month = new CalendarMonth(Clock());
        month.Date = utc;

        Assert.Equal(utc.ToLocalTime(), month.Date);
        month.MonthOffset = int.MinValue;
        Assert.Equal(2, month.Days[7].Date.Year);
        month.MonthOffset = int.MaxValue;
        Assert.Equal(9998, month.Days[7].Date.Year);
    }

    [Fact]
    public void An_uninitialized_date_does_not_shift_the_month()
    {
        var month = new CalendarMonth(Clock());
        var label = month.MonthLabel;
        month.Date = DateTime.MinValue;

        Assert.Equal(0, month.MonthOffset);
        Assert.Equal(label, month.MonthLabel);
        month.Date = new DateTime(2026, 10, 29);
        Assert.Equal(0, month.MonthOffset);
        Assert.Contains("2026", month.MonthLabel);
    }

    [Fact]
    public void Gregorian_week_numbers_do_not_use_a_locales_default_non_gregorian_calendar()
    {
        var month = new CalendarMonth(Clock()) { LocaleName = "ar-SA" };
        month.Date = new DateTime(1800, 1, 1);

        Assert.Equal(6, month.WeekNumbers.Count);
        Assert.Contains("1800", month.MonthLabel);
    }
}
