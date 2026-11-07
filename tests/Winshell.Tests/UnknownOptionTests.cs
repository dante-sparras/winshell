namespace Winshell.Tests;

/// <summary>
/// A bad command line exits 2 with only its message, before a shell starts. <see cref="CommandOptionTests"/> and
/// <see cref="CommandWordTests"/> own each message.
/// </summary>
[Trait("Category", "Slow")]
public class UnknownOptionTests
{
    [Fact]
    public void A_bad_command_line_exits_2_with_only_its_message()
    {
        // The path is not there, so a bug in the check fails with 1 and cannot start a shell.
        var missing = Path.Combine(Path.GetTempPath(), "winshell-unknown-option-missing");

        var result = WinshellCli.Run(["run", "-p", missing, "--pth"],
            ("WINSHELL_CONFIG_DIR", Path.Combine(Path.GetTempPath(), "winshell-unknown-option-absent")));

        result.AssertExitCode(2);
        Assert.Equal("", result.Output);
        Assert.Equal("Unknown option \"--pth\"." + Environment.NewLine, result.Error);
    }
}
