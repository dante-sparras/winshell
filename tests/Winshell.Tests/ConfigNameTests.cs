namespace Winshell.Tests;

/// <summary><c>-c</c> names one folder in the config folder. It can't be empty or leave that folder.</summary>
public class ConfigNameTests
{
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"..\..\somewhere")]
    [InlineData("work/old")]
    [InlineData(@"C:\config")]
    public void A_name_that_is_not_one_folder_is_a_usage_error(string name)
    {
        var error = Assert.Throws<ArgumentException>(() => Cli.Parse(["run", "-c", name], "run"));

        Assert.Contains("Use -p for a path", error.Message);
    }

    [Theory]
    [InlineData("work")]
    [InlineData("my config")]
    [InlineData("v1.2")]
    public void A_folder_name_is_kept(string name) => Assert.Equal(name, Cli.Parse(["run", "-c", name], "run").Config);
}
