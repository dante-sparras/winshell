using Winshell.Platform.Windows.Tests.Snapshots;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// What the notice must not do: run a script it was asked to open, ignore Escape after a click, outlive the error
/// it reports, or let a window that failed push a configuration error off the screen. Offscreen platform, manual loop.
/// </summary>
[Collection(ThemeCollection.Name)]
public sealed class ShellNoticeReviewTests : IDisposable
{
    private const string Good = """
        import Winshell
        ShellRoot {
          PanelWindow { objectName: "bar"; anchors.top: true; implicitHeight: 32 }
        }
        """;

    private const string Broken = """
        import Winshell
        ShellRoot {
          PanelWindow {
            objectName: "bar"
            colour: "red"
          }
        }
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualEventLoop _loop = new();
    private readonly SnapshotPlatform _platform = new(1);
    private readonly List<string> _opened = [];
    private ShellHost? _host;
    private ShellNotices? _notices;

    public ShellNoticeReviewTests() => Directory.CreateDirectory(_dir);

    private string Root => Path.Combine(_dir, "shell.qml");

    private static ModuleRegistry Modules()
    {
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        return registry;
    }

    private (ShellHost Host, ShellNotices Notices) Start(string shell, bool crashes = false)
    {
        File.WriteAllText(Root, shell);
        var log = new Log { Sink = (_, _, _) => { } };
        _host = new ShellHost(Root, _loop, _platform, log, Modules)
        {
            WatchFiles = false,
            Crashes = crashes ? new CrashReports(Path.Combine(_dir, "crashes")) : null,
        };
        _notices = new ShellNotices(_host, _loop, _platform, log, _opened.Add, Modules);
        _host.Start();
        _loop.Process();
        return (_host, _notices);
    }

    private bool NoticeShown => _platform.Windows.Any(w => ((ShellWindow)w.Client).ObjectName == "notice");

    private static Item Find(ShellWindow window, string name) =>
        All(window.Scene.Root).FirstOrDefault(i => i.ObjectName == name) ?? throw new InvalidOperationException($"No item {name}");

    private static IEnumerable<Item> All(Item item) => item.ChildItems.SelectMany(All).Prepend(item);

    private void Settle()
    {
        _loop.Process();
        PolishQueue.Flush();
        _loop.Process();
    }

    // Opening

    [Theory]
    [InlineData(@"C:\config\lib.js", "edit")]
    [InlineData(@"C:\config\lib.MJS", "edit")]
    [InlineData(@"C:\config\tool.bat", "edit")]
    [InlineData(@"C:\config\tool.vbs", "edit")]
    [InlineData(@"C:\config\qmldir", "edit")]
    [InlineData(@"C:\config\shell.qml", null)]
    [InlineData(@"C:\Users\you\AppData\Local\winshell\crashes\crash-20261029-000000-0000000Z-1-taskbar.txt", null)]
    public void A_script_is_opened_to_edit_never_to_run(string path, string? verb) =>
        Assert.Equal(verb, ShellNotices.VerbFor(path));

    [Fact]
    public void Open_file_opens_a_script_that_has_the_error_and_otherwise_the_configs_shell_qml()
    {
        File.WriteAllText(Path.Combine(_dir, "lib.js"), "function broken( {\n");
        File.WriteAllText(Root, Good);
        var lib = Path.Combine(_dir, "lib.js");

        Assert.Equal(lib, ShellNotices.ConfigFileOrRoot(lib, Root));
        Assert.Equal(Root, ShellNotices.ConfigFileOrRoot(Path.Combine(_dir, "missing.qml"), Root));
        Assert.Equal(Root, ShellNotices.ConfigFileOrRoot(@"C:\Windows\System32\calc.exe", Root));
        Assert.Equal(Root, ShellNotices.ConfigFileOrRoot("lib.js", Root));
        Assert.Equal(Root, ShellNotices.ConfigFileOrRoot("", Root));
    }

    [Fact]
    public void Open_file_on_an_error_that_names_no_existing_file_opens_shell_qml()
    {
        var (_, notices) = Start(Good);
        notices.Show(ShellNotices.ForConfigError(Path.Combine(_dir, "gone.js") + "[1:1]: SyntaxError: x", Root, running: true));
        _loop.Process();

        Find(notices.Window!, "action").Emit("clicked");
        _loop.Process();

        Assert.Equal([Root], _opened);
    }

    // The keyboard

    [Fact]
    public void A_click_on_the_text_gives_the_notice_the_keyboard_and_Escape_dismisses_it()
    {
        var (_, notices) = Start(Broken);
        var window = notices.Window!;
        Settle();
        Assert.Null(window.Scene.FocusItem);

        var body = Find(window, "body");
        var p = body.MapToScene(body.Width / 2, body.Height / 2);
        window.Scene.PointerPress(p.X, p.Y, QtEnums.LeftButton);
        window.Scene.PointerRelease(p.X, p.Y, QtEnums.LeftButton);
        Settle();
        Assert.NotNull(window.Scene.FocusItem);

        window.Scene.KeyPress(new KeyEvent { Key = 0x01000000 });
        Settle();

        Assert.False(NoticeShown);
        Assert.Empty(_opened);
    }

    [Fact]
    public void A_crash_notice_waits_while_it_has_the_keyboard()
    {
        var (_, notices) = Start(Good);
        notices.Show(ShellNotices.ForCrash(new CrashReport(Path.Combine(_dir, "report.txt"), "here", new InvalidOperationException("x"), false) { Recovery = "Something." }));
        Settle();
        var window = notices.Window!;
        var body = Find(window, "body");
        var p = body.MapToScene(body.Width / 2, body.Height / 2);
        window.Scene.PointerPress(p.X, p.Y, QtEnums.LeftButton);
        window.Scene.PointerRelease(p.X, p.Y, QtEnums.LeftButton);
        Settle();

        _loop.Advance(TimeSpan.FromSeconds(40));
        Assert.True(NoticeShown);

        // The pointer was on it too; it leaves, and the keyboard goes elsewhere.
        window.Scene.PointerLeave();
        _loop.Advance(TimeSpan.FromSeconds(40));
        Assert.True(NoticeShown);
        window.Scene.SetFocusItem(null);
        _loop.Advance(TimeSpan.FromSeconds(26));
        Assert.False(NoticeShown);
    }

    // Order

    [Fact]
    public void A_failed_and_a_fixed_reload_in_one_turn_leave_no_notice()
    {
        var (host, _) = Start(Good);

        File.WriteAllText(Root, Broken);
        host.Reload(hard: false);
        File.WriteAllText(Root, Good);
        host.Reload(hard: false);
        _loop.Process();

        Assert.False(NoticeShown);
    }

    [Fact]
    public void A_fixed_and_a_failed_reload_in_one_turn_leave_the_error()
    {
        var (host, notices) = Start(Good);

        File.WriteAllText(Root, Broken);
        host.Reload(hard: false);
        _loop.Process();
        host.Reload(hard: false);
        File.WriteAllText(Root, Good);
        host.Reload(hard: false);
        File.WriteAllText(Root, Broken);
        host.Reload(hard: false);
        _loop.Process();

        Assert.Equal(NoticeKind.ConfigError, notices.Current?.Kind);
    }

    [Fact]
    public void A_failure_the_shell_hides_closes_the_error_before_it()
    {
        var (host, _) = Start(Good);
        File.WriteAllText(Root, Broken);
        host.Reload(hard: false);
        _loop.Process();
        Assert.True(NoticeShown);

        var previous = Environment.GetEnvironmentVariable("WINSHELL_NO_RELOAD_POPUP");
        Environment.SetEnvironmentVariable("WINSHELL_NO_RELOAD_POPUP", "1");
        try
        {
            host.Reload(hard: false);
            _loop.Process();
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINSHELL_NO_RELOAD_POPUP", previous);
        }

        Assert.False(NoticeShown);
    }

    // A config error comes first

    [Fact]
    public void A_window_that_fails_does_not_push_a_config_error_off_the_screen()
    {
        var (host, notices) = Start(Good, crashes: true);
        File.WriteAllText(Root, Broken);
        host.Reload(hard: false);
        _loop.Process();
        Assert.Equal(NoticeKind.ConfigError, notices.Current?.Kind);

        var bar = _platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == "bar");
        bar.Client.OnFailed(new InvalidOperationException("broke"));
        _loop.Process();
        _loop.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(NoticeKind.ConfigError, notices.Current?.Kind);
        Assert.True(NoticeShown);
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "crashes")));
    }

    public void Dispose()
    {
        _notices?.Dispose();
        _host?.Dispose();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }
}
