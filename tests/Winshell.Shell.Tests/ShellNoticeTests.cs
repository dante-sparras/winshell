using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.ShellTests;

[Trait("Category", "Slow")]
public sealed class ShellNoticeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualEventLoop _loop = new();
    private readonly List<string> _logged = [];
    private readonly List<Notice> _shown = [];
    private readonly List<string> _opened = [];
    private readonly Log _log;
    private readonly CrashReports _crashes;
    private readonly ShellNotices _notices;
    private Action _act = () => { };
    private Action _dismiss = () => { };
    private int _closed;
    private bool _quiet;
    private bool _showFails;

    public ShellNoticeTests()
    {
        _crashes = new CrashReports(_dir);
        _log = new Log { Sink = (_, category, message) => _logged.Add($"{category}: {message}") };
        _notices = new ShellNotices(_loop, _log, _crashes, Show, _opened.Add, () => _quiet);
    }

    public void Dispose()
    {
        _notices.Dispose();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private Closes Show(Notice notice, Action act, Action dismiss)
    {
        if (_showFails)
            throw new InvalidOperationException("no window");
        _shown.Add(notice);
        _act = act;
        _dismiss = dismiss;
        return new Closes(this);
    }

    [Fact]
    public void A_config_file_that_does_not_read_shows_until_it_is_fixed()
    {
        _notices.FollowFileErrors();
        _log.ReportFileError(@"C:\config\preferences.toml", "preferences.toml[3:1]: expected a value");

        var notice = Assert.Single(_shown);
        Assert.Equal(NoticeKind.ConfigError, notice.Kind);
        Assert.Equal("preferences.toml", notice.Detail);
        Assert.Contains("[3:1]", notice.Body, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.Zero, notice.Timeout);

        _log.ReportFileError(@"C:\config\preferences.toml", "");

        Assert.Null(_notices.Current);
    }

    [Fact]
    public void Fixing_one_config_file_leaves_the_notice_of_another()
    {
        _notices.ShowConfigError(@"C:\config\launcher.toml", "broken");

        _notices.ShowConfigError(@"C:\config\preferences.toml", "");

        Assert.NotNull(_notices.Current);
    }

    [Fact]
    public void A_fixed_config_file_leaves_a_build_error_on_screen()
    {
        _notices.Show(ShellNotices.ForBuildError("no", "Mine.csproj"));

        _notices.ShowConfigError(@"C:\config\preferences.toml", "");

        Assert.Equal(NoticeKind.BuildError, _notices.Current?.Kind);
    }

    private sealed class Closes(ShellNoticeTests test) : IDisposable
    {
        public void Dispose()
        {
            test._closed++;
        }
    }

    private void Fail(bool fatal = false, string recovery = "Winshell opened it again.")
    {
        if (fatal)
            _crashes.Write("taskbar", new InvalidOperationException("no"), true);
        else
            _crashes.WriteOnce("taskbar", new InvalidOperationException(Guid.NewGuid().ToString()), "the taskbar",
                recovery);
        _loop.Process();
    }

    private static Notice Build()
    {
        return ShellNotices.ForBuildError("Bar.cs(1,1): error CS1002: ; expected", @"C:\mine\Bar.csproj");
    }

    [Fact]
    public void A_failure_says_what_stopped_and_what_Winshell_did()
    {
        Fail();

        var notice = Assert.Single(_shown);
        Assert.Equal(NoticeKind.Crash, notice.Kind);
        Assert.Equal("The taskbar stopped working", notice.Title);
        Assert.Equal("Winshell opened it again.", notice.Body);
        Assert.Equal("Open report", notice.ActionText);
        Assert.True(notice.Timeout >= ShellNotices.CrashTimeout);
    }

    [Fact]
    public void A_failure_that_ends_the_process_or_has_no_recovery_shows_nothing()
    {
        Fail(true);
        Fail(recovery: "");

        Assert.Empty(_shown);
    }

    [Fact]
    public void A_previous_close_is_shown_from_the_report()
    {
        var report = _crashes.Write("Winshell", new InvalidOperationException("boom"), true);
        Assert.NotNull(report);
        var state = Path.Combine(_dir, "last-exit.toml");

        _notices.ShowPrevious(state);
        _loop.Process();

        var notice = Assert.Single(_shown);
        Assert.Equal("Winshell stopped working", notice.Title);
        Assert.Contains("boom", notice.Body, StringComparison.Ordinal);
        Assert.Equal(report.Path, notice.Target);

        _notices.ShowPrevious(state);
        _loop.Process();

        Assert.Single(_shown);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(6, true)]
    [InlineData(5, false)]
    public void Windows_holding_notifications_back_is_read_as_a_toast_reads_it(int state, bool quiet)
    {
        Assert.Equal(quiet, ShellNotices.QuietState(state));
    }

    [Fact]
    public void A_quiet_Windows_holds_a_failure_back_and_the_log_says_where_it_went()
    {
        _quiet = true;

        Fail();

        Assert.Empty(_shown);
        Assert.Contains(_logged, line => line.Contains("held back", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failure_does_not_replace_a_build_error()
    {
        _notices.Show(Build());

        Fail();

        Assert.Single(_shown);
        Assert.Equal(NoticeKind.BuildError, _notices.Current?.Kind);
    }

    [Fact]
    public void A_build_error_replaces_a_failure_and_stays_until_it_is_dismissed()
    {
        Fail();

        _notices.Show(Build());

        Assert.Equal(1, _closed);
        Assert.Equal(NoticeKind.BuildError, _notices.Current?.Kind);
        Assert.Equal(TimeSpan.Zero, _notices.Current?.Timeout);
    }

    [Fact]
    public void A_build_error_names_the_project_and_what_keeps_running()
    {
        var notice = Build();

        Assert.Equal("Bar.csproj", notice.Detail);
        Assert.Equal(@"C:\mine\Bar.csproj", notice.Target);
        Assert.Contains("keeps running", notice.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_button_opens_the_file_and_closes_the_notice()
    {
        _notices.Show(Build());

        _act();
        _loop.Process();

        Assert.Equal([@"C:\mine\Bar.csproj"], _opened);
        Assert.Null(_notices.Current);
        Assert.Equal(1, _closed);
    }

    [Fact]
    public void Dismissing_closes_it_from_the_loop_and_opens_nothing()
    {
        _notices.Show(Build());

        _dismiss();
        Assert.NotNull(_notices.Current);
        _loop.Process();

        Assert.Null(_notices.Current);
        Assert.Empty(_opened);
    }

    [Fact]
    public void A_notice_that_cannot_show_goes_to_the_log()
    {
        _showFails = true;

        _notices.Show(Build());

        Assert.Null(_notices.Current);
        Assert.Contains(_logged, line => line.StartsWith("notice: Could not show", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_window_sits_above_the_taskbar_at_the_right_of_the_screen_and_says_the_whole_notice()
    {
        var read = await ShellFixture.On(async shell =>
        {
            var notice = Build();
            using var window = new NoticeWindow(notice, () => { }, () => { }, shell.Loop);
            var panel = (PanelWindow)shell.Platform.Windows.Single().Client;
            await ShellFixture.LaidOut((FrameworkElement)panel.Content!);
            return (
                panel.Width,
                panel.Height,
                Name: AutomationProperties.GetName(panel.Content!),
                Live: AutomationProperties.GetLiveSetting(panel.Content!));
        });

        Assert.Equal(364, read.Width);
        Assert.True(read.Height > 100);
        Assert.Contains("did not build", read.Name, StringComparison.Ordinal);
        Assert.Contains("CS1002", read.Name, StringComparison.Ordinal);
        Assert.Equal(AutomationLiveSetting.Assertive, read.Live);
    }

    [Fact]
    public async Task The_window_is_tall_enough_for_its_button()
    {
        var fits = await ShellFixture.On(async shell =>
        {
            using var window = new NoticeWindow(Build(), () => { }, () => { }, shell.Loop);
            var root = (Grid)((PanelWindow)shell.Platform.Windows.Single().Client).Content!;
            var action = ((StackPanel)root.Children[0]).Children.OfType<Button>().Last();
            await ShellFixture.LaidOut(action);
            await Wait.UntilAsync(
                () => action.TransformToVisual(root).TransformPoint(default).Y + action.ActualHeight +
                    root.Padding.Bottom <= root.ActualHeight + 0.5,
                "the button to fit in the window");
            return true;
        });

        Assert.True(fits);
    }

    [Fact]
    public async Task A_timed_window_closes_itself_and_an_untimed_one_waits()
    {
        var dismissed = await ShellFixture.On(shell =>
        {
            var loop = new ManualEventLoop();
            var timed = 0;
            var untimed = 0;
            var crash = ShellNotices.ForCrash(
                new CrashReport("r.txt", "x", new InvalidOperationException("no"), false) { Recovery = "Fine." },
                TimeSpan.FromSeconds(25));
            using var first = new NoticeWindow(crash, () => { }, () => timed++, loop);
            using var second = new NoticeWindow(Build(), () => { }, () => untimed++, loop);
            loop.Advance(TimeSpan.FromSeconds(26));
            return (timed, untimed);
        });

        Assert.Equal((1, 0), dismissed);
    }
}
