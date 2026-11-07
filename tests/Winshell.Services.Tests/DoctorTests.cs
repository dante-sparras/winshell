using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Winshell.Diagnostics;
using StartupTaskState = Windows.ApplicationModel.StartupTaskState;
using static Winshell.Diagnostics.Doctor;
using static Winshell.Diagnostics.DoctorCrashes;
using static Winshell.Diagnostics.DoctorReport;
using static Winshell.Diagnostics.DoctorScreens;
using static Winshell.Diagnostics.DoctorSignIn;

namespace Winshell.Services.Tests;

/// <summary>
/// winshell doctor's checks: each judges facts passed in, so these tests need nothing of this PC. Two tests run the
/// whole doctor here. Every file is under a temp folder this class creates.
/// </summary>
public sealed class DoctorTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "winshell-doctor-tests", Guid.NewGuid().ToString("N"));

    public DoctorTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, true);
    }

    // Windows and .NET

    [Theory]
    [InlineData(26300, 9711, "26H2", false, CheckStatus.Pass, "Windows 11 26H2, build 26300.9711")]
    [InlineData(22621, 0, "22H2", false, CheckStatus.Pass, "Windows 11 22H2, build 22621.0")]
    [InlineData(22000, 2538, "21H2", false, CheckStatus.Warn, "Windows 11 21H2, build 22000.2538")]
    [InlineData(19045, 5011, "22H2", false, CheckStatus.Fail, "Windows 10 22H2, build 19045.5011")]
    [InlineData(26100, 1, null, true, CheckStatus.Pass, "Windows Server, build 26100.1")]
    public void Windows_needs_11_and_22H2_for_its_materials(int build, int revision, string? version, bool server,
        CheckStatus status, string message)
    {
        var check = Doctor.Windows(build, revision, version, server);
        Assert.Equal(status, check.Status);
        Assert.Equal(message, check.Message);
        Assert.Equal(status == CheckStatus.Pass, check.Fix is null);
    }

    [Fact]
    public void DotNet_says_where_the_runtime_comes_from()
    {
        Assert.Equal(new DoctorCheck("dotnet", ".NET", CheckStatus.Pass, ".NET 10.0.1, built into Winshell"),
            DotNet(new Version(10, 0, 1), ".NET 10.0.1", true));
        Assert.Equal(CheckStatus.Fail, DotNet(new Version(9, 0), ".NET 9.0.0", false).Status);
    }

    // Folders

    [Fact]
    public void Folders_fail_when_one_cannot_be_written_and_warn_about_an_elevated_instance()
    {
        var ok = new[] { ("the config folder", @"C:\a", true), ("its local folder", @"C:\b", true) };
        Assert.Equal(
            new DoctorCheck("folders", "Folders", CheckStatus.Pass,
                "Winshell can write the config folder and its local folder"), Folders(ok, []));
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
        var check = Folders([("the config folder", @"C:\a", true)], [], true);
        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Contains("This ran as administrator", check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("administrator", Folders([("the config folder", @"C:\a", true)], []).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_that_denies_writing_cannot_be_written_and_is_left_as_it_was()
    {
        var locked = Path.Combine(_dir, "locked");
        Directory.CreateDirectory(locked);
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles,
            AccessControlType.Deny);
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
    public void A_config_folder_the_doctor_cannot_list_fails_the_project_check_in_its_own_words()
    {
        var unreadable = Path.Combine(_dir, "unreadable");
        Directory.CreateDirectory(unreadable);
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory,
            AccessControlType.Deny);
        var folder = new DirectoryInfo(unreadable);
        var acl = folder.GetAccessControl();
        acl.AddAccessRule(deny);
        folder.SetAccessControl(acl);
        IReadOnlyList<DoctorCheck> checks;
        try
        {
            checks = DoctorProject.Configuration(unreadable);
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            folder.SetAccessControl(acl);
        }

        Assert.Equal(CheckStatus.Fail, checks[0].Status);
        Assert.StartsWith("Winshell cannot read the config folder: ", checks[0].Message, StringComparison.Ordinal);
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

    private static MonitorFacts Screen(string name = "DISPLAY1", int x = 0, int top = 0, int bottom = 0, int left = 0,
        int right = 0)
    {
        return new MonitorFacts(name, x, 0, x + 1920, 1080, x + left, top, x + 1920 - right, 1080 - bottom, 1);
    }

    private static WindowFacts Bar(string className, int x, int top, int width, int height,
        string program = "explorer.exe")
    {
        return new WindowFacts(className, x, top, x + width, top + height, program);
    }

    [Fact]
    public void An_edge_the_taskbar_or_winshell_reserves_is_fine()
    {
        Assert.Equal(CheckStatus.Pass, AppBars([Screen(bottom: 48)], [Bar(TaskbarClass, 0, 1032, 1920, 48)]).Status);
        Assert.Equal(CheckStatus.Pass,
            AppBars([Screen(bottom: 48)], [Bar(WinshellClass, 0, 1032, 1920, 48, "winshell.exe")]).Status);
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
            [Screen(bottom: 48), Screen("DISPLAY2", 1920, 40)],
            [
                Bar(TaskbarClass, 0, 1032, 1920, 48), Bar("YasbBar", 1920, 0, 1920, 40, "yasb.exe"),
                Bar("Chrome_WidgetWin_1", 1920, 0, 1920, 1080, "chrome.exe")
            ]);
        Assert.Equal("yasb.exe reserves 40 pixels at the top of DISPLAY2.", named.Message);
    }

    [Fact]
    public void A_winshell_panel_explains_its_margins_and_a_zone_other_than_its_size()
    {
        // A floating panel: 8 pixels of margin above and below a 40-pixel window reserve 56.
        Assert.Equal(CheckStatus.Pass,
            AppBars([Screen(top: 56)], [Bar(WinshellClass, 0, 8, 1920, 40, "winshell.exe")]).Status);
        // Thin windows along the edge, such as another app's 2-pixel border, are not app bars.
        Assert.Equal(CheckStatus.Pass,
            AppBars([Screen(top: 56)],
            [
                Bar(WinshellClass, 0, 8, 1920, 40, "winshell.exe"), Bar("Border", 0, 0, 1920, 2, "borders.exe"),
                Bar("EdgeUiInputTopWndClass", 15, 0, 1905, 4)
            ]).Status);
        // A panel taller than its exclusive zone.
        Assert.Equal(CheckStatus.Pass,
            AppBars([Screen(top: 40)], [Bar(WinshellClass, 0, 0, 1920, 60, "winshell.exe")]).Status);
        // A Winshell window filling the screen is no panel, and explains nothing.
        Assert.Equal(CheckStatus.Warn,
            AppBars([Screen(top: 40)], [Bar(WinshellClass, 0, 0, 1920, 1080, "winshell.exe")]).Status);
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
            [
                Bar(WinshellClass, 0, 0, 960, 40, "winshell.exe"), Bar(WinshellClass, 960, 0, 960, 40, "winshell.exe"),
                Bar("YasbBar", 0, 40, 1920, 40, "yasb.exe")
            ]);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal("yasb.exe reserves space at the top of DISPLAY1.", check.Message);

        var taskbars = AppBars([Screen(bottom: 96)],
            [Bar(TaskbarClass, 0, 1032, 960, 48), Bar(SecondaryTaskbarClass, 960, 1032, 960, 48)]);
        Assert.Equal("Another app reserves 48 pixels at the bottom of DISPLAY1.", taskbars.Message);
    }

    [Fact]
    public void The_taskbar_and_a_winshell_panel_on_one_edge_are_reported()
    {
        var both = AppBars([Screen(bottom: 96)],
            [Bar(TaskbarClass, 0, 1032, 1920, 48), Bar(WinshellClass, 0, 984, 1920, 48, "winshell.exe")]);
        Assert.Equal(CheckStatus.Pass, AppBars([Screen(bottom: 48)], [Bar(TaskbarClass, 0, 1032, 1920, 48)]).Status);
        Assert.Equal(CheckStatus.Warn, both.Status);
        Assert.Contains("both reserve the bottom of DISPLAY1", both.Message, StringComparison.Ordinal);
    }

    // DPI

    [Fact]
    public void A_scaling_override_fails_and_another_compatibility_mode_warns()
    {
        var screens = new[] { Screen() with { Scale = 1.5 }, Screen("DISPLAY2", 1920) };
        Assert.Equal(
            new DoctorCheck("dpi", "DPI", CheckStatus.Pass, "Per-monitor aware. DISPLAY1 at 150%, DISPLAY2 at 100%"),
            Dpi(true, [], screens));
        Assert.Equal(CheckStatus.Fail, Dpi(true, ["~ DPIUNAWARE"], screens).Status);
        Assert.Equal(CheckStatus.Fail, Dpi(true, ["~ GDIDPISCALING DPIUNAWARE"], screens).Status);
        Assert.Equal(CheckStatus.Fail, Dpi(false, [], screens).Status);
        Assert.Equal(CheckStatus.Warn, Dpi(true, ["WIN8RTM"], screens).Status);
        var admin = Dpi(true, ["~ RUNASADMIN"], screens);
        Assert.Equal(CheckStatus.Warn, admin.Status);
        Assert.Equal(
            "Right-click winshell.exe, choose Properties > Compatibility, and clear Run this program as an administrator.",
            admin.Fix);
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
        Assert.EndsWith(
            "clear Use this setting to fix scaling problems for this program instead of the one in Settings.",
            program.Fix, StringComparison.Ordinal);
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

        bool Exists(string path)
        {
            return path == Here || path == @"C:\Old\winshell.exe";
        }

        Assert.Equal(CheckStatus.Pass, SignIn(null, false, Here, Exists).Status);
        Assert.Equal(new DoctorCheck("signIn", "Sign-in", CheckStatus.Pass, "Winshell starts when you sign in"),
            SignIn(command, false, Here, Exists));
        Assert.Equal(CheckStatus.Warn, SignIn(command, true, Here, Exists).Status);
        Assert.Equal(CheckStatus.Warn,
            SignIn(@"C:\Windows\System32\conhost.exe --headless C:\Old\winshell.exe -c bar", false, Here, Exists)
                .Status);
        var gone = SignIn(@"C:\Windows\System32\conhost.exe --headless C:\Gone\winshell.exe", false, Here, Exists);
        Assert.Equal(CheckStatus.Fail, gone.Status);
        Assert.Equal(@"The sign-in entry starts C:\Gone\winshell.exe, which is not there.", gone.Message);
        Assert.Equal(Here, ExeOf($@"""{Here}"" -p ""C:\my config"""));
        Assert.Null(ExeOf("  "));
    }

    [Fact]
    public void A_sign_in_entry_through_a_link_to_this_winshell_is_this_winshell()
    {
        const string Here = @"C:\Users\a\AppData\Local\Microsoft\WinGet\Packages\Winshell\winshell.exe";
        const string Link = @"C:\Users\a\AppData\Local\Microsoft\WinGet\Links\winshell.exe";

        string Resolve(string path)
        {
            return path == Link ? Here : path;
        }

        Assert.Equal(CheckStatus.Pass,
            SignIn($@"conhost.exe --headless ""{Link}"" --no-duplicate", false, Here, _ => true, Resolve).Status);
        Assert.Equal(CheckStatus.Warn,
            SignIn($@"conhost.exe --headless ""{Link}"" --no-duplicate", false, Here, _ => true).Status);

        var target = Path.Combine(_dir, "target.exe");
        File.WriteAllText(target, "");
        Assert.Equal(target, DoctorProbe.ResolveLinks(target));
    }

    [Fact]
    public void A_packaged_sign_in_is_the_startup_task_not_a_path()
    {
        Assert.Equal(CheckStatus.Pass, SignIn(StartupTaskState.Disabled).Status);
        Assert.StartsWith("Winshell does not start when you sign in.", SignIn(StartupTaskState.Disabled).Message,
            StringComparison.Ordinal);
        Assert.Equal(new DoctorCheck("signIn", "Sign-in", CheckStatus.Pass, "Winshell starts when you sign in"),
            SignIn(StartupTaskState.Enabled));
        Assert.Equal(CheckStatus.Pass, SignIn(StartupTaskState.EnabledByPolicy).Status);

        var user = SignIn(StartupTaskState.DisabledByUser);
        Assert.Equal(CheckStatus.Warn, user.Status);
        Assert.Equal("Winshell is set to start when you sign in, but Startup apps in Windows turned it off.",
            user.Message);

        var policy = SignIn(StartupTaskState.DisabledByPolicy);
        Assert.Equal(CheckStatus.Warn, policy.Status);
        Assert.Contains("policy", policy.Message, StringComparison.OrdinalIgnoreCase);

        var missing = SignIn((StartupTaskState?)null);
        Assert.Equal(CheckStatus.Warn, missing.Status);
        Assert.Contains("no startup task", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_sign_in_entry_for_the_console_exe_is_the_windowed_copy_beside_it()
    {
        const string windowed = @"C:\Program Files\Winshell\winshellw.exe";
        const string console = @"C:\Program Files\Winshell\winshell.exe";
        var command = $@"C:\Windows\System32\conhost.exe --headless ""{console}"" --no-duplicate";
        Assert.Equal(CheckStatus.Pass, SignIn(command, false, windowed, path => path == console).Status);
    }

    [Fact]
    public void A_configuration_program_judges_sign_in_by_the_winshell_that_started_it()
    {
        const string host = @"C:\Program Files\Winshell\winshell.exe";
        const string shell = @"C:\Users\a\work\bin\shell\work.exe";
        var command = $@"C:\Windows\System32\conhost.exe --headless ""{host}"" --no-build --no-duplicate";
        Assert.Equal(CheckStatus.Pass,
            SignInForProcess(shell, host, command, false, _ => true).Status);
        Assert.Equal(CheckStatus.Warn,
            SignInForProcess(shell, @"C:\Other\winshell.exe", command, false, _ => true).Status);
    }

    // Notifications, instances, crashes

    [Theory]
    [InlineData("Allowed", CheckStatus.Pass)]
    [InlineData("Denied", CheckStatus.Warn)]
    [InlineData("Unspecified", CheckStatus.Warn)]
    [InlineData("Unavailable", CheckStatus.Warn)]
    public void Notification_access_is_read_without_asking(string access, CheckStatus status)
    {
        Assert.Equal(status, Doctor.Notifications(access).Status);
    }

    [Fact]
    public void Running_instances_and_recent_crash_reports_are_listed()
    {
        Assert.Equal("No Winshell is running", Instances([]).Message);
        Assert.Equal(@"abc (process 42) runs C:\c\MyShell.exe, log C:\l\log.txt",
            Instances([new DoctorInstance("abc", 42, @"C:\c\MyShell.exe", @"C:\l\log.txt")]).Message);

        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        static CrashRecord Report(string path, DateTime written)
        {
            return new CrashRecord(path, new DateTimeOffset(written.Ticks, TimeSpan.Zero), false, "");
        }

        Assert.Equal(CheckStatus.Pass, Crashes([Report(@"C:\r\old.txt", now.AddDays(-8))], now, null).Status);
        var recent =
            Crashes(
            [
                Report(@"C:\r\b.txt", now.AddHours(-1)), Report(@"C:\r\a.txt", now.AddDays(-2)),
                Report(@"C:\r\old.txt", now.AddDays(-8))
            ], now, null);
        Assert.Equal(CheckStatus.Warn, recent.Status);
        Assert.Equal(@"2 crash reports in the last 7 days. The newest is C:\r\b.txt.", recent.Message);
    }

    [Fact]
    public void The_user_folders_are_written_as_their_variables()
    {
        (string, string)[] folders =
        [
            (@"C:\Users\Ann", "%USERPROFILE%"), (@"C:\Users\Ann\AppData\Local", "%LOCALAPPDATA%"),
            (@"C:\Users\Ann\AppData\Roaming\", "%APPDATA%")
        ];
        DoctorCheck[] checks =
        [
            new("crashes", "Crashes", CheckStatus.Warn,
                @"The newest is C:\Users\Ann\AppData\Local\winshell\crashes\a.txt.",
                @"Open c:\users\ann\appdata\roaming\winshell or C:\Users\Ann."),
            new("x", "X", CheckStatus.Pass, @"C:\Users\Anna\x and C:\Users\Ann.old stay")
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
        Assert.Equal(
            new DoctorCheck("dpi", "DPI", CheckStatus.Warn, "Could not check: InvalidOperationException: no screens",
                $"Report it at {BugReports}."), check);
    }

    // Output

    private static readonly DoctorCheck[] Sample =
    [
        new("windows", "Windows", CheckStatus.Pass, "Windows 11 26H2, build 26300.1"),
        new("signIn", "Sign-in", CheckStatus.Warn, "Startup apps turned it off.",
            "Turn Winshell on in Settings > Apps > Startup."),
        new("config", "Config", CheckStatus.Fail, "App.xaml[1:1]: x", "Correct that line.")
    ];

    [Fact]
    public void Text_has_a_line_a_check_a_fix_under_each_problem_and_a_count()
    {
        var text = Format(Sample, "1.2.3", false);

        Assert.Equal(
        [
            "Winshell 1.2.3: this PC's setup",
            "",
            "  PASS  Windows  Windows 11 26H2, build 26300.1",
            "  WARN  Sign-in  Startup apps turned it off.",
            "                 Fix: Turn Winshell on in Settings > Apps > Startup.",
            "  FAIL  Config   App.xaml[1:1]: x",
            "                 Fix: Correct that line.",
            "",
            "1 passed, 1 warning, 1 failed."
        ], text.TrimEnd().Split(Environment.NewLine));
        Assert.Contains("\e[31mFAIL\e[0m", Format(Sample, "1.2.3", true), StringComparison.Ordinal);
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
    public void A_path_with_no_project_is_the_folder_the_doctor_tries_to_write()
    {
        var parent = Path.Combine(_dir, "parent");
        var asked = Path.Combine(parent, "asked");
        Directory.CreateDirectory(asked);
        File.WriteAllText(Path.Combine(parent, "Other.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles,
            AccessControlType.Deny);
        var folder = new DirectoryInfo(asked);
        var acl = folder.GetAccessControl();
        acl.AddAccessRule(deny);
        folder.SetAccessControl(acl);
        try
        {
            var checks = Run(new DoctorContext(new ShellLocations(parent, Path.Combine(_dir, "local")), asked, null));
            var folders = checks.Single(c => c.Id == "folders");
            Assert.Equal(CheckStatus.Fail, folders.Status);
            Assert.Contains(@"\asked", folders.Message, StringComparison.Ordinal);
            var project = checks.Single(c => c.Id == "project");
            Assert.Equal(CheckStatus.Fail, project.Status);
            Assert.DoesNotContain("Other.csproj", project.Message, StringComparison.Ordinal);
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            folder.SetAccessControl(acl);
        }
    }
}
