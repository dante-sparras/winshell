using Microsoft.Win32;
using Winshell.Session;
using StartupTaskState = global::Windows.ApplicationModel.StartupTaskState;

namespace Winshell.Services.Tests;

/// <summary>
/// The sign-in entry is a registry value, or the package's startup task. These tests use a key of their own under
/// <c>HKCU\Software\Winshell\Tests</c> that they delete, and a fake task. They never touch the user's Run key.
/// </summary>
public sealed class StartupRegistryTests : IDisposable
{
    private readonly string _testKey = @"Software\Winshell\Tests\" + Guid.NewGuid().ToString("N");
    private readonly List<string> _warnings = [];
    private readonly Log _log;

    public StartupRegistryTests() =>
        _log = new Log { Sink = (level, category, message) => { if (level >= LogLevel.Warn) _warnings.Add($"{category}: {message}"); } };

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_testKey, throwOnMissingSubKey: false);
        foreach (var parent in new[] { @"Software\Winshell\Tests", @"Software\Winshell" })
        {
            using var key = Registry.CurrentUser.OpenSubKey(parent);
            if (key is { SubKeyCount: 0, ValueCount: 0 })
            {
                key.Close();
                Registry.CurrentUser.DeleteSubKey(parent, throwOnMissingSubKey: false);
            }
        }
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
            Assert.Equal("\"C:\\winshell.exe\" --no-duplicate", key.GetValue(SignInEntry.Name));
        Assert.True(registry.IsRegistered());
        Assert.False(registry.TurnedOffInWindows());

        using (var key = Registry.CurrentUser.CreateSubKey(approved))
            key.SetValue(SignInEntry.Name, new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, RegistryValueKind.Binary);
        Assert.True(registry.TurnedOffInWindows());
        using (var key = Registry.CurrentUser.CreateSubKey(approved))
            key.SetValue(SignInEntry.Name, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        Assert.False(registry.TurnedOffInWindows());

        Assert.True(registry.Unregister());
        Assert.False(registry.IsRegistered());
        Assert.True(registry.Unregister());
    }

    [Fact]
    public void An_entry_another_winshell_wrote_points_elsewhere_until_this_one_registers()
    {
        var run = _testKey + @"\Run";
        var registry = new WindowsStartupRegistry("\"C:\\new\\winshell.exe\" --no-duplicate", run, _testKey + @"\StartupApproved");
        Assert.False(registry.PointsElsewhere());
        using (var key = Registry.CurrentUser.CreateSubKey(run))
            key.SetValue(SignInEntry.Name, "\"C:\\old\\winshell.exe\" --no-duplicate", RegistryValueKind.String);
        Assert.True(registry.IsRegistered());
        Assert.True(registry.PointsElsewhere());
        Assert.True(registry.Register());
        Assert.False(registry.PointsElsewhere());
    }

    [Fact]
    public void The_command_runs_this_winshell_without_a_console_window_and_never_twice()
    {
        var conhost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");
        var relative = Path.Combine("configs", "work");

        Assert.Equal(
            $"{conhost} --headless \"C:\\Program Files\\Winshell\\winshell.exe\" -c work --no-build --no-duplicate",
            WindowsStartupRegistry.CommandFor(@"C:\Program Files\Winshell\winshell.exe", ["-d", "-c", "work"]));
        Assert.Equal(
            $"{conhost} --headless C:\\winshell.exe --no-build -n -p {WindowsStartupRegistry.Quote(Path.GetFullPath(relative))}",
            WindowsStartupRegistry.CommandFor(@"C:\winshell.exe", ["-n", "-p", relative]));
    }

    [Fact]
    public void The_windowed_copy_is_the_same_sign_in_entry_as_the_console_exe_beside_it()
    {
        var conhost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");
        const string folder = @"C:\Program Files\Winshell";
        var console = WindowsStartupRegistry.CommandFor(folder + @"\winshell.exe", ["--no-duplicate"]);
        Assert.Equal(console, WindowsStartupRegistry.CommandFor(folder + @"\winshellw.exe", ["--no-duplicate"]));
        Assert.Equal($@"{conhost} --headless ""{folder}\winshell.exe"" --no-duplicate", console);

        var run = _testKey + @"\Run";
        var registry = new WindowsStartupRegistry(console, run, _testKey + @"\StartupApproved");
        using (var key = Registry.CurrentUser.CreateSubKey(run))
            key.SetValue(SignInEntry.Name, $@"{conhost} --headless ""{folder}\winshellw.exe"" --no-duplicate", RegistryValueKind.String);
        Assert.True(registry.IsRegistered());
        Assert.False(registry.PointsElsewhere());

        using (var key = Registry.CurrentUser.CreateSubKey(run))
            key.SetValue(SignInEntry.Name, $@"{conhost} --headless ""C:\Old\winshellw.exe"" --no-duplicate", RegistryValueKind.String);
        Assert.True(registry.PointsElsewhere());
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\My Shell\", "\"C:\\My Shell\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void Arguments_are_quoted_the_way_Windows_splits_a_command_line(string arg, string quoted) =>
        Assert.Equal(quoted, WindowsStartupRegistry.Quote(arg));

    [Fact]
    public void A_startup_task_turns_on_and_off_without_a_path()
    {
        var task = new FakeStartupTask();
        var startup = new StartupService(_log, new StartupTaskRegistry(task));

        Assert.False(startup.RunAtSignIn);
        Assert.False(startup.RegisteredElsewhere);

        startup.RunAtSignIn = true;
        Assert.True(startup.RunAtSignIn);
        Assert.Equal(StartupTaskState.Enabled, task.State);
        Assert.Equal(1, task.Enables);
        Assert.Empty(_warnings);

        startup.RunAtSignIn = false;
        Assert.False(startup.RunAtSignIn);
        Assert.Equal(StartupTaskState.Disabled, task.State);
        Assert.Equal(1, task.Disables);
    }

    [Fact]
    public void A_startup_task_Windows_turned_off_stays_off_until_Windows_turns_it_on()
    {
        var task = new FakeStartupTask { State = StartupTaskState.DisabledByUser };
        var registry = new StartupTaskRegistry(task);
        var startup = new StartupService(_log, registry);

        Assert.True(startup.RunAtSignIn);
        Assert.True(startup.TurnedOffInWindows);
        Assert.False(startup.RegisteredElsewhere);

        Assert.False(registry.Register());
        Assert.False(registry.Unregister());
        Assert.Equal(0, task.Enables);
        Assert.Equal(0, task.Disables);
        Assert.Equal(StartupTaskState.DisabledByUser, task.State);
    }

    [Fact]
    public void A_startup_task_Windows_policy_owns_is_left_alone()
    {
        var forced = new FakeStartupTask { State = StartupTaskState.EnabledByPolicy };
        var on = new StartupTaskRegistry(forced);
        Assert.True(on.IsRegistered());
        Assert.False(on.TurnedOffInWindows());
        Assert.False(on.Unregister());
        Assert.Equal(0, forced.Disables);

        var blocked = new FakeStartupTask { State = StartupTaskState.DisabledByPolicy };
        var off = new StartupTaskRegistry(blocked);
        Assert.True(off.IsRegistered());
        Assert.True(off.TurnedOffInWindows());
        Assert.False(off.Register());
        Assert.Equal(0, blocked.Enables);
    }

    [Fact]
    public void A_startup_task_that_stays_disabled_warns_and_stays_off()
    {
        var task = new FakeStartupTask { EnableResult = StartupTaskState.Disabled };
        var startup = new StartupService(_log, new StartupTaskRegistry(task));

        startup.RunAtSignIn = true;

        Assert.False(startup.RunAtSignIn);
        Assert.Equal(StartupTaskState.Disabled, task.State);
        Assert.Equal(["startup: Windows did not let Winshell register to run at sign-in."], _warnings);
    }

    /// <summary>A startup task that records calls. It is not <c>Windows.ApplicationModel.StartupTask</c>.</summary>
    private sealed class FakeStartupTask : IStartupTask
    {
        public StartupTaskState State { get; set; } = StartupTaskState.Disabled;

        public StartupTaskState EnableResult { get; set; } = StartupTaskState.Enabled;

        public int Enables { get; private set; }

        public int Disables { get; private set; }

        public StartupTaskState RequestEnable()
        {
            Enables++;
            State = EnableResult;
            return EnableResult;
        }

        public bool Disable()
        {
            Disables++;
            State = StartupTaskState.Disabled;
            return true;
        }
    }
}
