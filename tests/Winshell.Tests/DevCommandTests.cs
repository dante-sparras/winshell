using System.Diagnostics;
using System.Text;

namespace Winshell.Tests;

/// <summary>The real <c>winshell dev</c>, on a real project, editing its source.</summary>
[Trait("Category", "Slow")]
public sealed class DevCommandTests : IDisposable
{
    private static readonly TimeSpan BuildPatience = TimeSpan.FromSeconds(150);

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
                public override void Start(Shell shell) => shell.Ipc.Handle("probe", "value", Marker.Value);
            }

            internal static class Program
            {
                [STAThread]
                private static void Main() => Shell.Run<Bare>(new ShellOptions { ShellId = "dev-probe" });
            }
            """);
        Mark("""=> "one";""");
    }

    public void Dispose()
    {
        if (_dev is not null)
        {
            _dev.Kill(entireProcessTree: true);
            _dev.WaitForExit();
            _dev.Dispose();
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

    private (int Code, string Text) Value()
    {
        var (code, output, _) = WinshellCli.Run(["ipc", "call", "probe", "value"], Folders);
        return (code, output.Trim());
    }

    private int ShellProcess()
    {
        var (_, output, _) = WinshellCli.Run(["list", "-j"], Folders);
        using var listed = System.Text.Json.JsonDocument.Parse(output);
        return listed.RootElement.EnumerateArray().Single().GetProperty("pid").GetInt32();
    }

    private void StartDev()
    {
        var info = WinshellCli.StartInfo(["dev", "-p", Project]);
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

    [Fact]
    public void An_edit_reaches_the_running_shell_and_a_broken_one_leaves_it_running()
    {
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
    }
}
