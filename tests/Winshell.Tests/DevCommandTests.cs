using System.Diagnostics;

namespace Winshell.Tests;

/// <summary>The real <c>winshell dev</c>, on a real project, editing its source.</summary>
[Trait("Category", "Slow")]
public sealed class DevCommandTests : IDisposable
{
    private readonly DevProject _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public void A_downloaded_project_never_reaches_a_build()
    {
        File.WriteAllText(Path.Combine(_project.Project, "Dev.csproj") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        var (code, _, error) = WinshellCli.Run(["dev", "-p", _project.Project], _project.Folders);

        Assert.Equal(1, code);
        Assert.Contains("Dev.csproj", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_project.Project, "obj")), "dotnet ran on a downloaded project.");
    }

    [Fact]
    public void With_no_project_it_says_how_to_make_one()
    {
        var (code, _, error) = WinshellCli.Run(["dev"], _project.Folders);

        Assert.Equal(1, code);
        Assert.Contains("dotnet new winshell", error, StringComparison.Ordinal);
    }

    [Fact(Timeout = 170_000)]
    public void The_console_shows_what_dotnet_watch_says_but_not_its_Ctrl_R_hint()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        _project.StartDev();
        Wait.Until(() => _project.Value() == (0, "one"), "the first build", DevProject.BuildPatience);

        Assert.True(_project.Printed("dotnet watch : "), "winshell dev printed nothing of dotnet watch's.");
        Assert.False(_project.Printed("Ctrl+R") || _project.Printed("Ctrl + R"), "winshell dev printed dotnet watch's Ctrl+R hint.");
    }

    [Fact(Timeout = 170_000)]
    public void An_edit_reaches_the_running_shell_and_a_broken_one_leaves_it_running()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        _project.StartDev();
        Wait.Until(() => _project.Value() == (0, "one"), "the first build", DevProject.BuildPatience);
        var shell = _project.ShellProcess();
        // The first edit also warms the compiler up.
        _project.Mark("""=> "two";""");
        Wait.Until(() => _project.Value().Text == "two", "the first edit", DevProject.BuildPatience);

        _project.Mark("""=> "broken" """);
        Wait.Until(() => _project.Printed("error CS1002"), "the compile error");
        Assert.Equal((0, "two"), _project.Value());
        Wait.Until(() => WinshellCli.Run(["log", "-t", "20"], _project.Folders).Output.Contains("Your configuration did not build", StringComparison.Ordinal), "the notice");

        _project.Mark("""=> "three";""");
        var applied = Stopwatch.StartNew();
        Wait.Until(() => _project.Value().Text == "three", "the fix");

        Assert.Equal(shell, _project.ShellProcess());
        Assert.True(applied.Elapsed < TimeSpan.FromSeconds(10), $"An edit took {applied.Elapsed}.");

        _project.Label("two");
        var saved = Stopwatch.StartNew();
        Wait.Until(() => _project.Shown() == "two", "the XAML edit");
        Assert.True(saved.Elapsed < TimeSpan.FromSeconds(5), $"A XAML edit took {saved.Elapsed}.");
        Assert.Equal(shell, _project.ShellProcess());
        TestContext.Current.TestOutputHelper?.WriteLine($"An edit took {applied.Elapsed}, and a XAML edit {saved.Elapsed}.");
    }
}
