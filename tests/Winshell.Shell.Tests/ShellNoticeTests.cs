using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.ShellTests;

public sealed class ShellNoticeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualEventLoop _loop = new();
    private readonly List<string> _logged = [];
    private readonly List<Notice> _shown = [];
    private readonly List<string> _opened = [];
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
        _notices = new ShellNotices(_loop, new Log { Sink = (_, category, message) => _logged.Add($"{category}: {message}") }, _crashes, Show, _opened.Add)
        {
            IsQuiet = () => _quiet,
        };
    }

    public void Dispose()
    {
        _notices.Dispose();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
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

    private sealed class Closes(ShellNoticeTests test) : IDisposable
    {
        public void Dispose() => test._closed++;
    }

    private void Fail(bool fatal = false, string recovery = "Winshell opened it again.")
    {
        if (fatal)
            _crashes.Write("taskbar", new InvalidOperationException("no"), fatal: true);
        else
            _crashes.WriteOnce("taskbar", new InvalidOperationException(Guid.NewGuid().ToString()), subject: "the taskbar", recovery: recovery);
        _loop.Process();
    }

    private static Notice Build() => ShellNotices.ForBuildError("Bar.cs(1,1): error CS1002: ; expected", @"C:\mine\Bar.csproj");

    [Fact]
    public void A_leftover_qml_file_says_to_start_a_project()
    {
        var notice = ShellNotices.ForRetiredQml(@"C:\configs\shell.qml");

        Assert.Equal(NoticeKind.RetiredQml, notice.Kind);
        Assert.Equal("QML configurations stopped working", notice.Title);
        Assert.Equal("shell.qml", notice.Detail);
        Assert.Contains(@"C:\configs\shell.qml", notice.Body, StringComparison.Ordinal);
        Assert.Equal("Start a new one with dotnet new winshell.", notice.Note);
        Assert.Equal(TimeSpan.Zero, notice.Timeout);
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
        Fail(fatal: true);
        Fail(recovery: "");

        Assert.Empty(_shown);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(6, true)]
    [InlineData(5, false)]
    public void Windows_holding_notifications_back_is_read_as_a_toast_reads_it(int state, bool quiet) =>
        Assert.Equal(quiet, ShellNotices.QuietState(state));

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

    [Theory]
    [InlineData(5u, 25)]
    [InlineData(60u, 60)]
    public void A_setting_for_longer_notifications_lengthens_a_crash_notice_and_a_shorter_one_does_not(uint seconds, int expected) =>
        Assert.Equal(TimeSpan.FromSeconds(expected), ShellNotices.CrashTimeoutFor(seconds));

    [Fact]
    public async Task The_window_sits_above_the_taskbar_at_the_right_of_the_screen_and_says_the_whole_notice()
    {
        var read = await ShellFixture.On(async shell =>
        {
            var notice = Build();
            using var window = new NoticeWindow(notice, () => { }, () => { }, shell.Loop);
            await ShellFixture.LaidOut((FrameworkElement)window.Window.Content!);
            return (
                window.Window.Width,
                window.Window.Height,
                window.Window.Host!.HoldsEdge,
                AutomationProperties.GetName(window.Window.Content!),
                AutomationProperties.GetLiveSetting(window.Window.Content!));
        });

        Assert.Equal(364, read.Width);
        Assert.True(read.Height > 100);
        Assert.False(read.HoldsEdge);
        Assert.Contains("did not build", read.Item4, StringComparison.Ordinal);
        Assert.Contains("CS1002", read.Item4, StringComparison.Ordinal);
        Assert.Equal(Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive, read.Item5);
    }

    [Fact]
    public async Task The_window_is_tall_enough_for_its_button()
    {
        var fits = await ShellFixture.On(async shell =>
        {
            using var window = new NoticeWindow(Build(), () => { }, () => { }, shell.Loop);
            var root = (Grid)window.Window.Content!;
            var action = ((StackPanel)root.Children[0]).Children.OfType<Button>().Last();
            await ShellFixture.LaidOut(action);
            await Wait.UntilAsync(
                () => action.TransformToVisual(root).TransformPoint(default).Y + action.ActualHeight + root.Padding.Bottom <= root.ActualHeight + 0.5,
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
            var crash = ShellNotices.ForCrash(new CrashReport("r.txt", "x", new InvalidOperationException("no"), false) { Recovery = "Fine." }, TimeSpan.FromSeconds(25));
            using var first = new NoticeWindow(crash, () => { }, () => timed++, loop);
            using var second = new NoticeWindow(Build(), () => { }, () => untimed++, loop);
            loop.Advance(TimeSpan.FromSeconds(26));
            return (timed, untimed);
        });

        Assert.Equal((1, 0), dismissed);
    }
}
