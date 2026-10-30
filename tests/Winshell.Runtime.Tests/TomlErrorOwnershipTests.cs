using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class TomlErrorOwnershipTests
{
    private const string View = """
        FileView {
          path: Shell.shellDir + "/settings.toml"
          blockLoading: true
          printErrors: false
          TomlAdapter { property string name: "default" }
        }
        """;

    private static string Config(int views) => """
        import Winshell
        import Winshell.Io
        Scope {
        """ + string.Concat(Enumerable.Repeat(View, views)) + "}";

    [Fact]
    public void A_reload_keeps_the_error_reported_by_the_new_generation()
    {
        using var files = new TestShell(("shell.qml", Config(1)), ("settings.toml", "name = \"broken"));
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log);
        var reports = new List<string?>();
        host.FileError += reports.Add;
        Assert.True(host.Start(), string.Join("\n", files.Messages));
        Assert.Contains("settings.toml[1:", Assert.Single(reports));
        var previous = host.Root;
        reports.Clear();

        host.Reload(hard: true);

        Assert.NotSame(previous, host.Root);
        Assert.True(previous!.IsDestroyed);
        Assert.NotEmpty(reports);
        Assert.All(reports, error => Assert.Contains("settings.toml[1:", error));
    }

    [Fact]
    public void Closing_one_view_keeps_another_views_error_until_that_view_reads_a_fixed_file()
    {
        using var files = new TestShell(("shell.qml", Config(2)), ("settings.toml", "name = \"broken"));
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log);
        var reports = new List<string?>();
        host.FileError += reports.Add;
        Assert.True(host.Start(), string.Join("\n", files.Messages));
        var views = host.Root!.OwnedObjects.OfType<FileView>().ToArray();
        Assert.Equal(2, views.Length);
        reports.Clear();

        views[0].Destroy();

        Assert.Contains("settings.toml[1:", Assert.Single(reports));
        File.WriteAllText(Path.Combine(files.Directory, "settings.toml"), "name = \"fixed\"");
        views[1].Reload();
        files.Loop.Process();
        Assert.Null(reports[^1]);
        Assert.Equal("", views[1].Adapter!.Error);
    }
}
