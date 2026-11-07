using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using Winshell.Diagnostics;
using Winshell.Session;
using StartupTaskState = Windows.ApplicationModel.StartupTaskState;
using static Winshell.Diagnostics.DoctorSignIn;

namespace Winshell.Services.Tests;

/// <summary>
/// The sign-in entry is a registry value, or the package's startup task. These tests use a key of their own under
/// <c>HKCU\Software\Winshell\Tests</c> that they delete, and a fake task. They never touch the user's Run key. A
/// configuration is a project in a config folder of their own.
/// </summary>
public sealed class StartupRegistryTests : IDisposable
{
    private readonly TempFolder _configRoot = new();
    private readonly string _testKey = @"Software\Winshell\Tests\" + Guid.NewGuid().ToString("N");
    private readonly List<string> _warnings = [];
    private readonly Log _log;

    public StartupRegistryTests()
    {
        _log = new Log
        {
            Sink = (level, category, message) =>
            {
                if (level >= LogLevel.Warn) _warnings.Add($"{category}: {message}");
            }
        };
    }

    public void Dispose()
    {
        _configRoot.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testKey, false);
        foreach (var parent in new[] { @"Software\Winshell\Tests", @"Software\Winshell" })
        {
            using var key = Registry.CurrentUser.OpenSubKey(parent);
            if (key is { SubKeyCount: 0, ValueCount: 0 })
            {
                key.Close();
                Registry.CurrentUser.DeleteSubKey(parent, false);
            }
        }
    }

    [Fact]
    public void The_entry_is_a_string_under_Run_and_the_Startup_apps_bytes_say_whether_it_is_off()
    {
        var run = _testKey + @"\Run";
        var approved = _testKey + @"\StartupApproved";
        var registry =
            new WindowsStartupRegistry("\"C:\\winshell.exe\" --no-duplicate", _configRoot.Path, run, approved);

        Assert.False(registry.IsRegistered());
        Assert.True(registry.Register());
        using (var key = Registry.CurrentUser.OpenSubKey(run)!)
        {
            Assert.Equal("\"C:\\winshell.exe\" --no-duplicate", key.GetValue(SignInEntry.Name));
        }

        Assert.True(registry.IsRegistered());
        Assert.False(registry.TurnedOffInWindows());

        using (var key = Registry.CurrentUser.CreateSubKey(approved))
        {
            key.SetValue(SignInEntry.Name, new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, RegistryValueKind.Binary);
        }

        Assert.True(registry.TurnedOffInWindows());
        using (var key = Registry.CurrentUser.CreateSubKey(approved))
        {
            key.SetValue(SignInEntry.Name, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        Assert.False(registry.TurnedOffInWindows());

        Assert.True(registry.Unregister());
        Assert.False(registry.IsRegistered());
        Assert.True(registry.Unregister());
    }

    [Fact]
    public void An_entry_another_winshell_wrote_points_elsewhere_until_this_one_registers()
    {
        var run = _testKey + @"\Run";
        var registry = new WindowsStartupRegistry("\"C:\\new\\winshell.exe\" --no-duplicate", _configRoot.Path, run,
            _testKey + @"\StartupApproved");
        Assert.False(registry.PointsElsewhere());
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, "\"C:\\old\\winshell.exe\" --no-duplicate", RegistryValueKind.String);
        }

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
            SignInEntry.CommandFor(@"C:\Program Files\Winshell\winshell.exe", ["-d", "-c", "work"]));
        Assert.Equal(
            $"{conhost} --headless C:\\winshell.exe --no-build -n -p {SignInEntry.Quote(Path.GetFullPath(relative))}",
            SignInEntry.CommandFor(@"C:\winshell.exe", ["-n", "-p", relative]));
    }

    [Fact]
    public void The_windowed_copy_is_the_same_sign_in_entry_as_the_console_exe_beside_it()
    {
        var conhost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");
        const string folder = @"C:\Program Files\Winshell";
        var console = SignInEntry.CommandFor(folder + @"\winshell.exe", ["--no-duplicate"]);
        Assert.Equal(console, SignInEntry.CommandFor(folder + @"\winshellw.exe", ["--no-duplicate"]));
        Assert.Equal($@"{conhost} --headless ""{folder}\winshell.exe"" --no-build --no-duplicate", console);

        var run = _testKey + @"\Run";
        var registry = new WindowsStartupRegistry(console, _configRoot.Path, run, _testKey + @"\StartupApproved");
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name,
                $@"{conhost} --headless ""{folder}\winshellw.exe"" --no-build --no-duplicate",
                RegistryValueKind.String);
        }

        Assert.True(registry.IsRegistered());
        Assert.False(registry.PointsElsewhere());

        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, $@"{conhost} --headless ""C:\Old\winshellw.exe"" --no-build --no-duplicate",
                RegistryValueKind.String);
        }

        Assert.True(registry.PointsElsewhere());
    }

    [Fact]
    public void The_sign_in_reader_reads_back_what_the_writer_wrote_and_the_programs_words_stay_last()
    {
        const string windowed = @"C:\Program Files\Winshell\winshellw.exe";
        var relative = Path.Combine("my configs", "-d");

        var byPath = SignInEntry.Read(SignInEntry.CommandFor(windowed, ["-d", "run", "-p", relative, "--", "-c", "x"]));
        var byName = SignInEntry.Read(SignInEntry.CommandFor(windowed, ["run", "--config", "-d"]));

        Assert.Equal(new SignInEntry.SignInLaunch(@"C:\Program Files\Winshell\winshell.exe",
            Path.GetFullPath(relative), null), byPath);
        Assert.Equal(new SignInEntry.SignInLaunch(@"C:\Program Files\Winshell\winshell.exe", null, "-d"), byName);
        Assert.EndsWith("--no-build --no-duplicate -- -c x",
            SignInEntry.CommandFor(windowed, ["run", "-p", relative, "--", "-c", "x"]));
    }

    [Fact]
    public async Task The_installer_writes_the_command_this_install_writes_for_itself()
    {
        const string winshell = @"C:\Program Files\Winshell\winshell.exe";

        var installer = await InstallerSignInCommand(winshell);

        // install.ps1 names conhost from %SystemRoot%, which can differ in case from the system folder .NET reports.
        var conhost = Path.Combine(Environment.SystemDirectory, "conhost.exe");
        var written = SignInEntry.CommandFor(winshell, []);
        Assert.StartsWith(conhost, installer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(written[conhost.Length..], installer[conhost.Length..]);
    }

    /// <summary>
    /// What install.ps1's <c>Get-WinshellSignInCommand</c> returns for <paramref name="exe"/>, run alone in Windows
    /// PowerShell, which install.ps1 also runs on. Running the whole script would install Winshell.
    /// </summary>
    private static async Task<string> InstallerSignInCommand(string exe)
    {
        var script = Path.Combine(Repo.Root, "scripts", "install.ps1");
        var program = $$"""
                        $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script.Replace("'", "''")}}', [ref]$null, [ref]$null)
                        $function = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-WinshellSignInCommand' }, $false)
                        if (-not $function) { throw 'install.ps1 has no Get-WinshellSignInCommand.' }
                        . ([scriptblock]::Create($function.Extent.Text))
                        [Console]::Out.Write((Get-WinshellSignInCommand '{{exe.Replace("'", "''")}}'))
                        """;
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                     { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand" })
            start.ArgumentList.Add(argument);
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(program)));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\My Shell\", "\"C:\\My Shell\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void Arguments_are_quoted_the_way_Windows_splits_a_command_line(string arg, string quoted)
    {
        Assert.Equal(quoted, SignInEntry.Quote(arg));
    }

    [Fact]
    public void The_install_sign_in_entry_for_this_configuration_is_on_and_the_doctor_agrees()
    {
        // install.ps1 -AutoStart writes this command, which names no configuration.
        const string winshell = @"C:\Program Files\Winshell\winshell.exe";
        const string windowed = @"C:\Program Files\Winshell\winshellw.exe";
        const string written = @"C:\Users\a\AppData\Roaming\winshell\work";
        const string otherExe = @"C:\Other\winshell.exe";
        var conhost = Path.Combine(Environment.SystemDirectory, "conhost.exe");
        var install = SignInEntry.CommandFor(winshell, []);
        var command = $"{conhost} --headless \"{winshell}\" run -p {written} --no-build --no-duplicate";
        var program = written + @"\bin\shell\work.exe";

        Assert.Equal(command, SignInEntry.CommandFor(winshell, ["run", "-p", written]));
        Assert.Equal(command, StartupService.SignInCommand(program, winshell));
        Assert.Equal(command, StartupService.SignInCommand(written + @"\bin\shell.next\work.exe", windowed));
        Assert.Null(StartupService.SignInCommand(program, null));
        Assert.Null(StartupService.SignInCommand(program, @"C:\Program Files\dotnet\dotnet.exe"));

        // From here the configurations are on disk, where winshell run looks for them.
        var root = _configRoot.Path;
        var project = Configuration(root, "work");
        // Not a folder of the config folder itself, so a bare winshell run still has one configuration to start.
        var play = Configuration(Path.Combine(root, "other"), "play");
        var supervised = SignInEntry.CommandFor(winshell, ["run", "-p", project]);

        bool Exists(string path)
        {
            return path == winshell || path == otherExe;
        }

        var run = _testKey + @"\Run";
        var approved = _testKey + @"\StartupApproved";
        var registry = new WindowsStartupRegistry(supervised, root, run, approved);
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, install, RegistryValueKind.String);
        }

        var startup = new StartupService(_log, registry);
        Assert.True(startup.RunAtSignIn);
        Assert.False(startup.RegisteredElsewhere);
        Assert.Equal(CheckStatus.Pass,
            SignIn(install, false, winshell, root, Exists, projectFolder: project).Status);

        startup.RunAtSignIn = false;
        using (var key = Registry.CurrentUser.OpenSubKey(run))
        {
            Assert.Null(key?.GetValue(SignInEntry.Name));
        }

        Assert.False(startup.RunAtSignIn);

        startup.RunAtSignIn = true;
        using (var key = Registry.CurrentUser.OpenSubKey(run))
        {
            Assert.Equal(supervised, key?.GetValue(SignInEntry.Name));
        }

        Assert.Equal(CheckStatus.Pass,
            SignIn(supervised, false, winshell, root, Exists, projectFolder: project).Status);

        var playCommand = SignInEntry.CommandFor(winshell, ["run", "-p", play]);
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, playCommand, RegistryValueKind.String);
        }

        startup.Refresh();
        Assert.False(startup.RunAtSignIn);
        Assert.True(startup.RegisteredElsewhere);
        var otherConfiguration =
            SignIn(playCommand, false, winshell, root, Exists, projectFolder: project);
        Assert.Equal(CheckStatus.Warn, otherConfiguration.Status);
        Assert.Equal("The sign-in entry does not start this configuration.", otherConfiguration.Message);

        var playRegistry = new WindowsStartupRegistry(
            SignInEntry.CommandFor(winshell, ["run", "-p", play]), root, run, approved);
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, install, RegistryValueKind.String);
        }

        var playStartup = new StartupService(_log, playRegistry);
        Assert.False(playStartup.RunAtSignIn);
        Assert.Equal(CheckStatus.Warn,
            SignIn(install, false, winshell, root, Exists, projectFolder: play).Status);

        var otherInstall =
            $"{conhost} --headless {otherExe} run -p {SignInEntry.Quote(project)} --no-build --no-duplicate";
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, otherInstall, RegistryValueKind.String);
        }

        startup.Refresh();
        Assert.False(startup.RunAtSignIn);
        Assert.True(startup.RegisteredElsewhere);
        var other = SignIn(otherInstall, false, winshell, root, Exists, projectFolder: project);
        Assert.Equal(CheckStatus.Warn, other.Status);
        Assert.Contains("another winshell.exe", other.Message, StringComparison.Ordinal);

        var byName = SignInEntry.CommandFor(winshell, ["run", "-c", "work"]);
        var named = new WindowsStartupRegistry(
            SignInEntry.CommandFor(winshell, ["run", "-p", project]), root, run, approved);
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, byName, RegistryValueKind.String);
        }

        var namedStartup = new StartupService(_log, named);
        Assert.True(namedStartup.RunAtSignIn);
        Assert.Equal(CheckStatus.Pass,
            SignIn(byName, false, winshell, root, Exists, projectFolder: project).Status);

        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, SignInEntry.CommandFor(winshell, ["run", "-c", "play"]),
                RegistryValueKind.String);
        }

        namedStartup.Refresh();
        Assert.False(namedStartup.RunAtSignIn);
        Assert.Equal(CheckStatus.Warn,
            SignIn(SignInEntry.CommandFor(winshell, ["run", "-c", "play"]), false, winshell, root, Exists,
                projectFolder: project).Status);
    }

    [Fact]
    public void Adding_a_configuration_stops_the_install_entry_counting_as_this_one()
    {
        const string winshell = @"C:\Program Files\Winshell\winshell.exe";
        var project = Configuration(_configRoot.Path, "work");
        var install = SignInEntry.CommandFor(winshell, []);
        var run = _testKey + @"\Run";
        var registry = new WindowsStartupRegistry(
            SignInEntry.CommandFor(winshell, ["run", "-p", project]), _configRoot.Path, run);
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, install, RegistryValueKind.String);
        }

        var startup = new StartupService(_log, registry);
        Assert.True(startup.RunAtSignIn);

        // Now a bare winshell run has two configurations to choose from, so it starts neither.
        Configuration(_configRoot.Path, "play");
        startup.Refresh();
        Assert.False(startup.RunAtSignIn);
        Assert.True(startup.RegisteredElsewhere);
    }

    [Fact]
    public void A_sign_in_entry_whose_program_is_not_a_path_is_not_this_one()
    {
        const string winshell = @"C:\Program Files\Winshell\winshell.exe";
        var project = Configuration(_configRoot.Path, "work");
        var run = _testKey + @"\Run";
        var registry = new WindowsStartupRegistry(
            SignInEntry.CommandFor(winshell, ["run", "-p", project]), _configRoot.Path, run);
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            key.SetValue(SignInEntry.Name, @"conhost.exe --headless ""C:\bad|winshell.exe"" --no-build",
                RegistryValueKind.String);
        }

        var startup = new StartupService(_log, registry);
        Assert.False(startup.RunAtSignIn);
        Assert.True(startup.RegisteredElsewhere);
    }

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

    /// <summary>A configuration named <paramref name="name"/> in <paramref name="folder"/>: a folder with its project.</summary>
    private static string Configuration(string folder, string name)
    {
        var project = Path.Combine(folder, name);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, name + ".csproj"), "<Project />");
        return project;
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
