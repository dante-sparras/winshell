using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>Whole cookies and passphrases are masked, not just their first word.</summary>
public class CrashReportNitsTests
{
    [Theory]
    [InlineData("Cookie: a=b; c=d", "Cookie: [redacted]")]
    [InlineData("Set-Cookie: session=abc; Path=/; Secure", "Set-Cookie: [redacted]")]
    [InlineData("password = correct horse battery", "password = [redacted]")]
    [InlineData("passphrase: correct horse battery", "passphrase: [redacted]")]
    public void A_value_with_spaces_is_masked_to_the_end_of_the_line(string line, string expected)
    {
        Assert.Equal(expected, CrashRedaction.Redact(line));
    }

    [Fact]
    public void A_masked_value_stops_at_the_end_of_its_line()
    {
        Assert.Equal("password = [redacted]\nnext line", CrashRedaction.Redact("password = correct horse\nnext line"));
    }

    [Fact]
    public void A_quoted_password_keeps_what_follows_it()
    {
        var redacted = CrashRedaction.Redact("password=\"hunter2\" and more");

        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.Contains("and more", redacted, StringComparison.Ordinal);
    }
}
