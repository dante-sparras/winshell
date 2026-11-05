using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// A real program, shell id <c>probe</c> unless a test names another, built once into <c>bin\shell</c>. Tests of
/// instances start it, so they hold no taskbar, no screen edge and no window unless a test puts
/// <see cref="HideTaskbarMarker"/> or <see cref="WindowMarker"/> in its config folder. Its <c>probe.note</c> function,
/// which takes a string, is there for <c>winshell ipc</c> to list. A class that starts it has the
/// <see cref="ProbeBuild"/> fixture. Nothing changes that build once it is made: a test that edits and rebuilds a probe
/// builds one of its own with <see cref="Build"/>.
/// </summary>
internal static class ProbeShell
{
    private const string ShellIdVariable = "PROBE_SHELL_ID";

    // Every probe builds the src/ projects it references, into their own obj folders, so one build runs at a time.
    private static readonly object Gate = new();

    private static string? _program;

    internal static string Program
    {
        get
        {
            Ensure();
            return _program!;
        }
    }

    /// <summary>
    /// Starts the program, or <paramref name="program"/>, and waits until <c>winshell list</c> shows it. The folders are
    /// the test's own, so it never sees the user's instances.
    /// </summary>
    internal static Process Start(string config, string local, string? shellId = null, string? program = null)
    {
        var process = Launch(config, local, [], shellId, program: program);
        Wait.Until(() => Listed(config, local).Contains(process.Id), $"probe {process.Id} in winshell list");
        return process;
    }

    /// <summary>Starts the program and returns at once. What it prints goes to <paramref name="printed"/>.</summary>
    internal static Process Launch(string config, string local, string[] args, string? shellId = null, StringBuilder? printed = null, string? program = null)
    {
        var info = new ProcessStartInfo(program ?? Program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        info.Environment["WINSHELL_CONFIG_DIR"] = config;
        info.Environment["WINSHELL_LOCAL_DIR"] = local;
        if (shellId is not null)
            info.Environment[ShellIdVariable] = shellId;
        var process = Process.Start(info)!;
        // Read the output so the program never blocks on a full pipe.
        void Print(object sender, DataReceivedEventArgs e)
        {
            if (printed is not null && e.Data is not null)
            {
                lock (printed)
                    printed.AppendLine(e.Data);
            }
        }
        process.ErrorDataReceived += Print;
        process.OutputDataReceived += Print;
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        return process;
    }

    /// <summary>
    /// A <c>-d</c> start, to the end of the process that returns, with its output read to the end as a script would: the
    /// shell it leaves behind must not hold that output open.
    /// </summary>
    internal static ChildProcess Detach(string config, string local, string[] args)
    {
        var info = new ProcessStartInfo(Program);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        info.Environment["WINSHELL_CONFIG_DIR"] = config;
        info.Environment["WINSHELL_LOCAL_DIR"] = local;
        return ChildProcess.Run(info, TimeSpan.FromSeconds(20));
    }

    private static HashSet<int> Listed(string config, string local)
    {
        var result = WinshellCli.Run(["list", "--json"], ("WINSHELL_CONFIG_DIR", config), ("WINSHELL_LOCAL_DIR", local));
        result.AssertExitCode(0);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(row => row.GetProperty("pid").GetInt32()).ToHashSet();
    }

    internal static void Ensure()
    {
        // Not behind the gate once built: a test reads Program, and must not wait out another class's build of its own probe.
        if (Volatile.Read(ref _program) is { } built && File.Exists(built))
            return;
        lock (Gate)
        {
            if (_program is not null && File.Exists(_program))
                return;
            // One folder per checkout. The project names this checkout's source, so two sharing a folder break each other's build.
            var checkout = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Repo.Root)))[..12];
            Volatile.Write(ref _program, Build(Path.Combine(Path.GetTempPath(), "winshell-probe-shell-" + checkout)));
        }
    }

    /// <summary>Builds the probe in <paramref name="folder"/>, with its source in <c>Program.cs</c>, and returns the program.</summary>
    /// <remarks>
    /// Each new folder rebuilds the src/ projects into their shared obj folders, which <see cref="Gate"/> cannot keep
    /// apart from a build <c>winshell reload</c> runs in another process. A test that wants the probe to behave
    /// differently puts a file in its config folder for <see cref="Source"/> to read, as <see cref="HangMarker"/> does,
    /// and runs the shared program.
    /// </remarks>
    internal static string Build(string folder)
    {
        lock (Gate)
            return BuildLocked(folder);
    }

    private static string BuildLocked(string folder)
    {
        var root = Repo.Root;
        Directory.CreateDirectory(folder);
        // The probe sits outside the repo. Without this, dotnet picks a newer SDK and rebuilds the shell from scratch.
        WriteIfDifferent(Path.Combine(folder, "global.json"), File.ReadAllText(Path.Combine(root, "global.json")));
        var project = Path.Combine(folder, "Probe.csproj");
        WriteIfDifferent(project, Project(Path.Combine(root, "src", "Winshell.Shell", "Winshell.Shell.csproj")));
        WriteIfDifferent(Path.Combine(folder, "Program.cs"), Source);
        var program = Path.Combine(folder, "bin", "shell", "Probe.exe");
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = folder };
        info.ArgumentList.Add("build");
        info.ArgumentList.Add(project);
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("Release");
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add(Path.Combine(folder, "bin", "shell"));
        info.ArgumentList.Add("--nologo");
        ChildProcess.Run(info, TimeSpan.FromMinutes(10)).AssertExitCode(0);
        Assert.True(File.Exists(program), $"The probe built, but {program} is not there.");
        return program;
    }

    private static void WriteIfDifferent(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path) == text)
            return;
        File.WriteAllText(path, text);
    }


    private static string Project(string shellProject) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
            <SupportedOSPlatformVersion>10.0.22621.0</SupportedOSPlatformVersion>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">$(NETCoreSdkRuntimeIdentifier)</RuntimeIdentifier>
            <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
            <UseWinUI>true</UseWinUI>
            <WindowsPackageType>None</WindowsPackageType>
            <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
            <AssemblyName>Probe</AssemblyName>
            <DisableWinExeOutputInference>true</DisableWinExeOutputInference>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="{shellProject}" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>A file whose presence in the config folder makes the probe start a program and never exit.</summary>
    internal const string HangMarker = "probe-hang";

    /// <summary>A file whose presence in the config folder makes the probe hide Explorer's taskbar, and then delete the file.</summary>
    internal const string HideTaskbarMarker = "probe-hide-taskbar";

    /// <summary>
    /// A file whose presence in the config folder makes the probe delete the file and then throw from its configuration's
    /// Start, as a configuration that crashes as it starts would.
    /// </summary>
    internal const string CrashMarker = "probe-crash";

    /// <summary>
    /// A file whose presence in the config folder makes the probe show a small panel, a <c>ProbePage</c> at the top left of
    /// the primary screen, whose button <c>Open</c> shows a popup, a <c>ProbePopup</c>. Neither reserves an edge.
    /// </summary>
    internal const string WindowMarker = "probe-window";

    private const string Source =
        $$"""
        using Winshell;

        using System.Diagnostics;
        using Microsoft.UI.Xaml.Controls;
        using Windows.Foundation;

        public sealed class ProbePage : StackPanel;

        public sealed class ProbePopup : StackPanel;

        internal sealed class Bare : ShellConfiguration
        {
            public override void Start(Shell shell)
            {
                shell.Ipc.Handle("probe", "note", (string text) => { });
                var config = Environment.GetEnvironmentVariable("WINSHELL_CONFIG_DIR");
                // A test that puts {{CrashMarker}} there gets a shell that crashes as it starts, once.
                if (config is not null && File.Exists(Path.Combine(config, "{{CrashMarker}}")))
                {
                    File.Delete(Path.Combine(config, "{{CrashMarker}}"));
                    throw new InvalidOperationException("The probe was asked to crash as it started.");
                }
                // A test that puts {{WindowMarker}} there gets a panel whose button opens a popup.
                if (config is not null && File.Exists(Path.Combine(config, "{{WindowMarker}}")))
                {
                    var popup = new PopupWindow { Content = new ProbePopup { Children = { new TextBlock { Text = "Opened" } } } };
                    var open = new Button { Name = "Open", Content = "Open" };
                    var panel = new PanelWindow
                    {
                        Anchors = Edges.Top | Edges.Left,
                        Width = 160,
                        Height = 60,
                        Content = new ProbePage { Children = { open } },
                        Visible = true,
                    };
                    open.Click += (_, _) =>
                    {
                        popup.Rect = new Rect(panel.Origin.X, panel.Origin.Y + panel.ActualHeight, 160, 60);
                        popup.Visible = true;
                    };
                }
                // A test that puts {{HideTaskbarMarker}} there gets a shell that hides Explorer's taskbar once.
                if (config is not null && File.Exists(Path.Combine(config, "{{HideTaskbarMarker}}")))
                {
                    shell.ExplorerTaskbar.Hidden = true;
                    File.Delete(Path.Combine(config, "{{HideTaskbarMarker}}"));
                }
                // A test that puts {{HangMarker}} there gets a shell that starts a program and then never exits.
                if (config is null || !File.Exists(Path.Combine(config, "{{HangMarker}}")))
                    return;
                var child = Process.Start(new ProcessStartInfo("ping", "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
                File.WriteAllText(Path.Combine(config, "child.pid"), child.Id.ToString());
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Thread.Sleep(Timeout.Infinite);
            }
        }

        internal static class Program
        {
            [STAThread]
            private static void Main() =>
                Shell.Run<Bare>(new ShellOptions { ShellId = Environment.GetEnvironmentVariable("{{ShellIdVariable}}") ?? "probe" });
        }
        """;
}
