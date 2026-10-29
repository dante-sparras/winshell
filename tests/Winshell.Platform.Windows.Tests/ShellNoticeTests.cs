using Winshell.Platform.Windows.Tests.Snapshots;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Winshell's own notice for a configuration error or a window that failed, against a shell on an offscreen
/// platform and a manual loop. Files open through a recorder, and crash reports go to the test's temp folder.
/// </summary>
[Collection(ThemeCollection.Name)]
public sealed class ShellNoticeTests : IDisposable
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
    private readonly List<string> _messages = [];
    private ShellHost? _host;
    private ShellNotices? _notices;

    public ShellNoticeTests() => Directory.CreateDirectory(_dir);

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
        var log = new Log { Sink = (level, category, message) => _messages.Add($"{level} {category}: {message}") };
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

    private SnapshotWindow NoticeWindow() => _platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == "notice");

    private bool NoticeShown => _platform.Windows.Any(w => ((ShellWindow)w.Client).ObjectName == "notice");

    private static Item Find(ShellWindow window, string name) =>
        All(window.Scene.Root).FirstOrDefault(i => i.ObjectName == name) ?? throw new InvalidOperationException($"No item {name}");

    private static IEnumerable<Item> All(Item item) => item.ChildItems.SelectMany(All).Prepend(item);

    private static IEnumerable<AccessibleNode> Flatten(AccessibleNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static string Text(ShellWindow window, string name) => (string)Find(window, name).ReadProperty("text")!;

    [Fact]
    public void A_config_that_fails_to_load_shows_where_the_error_is_and_that_nothing_runs()
    {
        var (_, notices) = Start(Broken);

        Assert.True(NoticeShown);
        var window = notices.Window!;
        var backend = NoticeWindow();
        Assert.True(backend.IsVisible);
        Assert.Equal("Your configuration has an error", Text(window, "title"));
        Assert.Equal("shell.qml, line 5, column 5", Text(window, "detail"));
        Assert.Equal("Cannot assign to non-existent property \"colour\"", Text(window, "body"));
        Assert.Equal("Nothing is running until it's fixed. Winshell loads it again when you save.", Text(window, "note"));
        Assert.Equal("Open file", Text(window, "action"));
        Assert.Equal("Winshell", Text(window, "appName"));
        // Bottom right, 12 pixels in, as wide as a Windows notification. Windows puts a panel that reserves no edge
        // in the work area, above the taskbar; this platform puts it on the whole screen.
        Assert.Equal(364, backend.Width);
        Assert.Equal(1920 - 364 - 12, backend.Origin.X);
        Assert.Equal(1080 - backend.Height - 12, backend.Origin.Y, 3);
        Assert.Equal(0.0, Convert.ToDouble(window.ReadProperty("exclusiveZone"), System.Globalization.CultureInfo.InvariantCulture));
        // Narrator finds the notice with everything it says, and its buttons by name.
        var tree = new AccessibleTree(window.Scene);
        tree.Refresh();
        var nodes = Flatten(tree.Root).ToList();
        var notice = Assert.Single(nodes, n => n.Role == AccessibleAttached.RoleValue.Notification);
        Assert.StartsWith("Your configuration has an error. shell.qml, line 5, column 5. Cannot assign to non-existent property", notice.Name, StringComparison.Ordinal);
        Assert.Equal(["Dismiss", "Open file", "Dismiss"], nodes.Where(n => n.Role == AccessibleAttached.RoleValue.Button).Select(n => n.Name));
        Assert.Equal(0, (int)window.ReadProperty("timeout")!);
        Assert.DoesNotContain(_messages, m => m.Contains(" notice: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reload_that_fails_says_the_last_version_keeps_running_and_a_fixed_reload_closes_it()
    {
        var (host, notices) = Start(Good);
        Assert.False(NoticeShown);

        File.WriteAllText(Root, Broken);
        host.Reload(hard: false);
        _loop.Process();

        Assert.Equal("Winshell keeps running the last version that loaded.", Text(notices.Window!, "note"));
        Assert.Contains(_platform.Windows, w => ((ShellWindow)w.Client).ObjectName == "bar");

        File.WriteAllText(Root, Good);
        host.Reload(hard: false);
        _loop.Process();

        Assert.False(NoticeShown);
        Assert.Null(notices.Current);
    }

    [Fact]
    public void Open_file_opens_the_file_with_the_error_and_closes_the_notice()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "bars"));
        File.WriteAllText(Path.Combine(_dir, "bars", "Bar.qml"), """
            import Winshell
            PanelWindow {
              colour: "red"
            }
            """);
        var (_, notices) = Start("""
            import Winshell
            import "bars"
            ShellRoot { Bar {} }
            """);
        Assert.Equal(Path.Combine("bars", "Bar.qml") + ", line 3, column 3", Text(notices.Window!, "detail"));

        Find(notices.Window!, "action").Emit("clicked");
        _loop.Process();

        Assert.Equal([Path.Combine(_dir, "bars", "Bar.qml")], _opened);
        Assert.False(NoticeShown);
    }

    [Fact]
    public void Dismiss_close_and_escape_each_close_the_notice()
    {
        var (host, notices) = Start(Broken);
        Find(notices.Window!, "dismiss").Emit("clicked");
        _loop.Process();
        Assert.False(NoticeShown);

        host.Reload(hard: false);
        _loop.Process();
        Find(notices.Window!, "close").Emit("clicked");
        _loop.Process();
        Assert.False(NoticeShown);

        host.Reload(hard: false);
        _loop.Process();
        var window = notices.Window!;
        window.Scene.SetFocusItem(Find(window, "action"));
        Assert.True(window.Scene.KeyPress(new KeyEvent { Key = 0x01000000 }));
        _loop.Process();
        Assert.False(NoticeShown);
        Assert.Empty(_opened);
    }

    [Fact]
    public void The_environment_variable_and_inhibit_reload_popup_hide_a_config_error()
    {
        var previous = Environment.GetEnvironmentVariable("WINSHELL_NO_RELOAD_POPUP");
        Environment.SetEnvironmentVariable("WINSHELL_NO_RELOAD_POPUP", "1");
        try
        {
            Start(Broken);
            Assert.False(NoticeShown);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINSHELL_NO_RELOAD_POPUP", previous);
        }
    }

    [Fact]
    public void A_shell_that_inhibits_the_popup_hides_the_notice_for_that_reload_only()
    {
        var (host, _) = Start("""
            import Winshell
            import QtQml
            ShellRoot {
              property int failures: 0
              Connections {
                target: Shell
                function onReloadFailed(error) {
                  failures++
                  if (failures === 1)
                    Shell.inhibitReloadPopup()
                }
              }
            }
            """);
        File.WriteAllText(Root, Broken);
        host.Reload(hard: false);
        _loop.Process();
        Assert.False(NoticeShown);

        host.Reload(hard: false);
        _loop.Process();
        Assert.True(NoticeShown);
        Assert.Equal(2, (int)host.Root!.ReadProperty("failures")!);
    }

    [Fact]
    public void A_window_that_failed_shows_what_stopped_and_opens_its_report()
    {
        File.WriteAllText(Path.Combine(_dir, "StatusBar.qml"), """
            import Winshell
            PanelWindow { objectName: "status"; anchors.top: true; implicitHeight: 32 }
            """);
        var (_, notices) = Start("""
            import Winshell
            ShellRoot { StatusBar {} }
            """, crashes: true);
        var bar = _platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == "status");

        bar.Client.OnFailed(new InvalidOperationException("broke"));
        _loop.Process();

        var window = notices.Window!;
        Assert.Equal(NoticeKind.Crash, notices.Current!.Kind);
        Assert.Equal("The status bar stopped working", Text(window, "title"));
        Assert.Equal("Winshell opens it again in 1 second.", Text(window, "body"));
        Assert.Equal("A crash report was saved on this PC.", Text(window, "note"));
        Assert.Equal("Open report", Text(window, "action"));
        Assert.False(Find(window, "detail").Visible);
        var report = Assert.Single(Directory.GetFiles(Path.Combine(_dir, "crashes")));
        Assert.Contains("Then: Winshell opens it again in 1 second.", File.ReadAllText(report), StringComparison.Ordinal);

        Find(window, "action").Emit("clicked");
        _loop.Process();
        Assert.Equal([report], _opened);
        Assert.False(NoticeShown);
    }

    [Fact]
    public void A_crash_notice_closes_by_itself_after_25_seconds_and_a_reload_leaves_it()
    {
        var (host, notices) = Start(Good, crashes: true);
        var bar = _platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == "bar");
        bar.Client.OnFailed(new InvalidOperationException("broke"));
        _loop.Process();
        Assert.Equal("A window stopped working", Text(notices.Window!, "title"));

        host.Reload(hard: false);
        _loop.Process();
        Assert.True(NoticeShown);

        _loop.Advance(TimeSpan.FromSeconds(24));
        Assert.True(NoticeShown);
        _loop.Advance(TimeSpan.FromSeconds(2));
        Assert.False(NoticeShown);
    }

    [Fact]
    public void A_window_that_gives_up_says_it_stays_closed()
    {
        var (_, notices) = Start(Good, crashes: true);
        ShellWindow Bar() => (ShellWindow)_host!.Root!.OwnedObjects.OfType<ShellWindow>().Single();
        for (var i = 0; i < 6; i++)
        {
            _platform.Windows.Single(w => w.Client == Bar()).Client.OnFailed(new InvalidOperationException($"failure {i}"));
            _loop.Process();
            if (i < 5)
                _loop.Advance(TimeSpan.FromSeconds(20));
        }

        Assert.Equal("It failed several times in a few minutes, so Winshell leaves it closed until the configuration reloads.", Text(notices.Window!, "body"));
    }

    [Fact]
    public void Reports_that_are_not_about_a_window_show_nothing()
    {
        var (host, _) = Start(Good, crashes: true);

        host.Crashes!.WriteOnce("event loop", new InvalidOperationException("a timer failed"));
        host.Crashes.Write("Winshell", new InvalidOperationException("the end"), fatal: true);
        _loop.Process();

        Assert.False(NoticeShown);
    }

    [Fact]
    public void The_notice_does_not_keep_the_shells_last_window_from_closing()
    {
        var (host, notices) = Start("""
            import Winshell
            import QtQml
            ShellRoot {
              id: root
              property bool lastClosed: false
              Connections {
                target: Shell
                function onLastWindowClosed() { root.lastClosed = true }
              }
              PanelWindow { id: bar; objectName: "bar"; anchors.top: true; implicitHeight: 32 }
            }
            """);
        notices.Show(ShellNotices.ForCrash(new CrashReport("report.txt", "here", new InvalidOperationException("x"), false) { Recovery = "Something." }));
        Assert.True(NoticeShown);

        var bar = host.Root!.OwnedObjects.OfType<ShellWindow>().Single(w => w.ObjectName == "bar");
        bar.WriteProperty("visible", false);
        _loop.Process();

        Assert.True((bool)host.Root.ReadProperty("lastClosed")!);
    }

    [Theory]
    [InlineData(@"C:\config\shell.qml[7:5]: Cannot assign to non-existent property ""colour""", "shell.qml, line 7, column 5", @"C:\config\shell.qml", "Cannot assign to non-existent property \"colour\"")]
    [InlineData(@"C:\config\bars\Bar.qml[12:3]: TypeError: x is not a function", @"bars\Bar.qml, line 12, column 3", @"C:\config\bars\Bar.qml", "TypeError: x is not a function")]
    [InlineData(@"C:\elsewhere\lib.js[1:1]: SyntaxError: Unexpected token", @"C:\elsewhere\lib.js, line 1, column 1", @"C:\elsewhere\lib.js", "SyntaxError: Unexpected token")]
    [InlineData(@"C:\config\shell.qml: InvalidOperationException: the runtime failed", "shell.qml", @"C:\config\shell.qml", "InvalidOperationException: the runtime failed")]
    [InlineData("Something without a place", "shell.qml", @"C:\config\shell.qml", "Something without a place")]
    public void A_config_error_is_shown_relative_to_the_config_folder(string error, string detail, string target, string body)
    {
        var notice = ShellNotices.ForConfigError(error, @"C:\config\shell.qml", running: true);

        Assert.Equal(detail, notice.Detail);
        Assert.Equal(target, notice.Target);
        Assert.Equal(body, notice.Body);
        Assert.Equal(TimeSpan.Zero, notice.Timeout);
    }

    [Fact]
    public void A_frame_that_could_not_be_drawn_shows_a_notice_and_leaves_the_window_up()
    {
        var (_, notices) = Start(Good, crashes: true);
        var bar = _host!.Root!.OwnedObjects.OfType<ShellWindow>().Single();
        _platform.Windows.Single(w => w.Client == bar).Client.OnDrawFailed(new InvalidOperationException("device lost"));
        _loop.Process();

        Assert.Contains(bar, _platform.Windows.Select(w => w.Client));
        Assert.True(NoticeShown);
        Assert.Equal("The panel stopped working", Text(notices.Window!, "title"));
        Assert.Equal("The panel could not draw. The rest of Winshell is still running.", Text(notices.Window!, "body"));
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
