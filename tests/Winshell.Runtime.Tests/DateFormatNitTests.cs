namespace Winshell.Runtime.Tests;

/// <summary>RFC 2822 offsets have no colon, and the zone token is short.</summary>
public class DateFormatNitTests
{
    [Fact]
    public void Rfc2822_offset_has_no_colon_and_the_zone_token_is_short()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property string rfc: Qt.formatDateTime(new Date(2026, 9, 28, 14, 5, 9), Qt.RFC2822Date)
              property string zone: Qt.formatDateTime(new Date(2026, 9, 28, 14, 5, 9), "t")
              property string ampm: Qt.formatDateTime(new Date(2026, 9, 28, 14, 5, 9), "AP")
            }
            """));
        var root = shell.Load();

        var rfc = Assert.IsType<string>(root.ReadProperty("rfc"));
        Assert.StartsWith("28 Oct 2026 14:05:09 ", rfc, StringComparison.Ordinal);
        var offset = rfc["28 Oct 2026 14:05:09 ".Length..];
        Assert.Matches(@"^[+-]\d{4}$", offset);

        var zone = Assert.IsType<string>(root.ReadProperty("zone"));
        Assert.DoesNotContain("Standard Time", zone, StringComparison.Ordinal);
        Assert.True(zone.Length is > 0 and <= 6);

        var marker = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.PMDesignator;
        if (string.IsNullOrEmpty(marker))
            marker = "PM";
        Assert.Equal(marker.ToUpper(System.Globalization.CultureInfo.CurrentCulture), root.ReadProperty("ampm"));
        Assert.Empty(shell.Messages);
    }
}
