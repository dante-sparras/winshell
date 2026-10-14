using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

/// <summary><c>--config</c> and <c>WINSHELL_CONFIG</c> point one start at another folder.</summary>
[TestClass]
public sealed class ConfigFolderTests
{
    [TestInitialize]
    public void Clear() => Restore();

    [TestCleanup]
    public void Restore()
    {
        ConfigFolder.Use(null);
        Environment.SetEnvironmentVariable(ConfigFolder.EnvironmentVariable, null);
    }

    [TestMethod]
    public void Use_points_the_process_at_a_folder_and_an_empty_value_restores_it()
    {
        var person = ConfigFolder.Path;
        var folder = Path.Combine(Path.GetTempPath(), "winoma-tests", "winshell-config");

        ConfigFolder.Use(folder);
        ConfigFolder.Path.Should().Be(Path.GetFullPath(folder));

        ConfigFolder.Use("  ");
        ConfigFolder.Path.Should().Be(person);
    }

    [TestMethod]
    public void The_switch_wins_over_the_environment_variable()
    {
        var person = ConfigFolder.Path;
        var fromSwitch = Path.Combine(Path.GetTempPath(), "winoma-tests", "from-switch");
        var fromEnv = Path.Combine(Path.GetTempPath(), "winoma-tests", "from-env");
        Environment.SetEnvironmentVariable(ConfigFolder.EnvironmentVariable, fromEnv);

        ConfigFolder.Resolve(["--watch", "--config", fromSwitch]).Should().Be(Path.GetFullPath(fromSwitch));
        ConfigFolder.Resolve(["--config=" + fromSwitch, "--watch"]).Should().Be(Path.GetFullPath(fromSwitch));
        ConfigFolder.Resolve(["--watch"]).Should().Be(Path.GetFullPath(fromEnv));
        ConfigFolder.Path.Should().Be(person);
    }

    [TestMethod]
    public void No_switch_and_no_variable_is_the_persons_folder()
    {
        var person = ConfigFolder.Path;
        ConfigFolder.Resolve([]).Should().Be(person);
        ConfigFolder.Resolve(["--give-back"]).Should().Be(person);
    }

    [TestMethod]
    public void A_switch_without_a_folder_is_refused()
    {
        var act = () => ConfigFolder.Resolve(["--config"]);
        act.Should().Throw<ArgumentException>().WithMessage("*--config*");

        var equals = () => ConfigFolder.Resolve(["--config="]);
        equals.Should().Throw<ArgumentException>().WithMessage("*--config*");

        var nextFlag = () => ConfigFolder.Resolve(["--config", "--watch"]);
        nextFlag.Should().Throw<ArgumentException>().WithMessage("*--config*");
    }
}
