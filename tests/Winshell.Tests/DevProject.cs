using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// A real configuration project in a folder of its own, and the <c>winshell dev</c> a test starts on it. It references the
/// <c>Winshell.Shell</c> the solution build made, so tests of two classes can each run one at the same time.
/// </summary>
internal sealed class DevProject : IDisposable
{
    internal static readonly TimeSpan BuildPatience = TimeSpan.FromSeconds(140);

    private readonly StringBuilder _printed = new();

    public DevProject()
    {
        Directory.CreateDirectory(Project);
        Directory.CreateDirectory(Path.Combine(Folder, "config"));
        Directory.CreateDirectory(Path.Combine(Folder, "local"));
        File.Copy(Path.Combine(Repo.Root, "global.json"), Path.Combine(Project, "global.json"));
        // Without BuildProjectReferences, each build builds Winshell.Shell again, which rewrites files in its obj folder
        // even when nothing changed, and two of these projects building at once would both write them.
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
                <BuildProjectReferences>false</BuildProjectReferences>
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

    internal string Folder { get; } = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    internal string Project => Path.Combine(Folder, "project");

    internal (string, string)[] Folders =>
    [
        ("WINSHELL_CONFIG_DIR", Path.Combine(Folder, "config")),
        ("WINSHELL_LOCAL_DIR", Path.Combine(Folder, "local")),
    ];

    /// <summary>The <c>winshell dev</c> process, once <see cref="StartDev"/> has started it.</summary>
    internal Process? Dev { get; private set; }

    public void Dispose()
    {
        if (Dev is not null)
        {
            Dev.Kill(entireProcessTree: true);
            Dev.WaitForExit();
            Dev.Dispose();
            lock (_printed)
                TestContext.Current.TestOutputHelper?.WriteLine(_printed.ToString());
        }
        Wait.Until(Removed, "the test folder to be free");
    }

    private bool Removed()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal void Mark(string body) =>
        File.WriteAllText(Path.Combine(Project, "Marker.cs"), $"internal static class Marker {{ public static string Value() {body} }}");

    /// <param name="needsBuild">Marks the label x:Load, which only a build applies.</param>
    internal void Label(string text, bool needsBuild = false) => File.WriteAllText(Path.Combine(Project, "PanelView.xaml"), $"""
        <UserControl
            x:Class="Dev.PanelView"
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <TextBlock x:Name="Label" {(needsBuild ? "x:Load=\"True\" " : "")}Text="{text}" />
        </UserControl>
        """);

    internal string Shown() => WinshellCli.Run(["ipc", "call", "probe", "label"], Folders).Output.Trim();

    internal (int Code, string Text) Value()
    {
        var (code, output, _) = WinshellCli.Run(["ipc", "call", "probe", "value"], Folders);
        return (code, output.Trim());
    }

    internal string InstanceId()
    {
        var (_, output, _) = WinshellCli.Run(["list", "-j"], Folders);
        using var listed = JsonDocument.Parse(output);
        return listed.RootElement.EnumerateArray().Single().GetProperty("id").GetString() ?? "";
    }

    internal int ShellProcess()
    {
        var (_, output, _) = WinshellCli.Run(["list", "-j"], Folders);
        using var listed = System.Text.Json.JsonDocument.Parse(output);
        return listed.RootElement.EnumerateArray().Single().GetProperty("pid").GetInt32();
    }

    /// <param name="ownGroup">
    /// In a process group of its own, on this test's console, so a Ctrl+Break can be sent to it alone.
    /// </param>
    internal void StartDev(bool ownGroup = false)
    {
        var info = WinshellCli.StartInfo(["dev", "-p", Project]);
        info.CreateNoWindow = !ownGroup;
        info.CreateNewProcessGroup = ownGroup;
        foreach (var (name, value) in Folders)
            info.Environment[name] = value;
        Dev = Process.Start(info)!;
        foreach (var stream in new[] { Dev.StandardOutput, Dev.StandardError })
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
    internal List<string> FromProject()
    {
        using var search = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine, ExecutablePath FROM Win32_Process");
        using var found = search.Get();
        return found.Cast<ManagementObject>()
            .Where(p => $"{p["CommandLine"]} {p["ExecutablePath"]}".Contains(Project, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p["Name"]} {p["ProcessId"]}")
            .ToList();
    }

    internal bool Printed(string text)
    {
        lock (_printed)
            return _printed.ToString().Contains(text, StringComparison.Ordinal);
    }
}
