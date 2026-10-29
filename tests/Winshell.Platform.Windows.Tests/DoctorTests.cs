using System.Text.Json;
using Winshell.Platform.Windows.Diagnostics;
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

        Assert.Equal(new DoctorCheck("config", "Config", CheckStatus.Pass, @"C:\c\shell.qml: 3 QML files, each reads"), Config(@"C:\c\shell.qml", null, new ConfigScan(3, []), null));
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
        var error = Assert.Single(scan.Errors);
        Assert.StartsWith(Path.Combine(_dir, "bars", "Bar.qml") + "[", error, StringComparison.Ordinal);
        Assert.Equal(new ConfigScan(0, []), DoctorProbe.ParseConfig(null));
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
        Assert.Equal(CheckStatus.Pass, Dpi(true, ["~ RUNASADMIN"], screens).Status);
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

    // Notifications, instances, crashes

    [Theory]
    [InlineData("Allowed", CheckStatus.Pass)]
    [InlineData("Denied", CheckStatus.Warn)]
    [InlineData("Unspecified", CheckStatus.Warn)]
    [InlineData("Unavailable", CheckStatus.Warn)]
    public void Notification_access_is_read_without_asking(string access, CheckStatus status) =>
        Assert.Equal(status, Notifications(access).Status);

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
    }
}
