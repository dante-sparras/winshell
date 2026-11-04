using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>The real <c>winshell dev</c>, on a real project, editing its source.</summary>
[Trait("Category", "Slow")]
public sealed class DevCommandTests : IDisposable
{
    private static readonly TimeSpan BuildPatience = TimeSpan.FromSeconds(140);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly StringBuilder _printed = new();
    private Process? _dev;

    private string Project => Path.Combine(_dir, "project");

    private (string, string)[] Folders =>
    [
        ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")),
        ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local")),
    ];

    public DevCommandTests()
    {
        Directory.CreateDirectory(Project);
        Directory.CreateDirectory(Path.Combine(_dir, "config"));
        Directory.CreateDirectory(Path.Combine(_dir, "local"));
        File.Copy(Path.Combine(Repo.Root, "global.json"), Path.Combine(Project, "global.json"));
        File.WriteAllText(Path.Combine(Project, "Dev.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
                <SupportedOSPlatformVersion>10.0.22621.0</SupportedOSPlatformVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <RuntimeIdentifier>win-x64</RuntimeIdentifier>
                <UseWinUI>true</UseWinUI>
                <WindowsPackageType>None</WindowsPackageType>
                <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
                <SelfContained>true</SelfContained>
                <AssemblyName>Dev</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{Path.Combine(Repo.Root, "src", "Winshell.Shell", "Winshell.Shell.csproj")}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(Project, "Program.cs"), """
            using Winshell;

            internal sealed class Bare : ShellConfiguration
            {
                public override void Start(Shell shell)
                {
                    var view = new Dev.PanelView();
                    _ = new PanelWindow { Anchors = Edges.Top | Edges.Left, Width = 200, Height = 32, Content = view, Visible = true };
                    shell.Ipc.Handle("probe", "value", Marker.Value);
                    shell.Ipc.Handle("probe", "label", view.Shown);
                }
            }

            internal static class Program
            {
                [STAThread]
                private static void Main() => Shell.Run<Bare>(new ShellOptions { ShellId = "dev-probe" });
            }
            """);
        File.WriteAllText(Path.Combine(Project, "PanelView.xaml.cs"), """
            using Microsoft.UI.Xaml.Controls;

            namespace Dev;

            public sealed partial class PanelView : UserControl
            {
                public PanelView() => InitializeComponent();

                public string Shown() => Label.Text;
            }
            """);
        Label("one");
        Mark("""=> "one";""");
    }

    public void Dispose()
    {
        if (_dev is not null)
        {
            _dev.Kill(entireProcessTree: true);
            _dev.WaitForExit();
            _dev.Dispose();
            lock (_printed)
                TestContext.Current.TestOutputHelper?.WriteLine(_printed.ToString());
        }
        Wait.Until(Removed, "the test folder to be free");
    }

    private bool Removed()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Mark(string body) =>
        File.WriteAllText(Path.Combine(Project, "Marker.cs"), $"internal static class Marker {{ public static string Value() {body} }}");

    /// <param name="needsBuild">Marks the label x:Load, which only a build applies.</param>
    private void Label(string text, bool needsBuild = false) => File.WriteAllText(Path.Combine(Project, "PanelView.xaml"), $"""
        <UserControl
            x:Class="Dev.PanelView"
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <TextBlock x:Name="Label" {(needsBuild ? "x:Load=\"True\" " : "")}Text="{text}" />
        </UserControl>
        """);

    private string Shown() => WinshellCli.Run(["ipc", "call", "probe", "label"], Folders).Output.Trim();

    private (int Code, string Text) Value()
    {
        var (code, output, _) = WinshellCli.Run(["ipc", "call", "probe", "value"], Folders);
        return (code, output.Trim());
    }

    private string InstanceId()
    {
        var (_, output, _) = WinshellCli.Run(["list", "-j"], Folders);
        using var listed = JsonDocument.Parse(output);
        return listed.RootElement.EnumerateArray().Single().GetProperty("id").GetString() ?? "";
    }

    private int ShellProcess()
    {
        var (_, output, _) = WinshellCli.Run(["list", "-j"], Folders);
        using var listed = System.Text.Json.JsonDocument.Parse(output);
        return listed.RootElement.EnumerateArray().Single().GetProperty("pid").GetInt32();
    }

    /// <param name="ownGroup">
    /// In a process group of its own, on this test's console, so a Ctrl+Break can be sent to it alone.
    /// </param>
    private void StartDev(bool ownGroup = false)
    {
        var info = WinshellCli.StartInfo(["dev", "-p", Project]);
        info.CreateNoWindow = !ownGroup;
        info.CreateNewProcessGroup = ownGroup;
        foreach (var (name, value) in Folders)
            info.Environment[name] = value;
        _dev = Process.Start(info)!;
        foreach (var stream in new[] { _dev.StandardOutput, _dev.StandardError })
        {
            new Thread(() =>
            {
                while (stream.ReadLine() is { } line)
                {
                    lock (_printed)
                        _printed.AppendLine(line);
                }
            })
            { IsBackground = true }.Start();
        }
    }

    /// <summary>Every process this project started: dotnet watch, the build, and the shell, by command line or path.</summary>
    private List<string> FromProject()
    {
        using var search = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine, ExecutablePath FROM Win32_Process");
        using var found = search.Get();
        return found.Cast<ManagementObject>()
            .Where(p => $"{p["CommandLine"]} {p["ExecutablePath"]}".Contains(Project, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p["Name"]} {p["ProcessId"]}")
            .ToList();
    }

    private bool Printed(string text)
    {
        lock (_printed)
            return _printed.ToString().Contains(text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_downloaded_project_never_reaches_a_build()
    {
        File.WriteAllText(Path.Combine(Project, "Dev.csproj") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        var (code, _, error) = WinshellCli.Run(["dev", "-p", Project], Folders);

        Assert.Equal(1, code);
        Assert.Contains("Dev.csproj", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Project, "obj")), "dotnet ran on a downloaded project.");
    }

    [Fact]
    public void With_no_project_it_says_how_to_make_one()
    {
        var (code, _, error) = WinshellCli.Run(["dev"], Folders);

        Assert.Equal(1, code);
        Assert.Contains("dotnet new winshell", error, StringComparison.Ordinal);
    }

    [Fact(Timeout = 170_000)]
    public void Ctrl_Break_stops_dotnet_watch_and_the_shell()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        Assert.True(GetConsoleWindow() != 0 || GetConsoleProcessList([0], 1) > 0, "The test program has no console to send a Ctrl+Break on.");
        StartDev(ownGroup: true);
        Wait.Until(() => Value() == (0, "one"), "the first build", BuildPatience);
        Assert.Contains(FromProject(), p => p.StartsWith("Dev.exe ", StringComparison.Ordinal));
        var id = InstanceId();

        // Ctrl+C reaches every process on the console, this test's too. Ctrl+Break can be sent to one process group.
        Assert.True(GenerateConsoleCtrlEvent(CtrlBreakEvent, (uint)_dev!.Id), "Ctrl+Break was not sent.");

        Assert.True(_dev.WaitForExit(TimeSpan.FromSeconds(30)), "winshell dev did not exit.");
        Wait.Until(() => FromProject().Count == 0, "dotnet watch and the shell to end", TimeSpan.FromSeconds(30));
        // The shell quit, as winshell kill asks it to, before dotnet watch ended: a shell that is only ended marks no exit.
        var record = File.ReadAllText(Path.Combine(_dir, "local", "run", id, "instance.json"));
        Assert.NotNull(JsonSerializer.Deserialize(record, InstanceJson.Default.InstanceInfo)?.ExitedAt);
    }

    [Fact(Timeout = 170_000)]
    public void The_console_shows_what_dotnet_watch_says_but_not_its_Ctrl_R_hint()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        StartDev();
        Wait.Until(() => Value() == (0, "one"), "the first build", BuildPatience);

        Assert.True(Printed("dotnet watch : "), "winshell dev printed nothing of dotnet watch's.");
        Assert.False(Printed("Ctrl+R") || Printed("Ctrl + R"), "winshell dev printed dotnet watch's Ctrl+R hint.");
    }

    // Two builds: the first start, and the one the XAML needs.
    [Fact(Timeout = 300_000)]
    public void Xaml_that_needs_a_build_lets_the_shell_quit_before_it_starts_again()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        StartDev();
        Wait.Until(() => Value() == (0, "one"), "the first build", BuildPatience);
        var shell = ShellProcess();
        var id = InstanceId();

        Label("two", needsBuild: true);
        Wait.Until(() => Printed("Building, because the XAML you saved needs it."), "the build the XAML needs");
        Wait.Until(() => Shown() == "two", "the shell to start again", BuildPatience);

        Assert.NotEqual(shell, ShellProcess());
        // The shell quit, as winshell kill asks it to: a shell that is only ended marks no exit.
        var record = File.ReadAllText(Path.Combine(_dir, "local", "run", id, "instance.json"));
        Assert.NotNull(JsonSerializer.Deserialize(record, InstanceJson.Default.InstanceInfo)?.ExitedAt);
    }

    [Fact(Timeout = 170_000)]
    public void An_edit_reaches_the_running_shell_and_a_broken_one_leaves_it_running()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        StartDev();
        Wait.Until(() => Value() == (0, "one"), "the first build", BuildPatience);
        var shell = ShellProcess();
        // The first edit also warms the compiler up.
        Mark("""=> "two";""");
        Wait.Until(() => Value().Text == "two", "the first edit", BuildPatience);

        Mark("""=> "broken" """);
        Wait.Until(() => Printed("error CS1002"), "the compile error");
        Assert.Equal((0, "two"), Value());
        Wait.Until(() => WinshellCli.Run(["log", "-t", "20"], Folders).Output.Contains("Your configuration did not build", StringComparison.Ordinal), "the notice");

        Mark("""=> "three";""");
        var applied = Stopwatch.StartNew();
        Wait.Until(() => Value().Text == "three", "the fix");

        Assert.Equal(shell, ShellProcess());
        Assert.True(applied.Elapsed < TimeSpan.FromSeconds(10), $"An edit took {applied.Elapsed}.");

        Label("two");
        var saved = Stopwatch.StartNew();
        Wait.Until(() => Shown() == "two", "the XAML edit");
        Assert.True(saved.Elapsed < TimeSpan.FromSeconds(5), $"A XAML edit took {saved.Elapsed}.");
        Assert.Equal(shell, ShellProcess());
        TestContext.Current.TestOutputHelper?.WriteLine($"An edit took {applied.Elapsed}, and a XAML edit {saved.Elapsed}.");
    }

    private const uint CtrlBreakEvent = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processes, uint count);
}
