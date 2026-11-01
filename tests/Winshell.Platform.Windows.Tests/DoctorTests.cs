using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Winshell.Platform.Windows.Diagnostics;
using Winshell.Platform.Windows.Services.Session;
using StartupTaskState = global::Windows.ApplicationModel.StartupTaskState;
using static Winshell.Platform.Windows.Diagnostics.Doctor;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// winshell doctor's checks: each judges facts passed in, so these tests need nothing of this PC. One test runs the
/// whole doctor here, which only reads. Every file is under a temp folder this class creates.
/// </summary>
public sealed class DoctorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-doctor-tests", Guid.NewGuid().ToString("N"));

    public DoctorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    // Windows and .NET

    [Theory]
    [InlineData(26300, 9711, "26H2", false, CheckStatus.Pass, "Windows 11 26H2, build 26300.9711")]
    [InlineData(22621, 0, "22H2", false, CheckStatus.Pass, "Windows 11 22H2, build 22621.0")]
    [InlineData(22000, 2538, "21H2", false, CheckStatus.Warn, "Windows 11 21H2, build 22000.2538")]
    [InlineData(19045, 5011, "22H2", false, CheckStatus.Fail, "Windows 10 22H2, build 19045.5011")]
    [InlineData(26100, 1, null, true, CheckStatus.Pass, "Windows Server, build 26100.1")]
    public void Windows_needs_11_and_22H2_for_its_materials(int build, int revision, string? version, bool server, CheckStatus status, string message)
    {
        var check = Doctor.Windows(build, revision, version, server);
        Assert.Equal(status, check.Status);
        Assert.Equal(message, check.Message);
        Assert.Equal(status == CheckStatus.Pass, check.Fix is null);
    }

    [Fact]
    public void DotNet_says_where_the_runtime_comes_from()
    {
        Assert.Equal(new DoctorCheck("dotnet", ".NET", CheckStatus.Pass, ".NET 10.0.1, built into Winshell"), DotNet(new Version(10, 0, 1), ".NET 10.0.1", selfContained: true));
        Assert.Equal(CheckStatus.Fail, DotNet(new Version(9, 0), ".NET 9.0.0", selfContained: false).Status);
    }

    // Config

    [Fact]
    public void Config_reports_the_first_error_a_problem_or_a_failed_load()
    {
        Assert.Equal(CheckStatus.Fail, Config(null, "Several configs in C:\\c; pick one with -c: a, b", new ConfigScan(0, []), null).Status);
        Assert.Equal(CheckStatus.Pass, Config(null, null, new ConfigScan(0, []), null).Status);
        Assert.Contains("default shell", Config(null, null, new ConfigScan(0, []), null).Message, StringComparison.Ordinal);

        var broken = Config(@"C:\c\shell.qml", null, new ConfigScan(3, [@"C:\c\Bar.qml[3:5]: Expected }", @"C:\c\Baz.qml[1:1]: x"]), null);
        Assert.Equal(CheckStatus.Fail, broken.Status);
        Assert.Equal(@"C:\c\Bar.qml[3:5]: Expected } (1 more)", broken.Message);

        var failed = Config(@"C:\c\shell.qml", null, new ConfigScan(1, []), "shell.qml[7:5]: Cannot assign to non-existent property \"colour\"");
        Assert.Equal(CheckStatus.Fail, failed.Status);
        Assert.EndsWith("could not load it: shell.qml[7:5]: Cannot assign to non-existent property \"colour\"", failed.Message, StringComparison.Ordinal);

        Assert.Equal(new DoctorCheck("config", "Config", CheckStatus.Pass, @"C:\c\shell.qml: 3 QML files read without errors"), Config(@"C:\c\shell.qml", null, new ConfigScan(3, []), null));
    }

    [Fact]
    public void Every_qml_file_of_a_config_is_read_but_hidden_folders_and_packages_are_left_out()
    {
        File.WriteAllText(Path.Combine(_dir, "shell.qml"), "import Winshell\nShellRoot {}\n");
        Directory.CreateDirectory(Path.Combine(_dir, "bars"));
        File.WriteAllText(Path.Combine(_dir, "bars", "Bar.qml"), "import QtQuick\nItem {\n  width: 10\n");
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        File.WriteAllText(Path.Combine(_dir, ".git", "Broken.qml"), "{{{");
        Directory.CreateDirectory(Path.Combine(_dir, "node_modules"));
        File.WriteAllText(Path.Combine(_dir, "node_modules", "Broken.qml"), "{{{");

        var scan = DoctorProbe.ParseConfig(Path.Combine(_dir, "shell.qml"));

        Assert.Equal(2, scan.Files);
        Assert.Empty(scan.Errors);
        var error = Assert.Single(scan.SubfolderErrors);
        Assert.StartsWith(Path.Combine(_dir, "bars", "Bar.qml") + "[", error, StringComparison.Ordinal);
        Assert.False(scan.Stopped);
        Assert.Equal(new ConfigScan(0, []), DoctorProbe.ParseConfig(null));
    }

    [Fact]
    public void An_error_beside_shell_qml_fails_one_further_down_warns_and_other_configs_are_left_out()
    {
        File.WriteAllText(Path.Combine(_dir, "shell.qml"), "import Winshell\nShellRoot {}\n");
        File.WriteAllText(Path.Combine(_dir, "Bar.qml"), "import QtQuick\nItem {\n");
        Directory.CreateDirectory(Path.Combine(_dir, "default"));
        File.WriteAllText(Path.Combine(_dir, "default", "shell.qml"), "{{{");

        var scan = DoctorProbe.ParseConfig(Path.Combine(_dir, "shell.qml"));

        Assert.Equal(2, scan.Files);
        Assert.StartsWith(Path.Combine(_dir, "Bar.qml") + "[", Assert.Single(scan.Errors), StringComparison.Ordinal);
        Assert.Empty(scan.SubfolderErrors);

        var warned = Config(@"C:\c\shell.qml", null, new ConfigScan(4, []) { SubfolderErrors = [@"C:\c\sub\Bad.qml[2:1]: Unterminated object 'Item'", "x"] }, null);
        Assert.Equal(CheckStatus.Warn, warned.Status);
        Assert.Equal(@"C:\c\sub\Bad.qml[2:1]: Unterminated object 'Item' (1 more). It does not parse; Winshell only fails if the config imports it.", warned.Message);
        var stopped = Config(@"C:\c\shell.qml", null, new ConfigScan(4, []) { Stopped = true }, null);
        Assert.Equal(CheckStatus.Pass, stopped.Status);
        Assert.EndsWith($"4 QML files read without errors. Stopped after {DoctorProbe.MaxFolders} folders; the rest were not read.", stopped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_junction_back_to_the_config_is_not_followed()
    {
        File.WriteAllText(Path.Combine(_dir, "shell.qml"), "import Winshell\nShellRoot {}\n");
        Directory.CreateDirectory(Path.Combine(_dir, "bars"));
        File.WriteAllText(Path.Combine(_dir, "bars", "Bar.qml"), "import QtQuick\nItem {}\n");
        var loop = Path.Combine(_dir, "bars", "loop");
        Junction(loop, _dir);
        Doctor.ConfigScan scan;
        try
        {
            scan = DoctorProbe.ParseConfig(Path.Combine(_dir, "shell.qml"));
        }
        finally
        {
            // Only the link goes; what it points to stays.
            Directory.Delete(loop);
        }

        Assert.Equal(2, scan.Files);
        Assert.Empty(scan.Errors);
        Assert.Empty(scan.SubfolderErrors);
    }

    [Fact]
    public void Reading_a_config_stops_after_so_many_folders()
    {
        File.WriteAllText(Path.Combine(_dir, "shell.qml"), "import Winshell\nShellRoot {}\n");
        for (var i = 0; i < DoctorProbe.MaxFolders; i++)
            Directory.CreateDirectory(Path.Combine(_dir, $"f{i:D3}"));

        var scan = DoctorProbe.ParseConfig(Path.Combine(_dir, "shell.qml"));

        Assert.Equal(1, scan.Files);
        Assert.True(scan.Stopped);
    }

    [Fact]
    public void The_last_load_in_the_log_decides()
    {
        const string Failed = "08:00:01.000 ERROR scene: Failed to load configuration: C:\\c\\shell.qml[7:5]: Cannot assign\r\n";
        const string Reloaded = "08:00:02.000  INFO: Configuration Loaded\r\n";
        const string Refailed = "08:00:03.000 ERROR scene: Failed to reload configuration: C:\\c\\Bar.qml[1:1]: x\r\n";
        const string Launched = "08:00:04.000  INFO: Launching config: \"C:\\c\\shell.qml\"\r\n";

        Assert.Null(DoctorProbe.LastLoadErrorIn(""));
        Assert.Equal("C:\\c\\shell.qml[7:5]: Cannot assign", DoctorProbe.LastLoadErrorIn(Failed));
        Assert.Null(DoctorProbe.LastLoadErrorIn(Failed + Reloaded));
        Assert.Equal("C:\\c\\Bar.qml[1:1]: x", DoctorProbe.LastLoadErrorIn(Failed + Reloaded + Refailed));
        Assert.Null(DoctorProbe.LastLoadErrorIn(Failed + Launched));
    }

    // Folders

    [Fact]
    public void Folders_fail_when_one_cannot_be_written_and_warn_about_an_elevated_instance()
    {
        var ok = new[] { ("the config folder", @"C:\a", true), ("its local folder", @"C:\b", true) };
        Assert.Equal(new DoctorCheck("folders", "Folders", CheckStatus.Pass, "Winshell can write the config folder and its local folder"), Folders(ok, []));
        var locked = Folders([("the config folder", @"C:\a", false), ("its local folder", @"C:\b", true)], []);
        Assert.Equal(CheckStatus.Fail, locked.Status);
        Assert.Equal(@"Winshell cannot write the config folder, C:\a.", locked.Message);
        var elevated = Folders(ok, ["abc123"]);
        Assert.Equal(CheckStatus.Warn, elevated.Status);
        Assert.StartsWith("Instance abc123 runs as administrator.", elevated.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_doctor_run_as_administrator_says_so()
    {
        var check = Folders([("the config folder", @"C:\a", true)], [], asAdministrator: true);
        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Contains("This ran as administrator", check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("administrator", Folders([("the config folder", @"C:\a", true)], []).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_that_denies_writing_cannot_be_written_and_is_left_as_it_was()
    {
        var locked = Path.Combine(_dir, "locked");
        Directory.CreateDirectory(locked);
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var folder = new DirectoryInfo(locked);
        var acl = folder.GetAccessControl();
        acl.AddAccessRule(deny);
        folder.SetAccessControl(acl);
        try
        {
            Assert.False(DoctorProbe.CanWrite(locked));
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            folder.SetAccessControl(acl);
        }
        Assert.Empty(Directory.EnumerateFileSystemEntries(locked));
    }

    [Fact]
    public void Telling_whether_a_folder_can_be_written_leaves_nothing_behind()
    {
        Assert.True(DoctorProbe.CanWrite(_dir));
        // A folder Winshell would make: the nearest one that is there is asked instead.
        Assert.True(DoctorProbe.CanWrite(Path.Combine(_dir, "not", "yet")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
    }

    // App bars

    private static MonitorFacts Screen(string name = "DISPLAY1", int x = 0, int top = 0, int bottom = 0, int left = 0, int right = 0) =>
        new(name, x, 0, x + 1920, 1080, x + left, top, x + 1920 - right, 1080 - bottom, 1);

    private static WindowFacts Bar(string className, int x, int top, int width, int height, string program = "explorer.exe") =>
        new(className, x, top, x + width, top + height, program);

    [Fact]
    public void An_edge_the_taskbar_or_winshell_reserves_is_fine()
    {
        Assert.Equal(CheckStatus.Pass, AppBars([Screen(bottom: 48)], [Bar(TaskbarClass, 0, 1032, 1920, 48)]).Status);
        Assert.Equal(CheckStatus.Pass, AppBars([Screen(bottom: 48)], [Bar(WinshellClass, 0, 1032, 1920, 48, "winshell.exe")]).Status);
        Assert.Equal(CheckStatus.Pass, AppBars([Screen()], []).Status);
    }

    [Fact]
    public void An_edge_another_app_reserves_is_named()
    {
        var unknown = AppBars([Screen(bottom: 48)], []);
        Assert.Equal(CheckStatus.Warn, unknown.Status);
        Assert.Equal("Another app reserves 48 pixels at the bottom of DISPLAY1.", unknown.Message);

        // On the second screen, beside a taskbar on the first.
        var named = AppBars(
            [Screen(bottom: 48), Screen("DISPLAY2", x: 1920, top: 40)],
            [Bar(TaskbarClass, 0, 1032, 1920, 48), Bar("YasbBar", 1920, 0, 1920, 40, "yasb.exe"), Bar("Chrome_WidgetWin_1", 1920, 0, 1920, 1080, "chrome.exe")]);
        Assert.Equal("yasb.exe reserves 40 pixels at the top of DISPLAY2.", named.Message);
    }

    [Fact]
    public void A_winshell_panel_explains_its_margins_and_a_zone_other_than_its_size()
    {
        // A floating panel: 8 pixels of margin above and below a 40-pixel window reserve 56.
        Assert.Equal(CheckStatus.Pass, AppBars([Screen(top: 56)], [Bar(WinshellClass, 0, 8, 1920, 40, "winshell.exe")]).Status);
        // Thin windows along the edge, such as another app's 2-pixel border, are not app bars.
        Assert.Equal(CheckStatus.Pass, AppBars([Screen(top: 56)], [Bar(WinshellClass, 0, 8, 1920, 40, "winshell.exe"), Bar("Border", 0, 0, 1920, 2, "borders.exe"), Bar("EdgeUiInputTopWndClass", 15, 0, 1905, 4)]).Status);
        // A panel taller than its exclusive zone.
        Assert.Equal(CheckStatus.Pass, AppBars([Screen(top: 40)], [Bar(WinshellClass, 0, 0, 1920, 60, "winshell.exe")]).Status);
        // A Winshell window filling the screen is no panel, and explains nothing.
        Assert.Equal(CheckStatus.Warn, AppBars([Screen(top: 40)], [Bar(WinshellClass, 0, 0, 1920, 1080, "winshell.exe")]).Status);
    }

    [Fact]
    public void An_app_bar_overlapping_a_winshell_panel_is_named()
    {
        // The panel covers the reserved strip, and yasb sits in the same pixels.
        var overlap = AppBars(
            [Screen(top: 40)],
            [Bar(WinshellClass, 0, 0, 1920, 40, "winshell.exe"), Bar("YasbBar", 0, 0, 1920, 40, "yasb.exe")]);
        Assert.Equal(CheckStatus.Warn, overlap.Status);
        Assert.Equal("yasb.exe reserves space at the top of DISPLAY1.", overlap.Message);

        // yasb is thicker than the 8 pixels the panel leaves above a window at 8–48.
        var thicker = AppBars(
            [Screen(top: 48)],
            [Bar(WinshellClass, 0, 8, 1920, 40, "winshell.exe"), Bar("YasbBar", 0, 0, 1920, 48, "yasb.exe")]);
        Assert.Equal(CheckStatus.Warn, thicker.Status);
        Assert.Equal("yasb.exe reserves space at the top of DISPLAY1.", thicker.Message);
    }

    [Fact]
    public void Panels_side_by_side_count_once_and_an_app_beside_them_is_named()
    {
        var check = AppBars(
            [Screen(top: 80)],
            [Bar(WinshellClass, 0, 0, 960, 40, "winshell.exe"), Bar(WinshellClass, 960, 0, 960, 40, "winshell.exe"), Bar("YasbBar", 0, 40, 1920, 40, "yasb.exe")]);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal("yasb.exe reserves space at the top of DISPLAY1.", check.Message);

        var taskbars = AppBars([Screen(bottom: 96)], [Bar(TaskbarClass, 0, 1032, 960, 48), Bar(SecondaryTaskbarClass, 960, 1032, 960, 48)]);
        Assert.Equal("Another app reserves 48 pixels at the bottom of DISPLAY1.", taskbars.Message);
    }

    [Fact]
    public void The_taskbar_and_a_winshell_panel_on_one_edge_are_reported()
    {
        var both = AppBars([Screen(bottom: 96)], [Bar(TaskbarClass, 0, 1032, 1920, 48), Bar(WinshellClass, 0, 984, 1920, 48, "winshell.exe")]);
        Assert.Equal(CheckStatus.Pass, AppBars([Screen(bottom: 48)], [Bar(TaskbarClass, 0, 1032, 1920, 48)]).Status);
        Assert.Equal(CheckStatus.Warn, both.Status);
        Assert.Contains("both reserve the bottom of DISPLAY1", both.Message, StringComparison.Ordinal);
    }

    // DPI

    [Fact]
    public void A_scaling_override_fails_and_another_compatibility_mode_warns()
    {
        var screens = new[] { Screen() with { Scale = 1.5 }, Screen("DISPLAY2", x: 1920) };
        Assert.Equal(new DoctorCheck("dpi", "DPI", CheckStatus.Pass, "Per-monitor aware. DISPLAY1 at 150%, DISPLAY2 at 100%"), Dpi(true, [], screens));
        Assert.Equal(CheckStatus.Fail, Dpi(true, ["~ DPIUNAWARE"], screens).Status);
        Assert.Equal(CheckStatus.Fail, Dpi(true, ["~ GDIDPISCALING DPIUNAWARE"], screens).Status);
        Assert.Equal(CheckStatus.Fail, Dpi(false, [], screens).Status);
        Assert.Equal(CheckStatus.Warn, Dpi(true, ["WIN8RTM"], screens).Status);
        var admin = Dpi(true, ["~ RUNASADMIN"], screens);
        Assert.Equal(CheckStatus.Warn, admin.Status);
        Assert.Equal("Right-click winshell.exe, choose Properties > Compatibility, and clear Run this program as an administrator.", admin.Fix);
    }

    [Fact]
    public void Scaling_settings_a_per_monitor_winshell_ignores_warn_and_point_at_their_box()
    {
        var screens = new[] { Screen() };
        var application = Dpi(true, ["~ HIGHDPIAWARE"], screens);
        Assert.Equal(CheckStatus.Warn, application.Status);
        Assert.EndsWith("clear Override high DPI scaling behavior.", application.Fix, StringComparison.Ordinal);
        var program = Dpi(true, ["~ PERPROCESSSYSTEMDPIFORCEON"], screens);
        Assert.Equal(CheckStatus.Warn, program.Status);
        Assert.EndsWith("clear Use this setting to fix scaling problems for this program instead of the one in Settings.", program.Fix, StringComparison.Ordinal);
        Assert.DoesNotContain("compatibility mode", program.Message, StringComparison.Ordinal);
        var unaware = Dpi(false, ["~ HIGHDPIAWARE"], screens);
        Assert.Equal(CheckStatus.Fail, unaware.Status);
        Assert.StartsWith("Winshell is not per-monitor DPI aware", unaware.Message, StringComparison.Ordinal);
    }

    // Sign-in

    [Fact]
    public void The_sign_in_entry_must_start_this_winshell_and_be_on()
    {
        const string Here = @"C:\Program Files\Winshell\winshell.exe";
        var command = $@"C:\Windows\System32\conhost.exe --headless ""{Here}"" --no-duplicate";
        bool Exists(string path) => path == Here || path == @"C:\Old\winshell.exe";

        Assert.Equal(CheckStatus.Pass, SignIn(null, false, Here, Exists).Status);
        Assert.Equal(new DoctorCheck("signIn", "Sign-in", CheckStatus.Pass, "Winshell starts when you sign in"), SignIn(command, false, Here, Exists));
        Assert.Equal(CheckStatus.Warn, SignIn(command, true, Here, Exists).Status);
        Assert.Equal(CheckStatus.Warn, SignIn(@"C:\Windows\System32\conhost.exe --headless C:\Old\winshell.exe -c bar", false, Here, Exists).Status);
        var gone = SignIn(@"C:\Windows\System32\conhost.exe --headless C:\Gone\winshell.exe", false, Here, Exists);
        Assert.Equal(CheckStatus.Fail, gone.Status);
        Assert.Equal(@"The sign-in entry starts C:\Gone\winshell.exe, which is not there.", gone.Message);
        Assert.Equal(Here, DoctorProbe.ExeOf($@"""{Here}"" -p ""C:\my config"""));
        Assert.Null(DoctorProbe.ExeOf("  "));
    }

    [Fact]
    public void A_sign_in_entry_through_a_link_to_this_winshell_is_this_winshell()
    {
        const string Here = @"C:\Users\a\AppData\Local\Microsoft\WinGet\Packages\Winshell\winshell.exe";
        const string Link = @"C:\Users\a\AppData\Local\Microsoft\WinGet\Links\winshell.exe";
        string Resolve(string path) => path == Link ? Here : path;

        Assert.Equal(CheckStatus.Pass, SignIn($@"conhost.exe --headless ""{Link}"" --no-duplicate", false, Here, _ => true, Resolve).Status);
        Assert.Equal(CheckStatus.Warn, SignIn($@"conhost.exe --headless ""{Link}"" --no-duplicate", false, Here, _ => true).Status);

        var target = Path.Combine(_dir, "target.exe");
        File.WriteAllText(target, "");
        Assert.Equal(target, DoctorProbe.ResolveLinks(target));
    }

    [Fact]
    public void A_packaged_sign_in_is_the_startup_task_not_a_path()
    {
        Assert.Equal(CheckStatus.Pass, SignIn(FakeTask(StartupTaskState.Disabled)).Status);
        Assert.StartsWith("Winshell does not start when you sign in.", SignIn(FakeTask(StartupTaskState.Disabled)).Message, StringComparison.Ordinal);
        Assert.Equal(new DoctorCheck("signIn", "Sign-in", CheckStatus.Pass, "Winshell starts when you sign in"), SignIn(FakeTask(StartupTaskState.Enabled)));
        Assert.Equal(CheckStatus.Pass, SignIn(FakeTask(StartupTaskState.EnabledByPolicy)).Status);

        var user = SignIn(FakeTask(StartupTaskState.DisabledByUser));
        Assert.Equal(CheckStatus.Warn, user.Status);
        Assert.Equal("Winshell is set to start when you sign in, but Startup apps in Windows turned it off.", user.Message);

        var policy = SignIn(FakeTask(StartupTaskState.DisabledByPolicy));
        Assert.Equal(CheckStatus.Warn, policy.Status);
        Assert.Contains("policy", policy.Message, StringComparison.OrdinalIgnoreCase);

        var missing = SignIn((IStartupTask?)null);
        Assert.Equal(CheckStatus.Warn, missing.Status);
        Assert.Contains("no startup task", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IStartupTask FakeTask(StartupTaskState state) => new StateTask(state);

    private sealed class StateTask(StartupTaskState state) : IStartupTask
    {
        public StartupTaskState State => state;

        public StartupTaskState RequestEnable() => state;

        public bool Disable() => false;
    }

    [Fact]
    public void A_sign_in_entry_for_the_console_exe_is_the_windowed_copy_beside_it()
    {
        const string windowed = @"C:\Program Files\Winshell\winshellw.exe";
        const string console = @"C:\Program Files\Winshell\winshell.exe";
        var command = $@"C:\Windows\System32\conhost.exe --headless ""{console}"" --no-duplicate";
        Assert.Equal(CheckStatus.Pass, SignIn(command, false, windowed, path => path == console).Status);
    }

    // Notifications, instances, crashes

    [Theory]
    [InlineData("Allowed", CheckStatus.Pass)]
    [InlineData("Denied", CheckStatus.Warn)]
    [InlineData("Unspecified", CheckStatus.Warn)]
    [InlineData("Unavailable", CheckStatus.Warn)]
    public void Notification_access_is_read_without_asking(string access, CheckStatus status) =>
        Assert.Equal(status, Doctor.Notifications(access).Status);

    [Fact]
    public void Running_instances_and_recent_crash_reports_are_listed()
    {
        Assert.Equal("No Winshell is running", Instances([]).Message);
        Assert.Equal(@"abc (process 42) runs C:\c\shell.qml, log C:\l\log.txt", Instances([new DoctorInstance("abc", 42, @"C:\c\shell.qml", @"C:\l\log.txt")]).Message);

        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(CheckStatus.Pass, Crashes([(@"C:\r\old.txt", now.AddDays(-8))], now).Status);
        var recent = Crashes([(@"C:\r\old.txt", now.AddDays(-8)), (@"C:\r\a.txt", now.AddDays(-2)), (@"C:\r\b.txt", now.AddHours(-1))], now);
        Assert.Equal(CheckStatus.Warn, recent.Status);
        Assert.Equal(@"2 crash reports in the last 7 days. The newest is C:\r\b.txt.", recent.Message);
    }

    [Fact]
    public void The_user_folders_are_written_as_their_variables()
    {
        (string, string)[] folders = [(@"C:\Users\Ann", "%USERPROFILE%"), (@"C:\Users\Ann\AppData\Local", "%LOCALAPPDATA%"), (@"C:\Users\Ann\AppData\Roaming\", "%APPDATA%")];
        DoctorCheck[] checks =
        [
            new("crashes", "Crashes", CheckStatus.Warn, @"The newest is C:\Users\Ann\AppData\Local\winshell\crashes\a.txt.", @"Open c:\users\ann\appdata\roaming\winshell or C:\Users\Ann."),
            new("x", "X", CheckStatus.Pass, @"C:\Users\Anna\x and C:\Users\Ann.old stay"),
        ];

        var shown = WithoutUserFolders(checks, folders);

        Assert.Equal(@"The newest is %LOCALAPPDATA%\winshell\crashes\a.txt.", shown[0].Message);
        Assert.Equal(@"Open %APPDATA%\winshell or %USERPROFILE%.", shown[0].Fix);
        Assert.Equal(@"C:\Users\Anna\x and C:\Users\Ann.old stay", shown[1].Message);
        Assert.Null(shown[1].Fix);
    }

    [Fact]
    public void A_check_that_throws_warns_instead_of_ending_the_run()
    {
        var check = Guard("dpi", "DPI", () => throw new InvalidOperationException("no screens"));
        Assert.Equal(new DoctorCheck("dpi", "DPI", CheckStatus.Warn, "Could not check: InvalidOperationException: no screens", $"Report it at {BugReports}."), check);
    }

    // Output

    private static readonly DoctorCheck[] Sample =
    [
        new("windows", "Windows", CheckStatus.Pass, "Windows 11 26H2, build 26300.1"),
        new("signIn", "Sign-in", CheckStatus.Warn, "Startup apps turned it off.", "Turn Winshell on in Settings > Apps > Startup."),
        new("config", "Config", CheckStatus.Fail, "shell.qml[1:1]: x", "Correct that line."),
    ];

    [Fact]
    public void Text_has_a_line_a_check_a_fix_under_each_problem_and_a_count()
    {
        var text = Format(Sample, "1.2.3", color: false);

        Assert.Equal(
        [
            "Winshell 1.2.3: this PC's setup",
            "",
            "  PASS  Windows  Windows 11 26H2, build 26300.1",
            "  WARN  Sign-in  Startup apps turned it off.",
            "                 Fix: Turn Winshell on in Settings > Apps > Startup.",
            "  FAIL  Config   shell.qml[1:1]: x",
            "                 Fix: Correct that line.",
            "",
            "1 passed, 1 warning, 1 failed.",
        ], text.TrimEnd().Split(Environment.NewLine));
        Assert.Contains("\e[31mFAIL\e[0m", Format(Sample, "1.2.3", color: true), StringComparison.Ordinal);
    }

    [Fact]
    public void Json_has_every_check_and_the_counts()
    {
        using var json = JsonDocument.Parse(ToJson(Sample, "1.2.3+abc"));
        var root = json.RootElement;

        Assert.Equal("1.2.3+abc", root.GetProperty("version").GetString());
        var checks = root.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal(["windows", "signIn", "config"], checks.Select(c => c.GetProperty("id").GetString()));
        Assert.Equal(["pass", "warn", "fail"], checks.Select(c => c.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, checks[0].GetProperty("fix").ValueKind);
        Assert.Equal("Correct that line.", checks[2].GetProperty("fix").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("fail").GetInt32());
        Assert.DoesNotContain("\\u002B", ToJson(Sample, "1.2.3+abc"), StringComparison.Ordinal);
    }

    // On this PC

    [Fact]
    public void The_doctor_runs_every_check_here_and_changes_nothing()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "shell.qml"), "import Winshell\nShellRoot {}\n");
        var before = Directory.EnumerateFileSystemEntries(_dir, "*", SearchOption.AllDirectories).Order().ToList();

        var checks = Run(new DoctorContext(Environment.ProcessPath!, config, Path.Combine(config, "shell.qml"), null, [], Path.Combine(_dir, "local"), Path.Combine(_dir, "crashes")));

        Assert.Equal(["windows", "dotnet", "config", "folders", "appBars", "dpi", "signIn", "notifications", "instances", "crashes"], checks.Select(c => c.Id));
        Assert.DoesNotContain(checks, c => c.Message.StartsWith("Could not check", StringComparison.Ordinal));
        Assert.Equal(CheckStatus.Pass, checks.Single(c => c.Id == "config").Status);
        Assert.Equal(CheckStatus.Pass, checks.Single(c => c.Id == "crashes").Status);
        Assert.All(checks.Where(c => c.Status != CheckStatus.Pass), c => Assert.False(string.IsNullOrEmpty(c.Fix)));
        Assert.Equal(before, Directory.EnumerateFileSystemEntries(_dir, "*", SearchOption.AllDirectories).Order().ToList());
        Assert.DoesNotContain(checks, c => c.Message.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_downloaded_config_fails_and_says_how_to_unblock_it()
    {
        var check = Config(@"C:\c\shell.qml", null, new ConfigScan(0, []), null, downloaded: true);

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal(@"C:\c\shell.qml was downloaded from the internet, so Winshell does not run it. It would run as you.", check.Message);
        Assert.Contains("Unblock-File", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void The_doctor_reads_the_zone_of_a_config_and_not_its_files()
    {
        var config = Path.Combine(_dir, "downloaded");
        Directory.CreateDirectory(config);
        var root = Path.Combine(config, "shell.qml");
        // A syntax error the doctor would report if it parsed the file.
        File.WriteAllText(root, "import Winshell\nShellRoot {\n");
        File.WriteAllText(root + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        var checks = Run(new DoctorContext(Environment.ProcessPath!, config, root, null, [], Path.Combine(_dir, "local"), Path.Combine(_dir, "crashes")));

        var check = checks.Single(c => c.Id == "config");
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.EndsWith("shell.qml was downloaded from the internet, so Winshell does not run it. It would run as you.", check.Message, StringComparison.Ordinal);
    }

    /// <summary>Makes a junction, which needs no administrator, as <c>mklink /J</c> does.</summary>
    private static void Junction(string link, string target)
    {
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
