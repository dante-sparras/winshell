using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// A real program with no windows, shell id <c>probe</c> unless a test names another, built once into <c>bin\shell</c>.
/// Tests of instances start it, so they hold no taskbar and no screen edge unless a test puts <see cref="HideTaskbarMarker"/>
/// in its config folder. A class that starts it has the
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
    internal static int Detach(string config, string local, string[] args)
    {
        var info = new ProcessStartInfo(Program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        info.Environment["WINSHELL_CONFIG_DIR"] = config;
        info.Environment["WINSHELL_LOCAL_DIR"] = local;
        using var process = Process.Start(info)!;
        // Threads of their own, as WinshellCli reads: a busy pool would leave a pipe unread.
        var readOutput = new Thread(() => process.StandardOutput.ReadToEnd()) { IsBackground = true };
        var readError = new Thread(() => process.StandardError.ReadToEnd()) { IsBackground = true };
        readOutput.Start();
        readError.Start();
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail("The probe did not return from -d.");
        }
        Assert.True(readOutput.Join(20_000) && readError.Join(20_000), "The probe's -d start left its output open.");
        return process.ExitCode;
    }

    private static HashSet<int> Listed(string config, string local)
    {
        var result = WinshellCli.Run(["list", "--json"], ("WINSHELL_CONFIG_DIR", config), ("WINSHELL_LOCAL_DIR", local));
        Assert.Equal(0, result.Code);
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
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("build");
        info.ArgumentList.Add(project);
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("Release");
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add(Path.Combine(folder, "bin", "shell"));
        info.ArgumentList.Add("--nologo");
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The probe shell did not build within 10 minutes.");
        }
        Assert.True(process.ExitCode == 0 && File.Exists(program), output + error);
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

    private const string Source =
        $$"""
        using Winshell;

        using System.Diagnostics;

        internal sealed class Bare : ShellConfiguration
        {
            public override void Start(Shell shell)
            {
                var config = Environment.GetEnvironmentVariable("WINSHELL_CONFIG_DIR");
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
