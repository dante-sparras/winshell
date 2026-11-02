using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// A real program with no windows, shell id <c>probe</c> unless a test names another, built once into <c>bin\shell</c>.
/// Tests of instances start it, so they hold no taskbar and no screen edge. Starts share <see cref="Gate"/> so a reload
/// can break the source and put it back without another test launching that build.
/// </summary>
internal static class ProbeShell
{
    private const string ShellIdVariable = "PROBE_SHELL_ID";

    internal static readonly object Gate = new();

    private static string? _folder;
    private static string? _program;

    internal static string Folder
    {
        get
        {
            Ensure();
            return _folder!;
        }
    }

    internal static string Program
    {
        get
        {
            Ensure();
            return _program!;
        }
    }

    /// <summary>Starts the program and waits until <c>winshell list</c> shows it. A null folder is the one this process has.</summary>
    internal static Process Start(string? config, string? local, string? shellId = null)
    {
        var process = Launch(config, local, [], shellId);
        Wait.Until(() => Listed(config, local).Contains(process.Id), $"probe {process.Id} in winshell list");
        return process;
    }

    /// <summary>Starts the program and returns at once. What it prints goes to <paramref name="printed"/>.</summary>
    internal static Process Launch(string? config, string? local, string[] args, string? shellId = null, StringBuilder? printed = null)
    {
        lock (Gate)
        {
            var info = new ProcessStartInfo(Program)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            if (config is not null)
                info.Environment["WINSHELL_CONFIG_DIR"] = config;
            if (local is not null)
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
    }

    /// <summary>
    /// A <c>-d</c> start, to the end of the process that returns. Not redirected: the child would inherit the pipes, and
    /// reading them to the end waits until the shell exits.
    /// </summary>
    internal static int Detach(string config, string local, string[] args)
    {
        lock (Gate)
        {
            var info = new ProcessStartInfo(Program) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            info.Environment["WINSHELL_CONFIG_DIR"] = config;
            info.Environment["WINSHELL_LOCAL_DIR"] = local;
            using var process = Process.Start(info)!;
            if (!process.WaitForExit(20_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                Assert.Fail("The probe did not return from -d.");
            }
            return process.ExitCode;
        }
    }

    private static HashSet<int> Listed(string? config, string? local)
    {
        List<(string, string)> folders = [];
        if (config is not null)
            folders.Add(("WINSHELL_CONFIG_DIR", config));
        if (local is not null)
            folders.Add(("WINSHELL_LOCAL_DIR", local));
        var result = WinshellCli.Run(["list", "--json"], [.. folders]);
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(row => row.GetProperty("pid").GetInt32()).ToHashSet();
    }

    internal static void Ensure()
    {
        lock (Gate)
        {
            if (_program is not null && File.Exists(_program))
                return;
            var root = Repo.Root;
            // One folder per checkout. The project names this checkout's source, so two sharing a folder break each other's build.
            var checkout = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..12];
            _folder = Path.Combine(Path.GetTempPath(), "winshell-probe-shell-" + checkout);
            Directory.CreateDirectory(_folder);
            // The probe sits outside the repo. Without this, dotnet picks a newer SDK and rebuilds the shell from scratch.
            WriteIfDifferent(Path.Combine(_folder, "global.json"), File.ReadAllText(Path.Combine(root, "global.json")));
            var project = Path.Combine(_folder, "Probe.csproj");
            WriteIfDifferent(project, Project(Path.Combine(root, "src", "Winshell.DefaultShell", "Winshell.DefaultShell.csproj")));
            var source = Path.Combine(_folder, "Program.cs");
            WriteIfDifferent(source, Source);
            _program = Path.Combine(_folder, "bin", "shell", "Probe.exe");
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _folder,
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
            info.ArgumentList.Add(Path.Combine(_folder, "bin", "shell"));
            info.ArgumentList.Add("--nologo");
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("The probe shell did not build within 10 minutes.");
            }
            Assert.True(process.ExitCode == 0 && File.Exists(_program), output + error);
        }
    }

    private static void WriteIfDifferent(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path) == text)
            return;
        File.WriteAllText(path, text);
    }


    private static string Project(string defaultShell) =>
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
            <ProjectReference Include="{defaultShell}" />
          </ItemGroup>
        </Project>
        """;

    private const string Source =
        $$"""
        using Winshell;

        internal sealed class Bare : ShellConfiguration
        {
            public override void Start(Shell shell)
            {
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
