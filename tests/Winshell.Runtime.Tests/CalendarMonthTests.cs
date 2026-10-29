using System.Globalization;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public sealed class CalendarMonthTests
{
    [Theory]
    [InlineData("en-US", DayOfWeek.Sunday, 28)]
    [InlineData("sv-SE", DayOfWeek.Monday, 29)]
    [InlineData("ar-SA", DayOfWeek.Sunday, 28)]
    public void Grid_uses_regional_weekday_order_and_contains_leap_day(string locale, DayOfWeek first, int firstDate)
    {
        var month = new CalendarMonth { LocaleName = locale, Date = new DateTime(2024, 2, 29, 14, 30, 0) };
        var days = month.Days.Rows.Cast<CalendarDay>().ToArray();
        Assert.Equal(42, days.Length);
        Assert.Equal((int)first, month.FirstDayOfWeek);
        Assert.Equal(firstDate, days[0].Date.Day);
        Assert.Equal(first, days[0].Date.DayOfWeek);
        Assert.Equal(29, days.Count(d => d.InMonth));
        Assert.Equal(new DateTime(2024, 2, 29), Assert.Single(days, d => d.IsToday).Date);
        Assert.Equal(7, month.WeekdayLabels.Length);
        Assert.Equal(6, month.WeekNumbers.Length);
        Assert.All(days.Zip(days.Skip(1)), pair => Assert.Equal(pair.First.Date.AddDays(1), pair.Second.Date));
        month.Destroy();
        Assert.All(days, day => Assert.True(day.IsDestroyed));
    }

    [Fact]
    public void Week_numbers_follow_the_locale_rule_at_a_year_boundary()
    {
        var month = new CalendarMonth { LocaleName = "sv-SE", Date = new DateTime(2021, 1, 1) };
        Assert.Equal(53, month.WeekNumbers[0]);
        Assert.Equal(1, month.WeekNumbers[1]);
        Assert.Equal("M", month.WeekdayLabels[0]);
        month.LocaleName = "en-US";
        Assert.Equal(2, month.WeekNumbers[1]);
        Assert.Equal("S", month.WeekdayLabels[0]);
        month.LocaleName = "sv-SE";
        month.Date = new DateTime(2018, 12, 1);
        Assert.Equal(1, month.WeekNumbers[5]);
    }

    [Fact]
    public void Navigation_selection_and_reset_preserve_grid_objects_and_update_labels()
    {
        var month = new CalendarMonth { LocaleName = "en-US", Date = new DateTime(2024, 12, 31) };
        var first = month.Days.Rows[0];
        month.Reset();
        month.MonthOffset++;
        Assert.Contains("2025", month.MonthLabel);
        Assert.Equal(new DateTime(2024, 12, 29), ((CalendarDay)month.Days.Rows[0]!).Date);
        month.SelectDay(4);
        Assert.Equal(new DateTime(2025, 1, 2), month.SelectedDate);
        Assert.True(((CalendarDay)month.Days.Rows[4]!).IsSelected);
        month.Reset();
        Assert.Equal(0, month.MonthOffset);
        Assert.Equal(new DateTime(2024, 12, 31), month.SelectedDate);
        Assert.Same(first, month.Days.Rows[0]);
        month.SelectDay(-1);
        month.SelectDay(42);
        Assert.Equal(new DateTime(2024, 12, 31), month.SelectedDate);
    }

    [Fact]
    public void Culture_formats_dates_and_clock_without_changing_process_settings()
    {
        var before = CultureInfo.CurrentCulture;
        var month = new CalendarMonth { LocaleName = "en-US", Date = new DateTime(2026, 10, 29, 16, 5, 0) };
        Assert.Equal("4:05 PM", month.TimeLabel);
        Assert.Equal("10/29/2026", month.ShortDateLabel);
        Assert.Equal("Thursday, October 29, 2026", month.DateLabel);
        month.LocaleName = "sv-SE";
        Assert.Equal("16:05", month.TimeLabel);
        Assert.Contains("oktober", month.MonthLabel);
        Assert.Same(before, CultureInfo.CurrentCulture);
    }

    [Fact]
    public void Utc_dates_use_local_civil_time_and_extreme_navigation_is_bounded()
    {
        var utc = new DateTime(2024, 3, 31, 23, 30, 0, DateTimeKind.Utc);
        var month = new CalendarMonth { Date = utc };
        Assert.Equal(utc.ToLocalTime(), month.Date);
        month.MonthOffset = int.MinValue;
        Assert.Equal(2, ((CalendarDay)month.Days.Rows[7]!).Date.Year);
        month.MonthOffset = int.MaxValue;
        Assert.Equal(9998, ((CalendarDay)month.Days.Rows[7]!).Date.Year);
    }

    [Fact]
    public void An_uninitialized_qml_date_does_not_shift_the_month_before_its_binding_arrives()
    {
        var month = new CalendarMonth { Date = DateTime.MinValue };
        month.Date = new DateTime(2026, 10, 29);
        Assert.Equal(0, month.MonthOffset);
        Assert.Contains("2026", month.MonthLabel);
    }

    [Fact]
    public void Gregorian_week_numbers_do_not_use_a_locales_default_non_gregorian_calendar()
    {
        var month = new CalendarMonth { LocaleName = "ar-SA", Date = new DateTime(1800, 1, 1) };
        Assert.Equal(6, month.WeekNumbers.Length);
        Assert.Contains("1800", month.MonthLabel);
    }
}

