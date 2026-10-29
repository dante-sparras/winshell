using Microsoft.Win32;
using Winshell.Platform.Windows.Services.Session;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// <c>Startup</c> registers Winshell to run at sign-in. These tests use a fake, or a registry key of their own under
/// <c>HKCU\Software\Winshell\Tests</c> that they delete, and never touch the user's Run key.
/// </summary>
public sealed class StartupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winshell-startup-tests", Guid.NewGuid().ToString("N"));
    private readonly string _testKey = @"Software\Winshell\Tests\" + Guid.NewGuid().ToString("N");
    private readonly List<string> _warnings = [];

    public StartupTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        Registry.CurrentUser.DeleteSubKeyTree(_testKey, throwOnMissingSubKey: false);
    }

    private const string Qml = """
        import QtQuick
        import Winshell
        Item {
          property bool available: Startup.available
          property bool runs: Startup.runAtSignIn
          property bool turnedOff: Startup.turnedOffInWindows
          property bool want: false
          property int refreshes: 0
          onWantChanged: Startup.runAtSignIn = want
          onRefreshesChanged: Startup.refresh()
        }
        """;

    [Fact]
    public void Nothing_is_registered_until_the_shell_turns_it_on_and_off_removes_it()
    {
        var windows = new FakeStartupRegistry();
        var root = Load(windows);

        Assert.True((bool)root.ReadProperty("available")!);
        Assert.False((bool)root.ReadProperty("runs")!);
        Assert.Equal(0, windows.Writes);

        root.WriteProperty("want", true);
        Assert.True(windows.Registered);
        Assert.True((bool)root.ReadProperty("runs")!);

        root.WriteProperty("want", false);
        Assert.False(windows.Registered);
        Assert.False((bool)root.ReadProperty("runs")!);
        Assert.Equal(2, windows.Writes);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void It_reports_when_the_Startup_apps_page_turned_it_off_and_leaves_that_alone()
    {
        var windows = new FakeStartupRegistry { Registered = true, TurnedOff = true };
        var root = Load(windows);

        Assert.True((bool)root.ReadProperty("runs")!);
        Assert.True((bool)root.ReadProperty("turnedOff")!);

        // The user turns it on in Windows; the shell sees it after a refresh, without writing anything.
        windows.TurnedOff = false;
        root.WriteProperty("refreshes", 1);
        Assert.False((bool)root.ReadProperty("turnedOff")!);
        Assert.Equal(0, windows.Writes);
    }

    [Fact]
    public void A_refused_write_keeps_the_state_Windows_has_and_warns()
    {
        var windows = new FakeStartupRegistry { Accepts = false };
        var root = Load(windows);

        root.WriteProperty("want", true);
        Assert.False((bool)root.ReadProperty("runs")!);
        Assert.Contains("startup: Windows did not let Winshell register to run at sign-in.", _warnings);
    }

    [Fact]
    public void Without_winshell_exe_it_is_unavailable_and_registers_nothing()
    {
        var root = Load(null);

        Assert.False((bool)root.ReadProperty("available")!);
        root.WriteProperty("want", true);
        Assert.False((bool)root.ReadProperty("runs")!);
        Assert.Single(_warnings);
    }

    [Fact]
    public void The_entry_is_a_string_under_Run_and_the_Startup_apps_bytes_say_whether_it_is_off()
    {
        var run = _testKey + @"\Run";
        var approved = _testKey + @"\StartupApproved";
        var registry = new WindowsStartupRegistry("\"C:\\winshell.exe\" --no-duplicate", run, approved);

        Assert.False(registry.IsRegistered());
        Assert.True(registry.Register());
        using (var key = Registry.CurrentUser.OpenSubKey(run)!)
            Assert.Equal("\"C:\\winshell.exe\" --no-duplicate", key.GetValue(WindowsStartupRegistry.ValueName));
        Assert.True(registry.IsRegistered());
        Assert.False(registry.TurnedOffInWindows());

        using (var key = Registry.CurrentUser.CreateSubKey(approved))
            key.SetValue(WindowsStartupRegistry.ValueName, new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, RegistryValueKind.Binary);
        Assert.True(registry.TurnedOffInWindows());
        using (var key = Registry.CurrentUser.CreateSubKey(approved))
            key.SetValue(WindowsStartupRegistry.ValueName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        Assert.False(registry.TurnedOffInWindows());

        Assert.True(registry.Unregister());
        Assert.False(registry.IsRegistered());
        Assert.True(registry.Unregister());
    }

    [Fact]
    public void The_command_runs_this_winshell_without_a_console_window_and_never_twice()
    {
        var conhost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");
        var relative = Path.Combine("configs", "work");

        Assert.Equal(
            $"{conhost} --headless \"C:\\Program Files\\Winshell\\winshell.exe\" -c work --no-duplicate",
            WindowsStartupRegistry.CommandFor(@"C:\Program Files\Winshell\winshell.exe", ["-d", "-c", "work"]));
        Assert.Equal(
            $"{conhost} --headless C:\\winshell.exe -n -p {WindowsStartupRegistry.Quote(Path.GetFullPath(relative))}",
            WindowsStartupRegistry.CommandFor(@"C:\winshell.exe", ["-n", "-p", relative]));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\My Shell\", "\"C:\\My Shell\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void Arguments_are_quoted_the_way_Windows_splits_a_command_line(string arg, string quoted) =>
        Assert.Equal(quoted, WindowsStartupRegistry.Quote(arg));

    private QObject Load(IStartupRegistry? windows)
    {
        var path = Path.Combine(_directory, "shell.qml");
        File.WriteAllText(path, Qml);
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        ServiceModules.Register(registry);
        var log = new Log { Sink = (level, category, message) => { if (level >= LogLevel.Warn) _warnings.Add($"{category}: {message}"); } };
        var loop = new ManualEventLoop();
        var engine = new QmlEngine(registry, loop, log, _directory);
        if (windows is not null)
            engine.AddService(windows);
        var root = engine.CreateRoot(path);
        loop.Process();
        return root;
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public bool Registered { get; set; }

        public bool TurnedOff { get; set; }

        public bool Accepts { get; set; } = true;

        public int Writes { get; private set; }

        public bool IsRegistered() => Registered;

        public bool TurnedOffInWindows() => TurnedOff;

        public bool Register()
        {
            Writes++;
            if (Accepts)
                Registered = true;
            return Accepts;
        }

        public bool Unregister()
        {
            Writes++;
            if (Accepts)
                Registered = false;
            return Accepts;
        }
    }
}
