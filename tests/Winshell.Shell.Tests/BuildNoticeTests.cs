namespace Winshell.ShellTests;

/// <summary>A configuration that builds closes the build-error notice of its own project, and nothing else.</summary>
public sealed class BuildNoticeTests : IDisposable
{
    private const string Mine = @"C:\mine\Mine.csproj";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly CrashReports _crashes;
    private readonly ShellNotices _notices;

    public BuildNoticeTests()
    {
        _crashes = new CrashReports(_dir);
        _notices = new ShellNotices(new ManualEventLoop(), new Log(), _crashes, (_, _, _) => new Window(), _ => { },
            () => false);
    }

    public void Dispose()
    {
        _notices.Dispose();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Fact]
    public void A_build_that_works_closes_the_build_error_of_its_project()
    {
        _notices.ShowBuildError("Bar.cs(1,1): error CS1002: ; expected", Mine);

        _notices.ShowBuildError("", Mine);

        Assert.Null(_notices.Current);
    }

    [Fact]
    public void A_build_that_works_leaves_the_build_error_of_another_project()
    {
        _notices.ShowBuildError("Bar.cs(1,1): error CS1002: ; expected", @"C:\other\Other.csproj");

        _notices.ShowBuildError("", Mine);

        Assert.Equal(NoticeKind.BuildError, _notices.Current?.Kind);
    }

    [Fact]
    public void A_build_that_works_leaves_a_config_file_error_on_screen()
    {
        _notices.ShowConfigError(@"C:\mine\preferences.toml", "preferences.toml[3:1]: expected a value");

        _notices.ShowBuildError("", Mine);

        Assert.Equal(NoticeKind.ConfigError, _notices.Current?.Kind);
    }

    private sealed class Window : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
