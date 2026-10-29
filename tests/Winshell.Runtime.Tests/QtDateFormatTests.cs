namespace Winshell.Runtime.Tests;

/// <summary>A quoted letter is text. It does not turn on 12-hour time.</summary>
public class QtDateFormatTests
{
    [Fact]
    public void A_quoted_a_does_not_select_twelve_hour_time()
    {
        var when = new DateTime(2026, 10, 28, 14, 5, 9);

        Assert.Equal("28 at 14:05", QtDateFormat.Format(when, "d 'at' h:mm"));
        Assert.Equal("28 at 02:05 PM", QtDateFormat.Format(when, "d 'at' hh:mm AP"));
    }
}
