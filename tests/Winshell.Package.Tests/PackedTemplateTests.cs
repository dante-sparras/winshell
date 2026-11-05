using System.Diagnostics;
using Winshell;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Winshell.Package.Tests;

/// <summary>
/// Packs the library and the templates, builds each one the way a start does, and runs it. This is slow, and the main
/// test stage leaves <c>Category=Package</c> out.
/// </summary>
[Trait("Category", "Package")]
[Collection(nameof(PackedTemplateTests))]
[CollectionDefinition(nameof(PackedTemplateTests), DisableParallelization = true)]
public sealed class PackedTemplateTests(PackedTemplates templates) : IClassFixture<PackedTemplates>
{
    private const int PatienceMs = 1_800_000;

    private static readonly string Cli = Path.GetFullPath(typeof(PackedTemplateTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "WinshellCli").Value!);

    [Fact(Timeout = PatienceMs)]
    public void The_small_shell_is_listed_and_killed()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        Run("PackedShell", templates.SmallProject);
    }

    [Fact(Timeout = PatienceMs)]
    public void The_default_shell_is_listed_and_killed()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        // The project's own name, not the built-in default shell's id, whose folders and mutex it would share.
        Run("PackedShell", templates.DefaultProject);
    }

    [Fact(Timeout = PatienceMs)]
    public void The_templates_take_WindowsAppSDK_from_the_Winshell_package()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        // A version of its own here would drift from Directory.Packages.props and fail restore with NU1605.
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", File.ReadAllText(templates.SmallProject), StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", File.ReadAllText(templates.DefaultProject), StringComparison.Ordinal);
    }

    [Fact(Timeout = PatienceMs)]
    public void Packing_and_creating_the_templates_leaves_the_user_PATH_as_it_was()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(templates.UserPathBefore, PackedTemplates.UserPath());
    }

    private void Run(string shellId, string projectFile)
    {
        var project = ConfigurationProject.Find(projectFile, null, Path.GetDirectoryName(projectFile)!, out var error);
        Assert.Null(error);
        var why = project!.Prepare(false, ConfigurationProject.Run);
        Assert.True(why is null, why);

        var dir = Path.Combine(Path.GetTempPath(), "winshell-package-tests", Guid.NewGuid().ToString("N"));
        (string Name, string Value)[] folders =
        [
            ("WINSHELL_CONFIG_DIR", Path.Combine(dir, "config")),
            ("WINSHELL_LOCAL_DIR", Path.Combine(dir, "local")),
        ];
        Directory.CreateDirectory(folders[0].Value);
        Directory.CreateDirectory(folders[1].Value);
        var output = new StringBuilder();
        var process = Start(project.Program, folders, output);
        try
        {
            var pid = process.Id.ToString(CultureInfo.InvariantCulture);
            Wait.Until(() =>
            {
                if (process.HasExited)
                    Assert.Fail($"The shell exited with {process.ExitCode}: {output}");
                return Listed(folders).Any(row => row.GetProperty("pid").GetInt32() == process.Id);
            }, $"instance {pid} in winshell list");

            Assert.Equal(shellId, Listed(folders).Single(row => row.GetProperty("pid").GetInt32() == process.Id).GetProperty("shellId").GetString());
            var log = CliRun(["log", "--pid", pid], folders);
            Assert.Equal(0, log.Code);
            Assert.Contains($"Winshell {templates.Version} on Windows", log.Output, StringComparison.Ordinal);

            var killed = CliRun(["kill", "--pid", pid], folders);
            Assert.Equal(0, killed.Code);
            Wait.Until(() => process.HasExited, "winshell kill to stop the shell");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            process.Dispose();
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Process Start(string exe, (string Name, string Value)[] environment, StringBuilder output)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var (name, value) in environment)
            info.Environment[name] = value;
        var process = Process.Start(info)!;
        void Add(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is not null)
                lock (output)
                    output.AppendLine(e.Data);
        }
        process.OutputDataReceived += Add;
        process.ErrorDataReceived += Add;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static List<JsonElement> Listed((string Name, string Value)[] environment)
    {
        var result = CliRun(["list", "--json"], environment);
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToList();
    }

    private static (int Code, string Output, string Error) CliRun(IReadOnlyList<string> args, (string Name, string Value)[] environment)
    {
        Assert.True(File.Exists(Cli), $"No CLI at {Cli}");
        return Command(Cli, args, environment);
    }

    private static (int Code, string Output, string Error) Command(string file, IReadOnlyList<string> args, (string Name, string Value)[]? environment)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var (name, value) in environment)
                info.Environment[name] = value;
        using var process = Process.Start(info)!;
        var output = "";
        var error = "";
        var readOutput = new Thread(() => output = process.StandardOutput.ReadToEnd()) { IsBackground = true };
        var readError = new Thread(() => error = process.StandardError.ReadToEnd()) { IsBackground = true };
        readOutput.Start();
        readError.Start();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"{file} {string.Join(' ', args)} did not exit");
        }
        Assert.True(readOutput.Join(TimeSpan.FromMinutes(1)) && readError.Join(TimeSpan.FromMinutes(1)));
        return (process.ExitCode, output, error);
    }
}

/// <summary>One pack and one copy of each template, shared by the shells that run it.</summary>
public sealed class PackedTemplates : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "winshell-package-tests", Guid.NewGuid().ToString("N"));

    public PackedTemplates()
    {
        UserPathBefore = UserPath();
        var root = Repo();
        // A version of its own each run. NuGet keeps a package by its version, so a repeated one would build what an
        // earlier run packed, and the test would pass on code it never packed.
        Version = "0.0.0-test." + Guid.NewGuid().ToString("N")[..12];
        var packages = Path.Combine(_work, "packages");
        var packed = ConfigurationProject.Run("pwsh", ["-NoProfile", "-File", Path.Combine(root, "scripts", "pack.ps1"), "-Out", packages, "-Version", Version], root);
        Assert.True(packed.ExitCode == 0, packed.Output);

        var library = Path.Combine(packages, $"Winshell.{Version}.nupkg");
        AssertPackage(library, Version);
        var templates = Path.Combine(packages, $"Winshell.Templates.{Version}.nupkg");
        Assert.True(File.Exists(templates), $"No template package at {templates}");

        // A hive of the run's own keeps the templates out of the developer's. A DOTNET_CLI_HOME of its own would too, but
        // the SDK's first run in a new home appends that home's tools folder to the user PATH.
        var hive = Path.Combine(_work, "hive");
        var install = DotnetNew(hive, "install", templates);
        Assert.True(install.ExitCode == 0, install.Output);
        SmallProject = Instantiate(hive, packages, "winshell");
        DefaultProject = Instantiate(hive, packages, "winshell-default");
    }

    public string Version { get; }

    public string SmallProject { get; }

    public string DefaultProject { get; }

    /// <summary>The developer's own PATH, read before anything was packed or created.</summary>
    public (RegistryValueKind Kind, string? Value) UserPathBefore { get; }

    /// <summary>HKCU\Environment\Path as stored, so <c>%USERPROFILE%</c> stays unexpanded.</summary>
    public static (RegistryValueKind Kind, string? Value) UserPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        if (key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value)
            return (RegistryValueKind.None, null);
        return (key.GetValueKind("Path"), value);
    }

    public void Dispose()
    {
        var cached = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } packages
            ? packages
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        foreach (var folder in new[] { _work, Path.Combine(cached, "winshell", Version) })
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private string Instantiate(string hive, string packages, string template)
    {
        var folder = Path.Combine(_work, template);
        var created = DotnetNew(hive, template, "-n", "PackedShell", "-o", folder);
        Assert.True(created.ExitCode == 0, created.Output);
        var project = Assert.Single(Directory.GetFiles(folder, "*.csproj"));
        File.WriteAllText(Path.Combine(folder, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{packages}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        return project;
    }

    private static CommandResult DotnetNew(string hive, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("new");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--debug:custom-hive");
        start.ArgumentList.Add(hive);
        using var process = Process.Start(start)!;
        var output = "";
        var error = "";
        var readOutput = new Thread(() => output = process.StandardOutput.ReadToEnd()) { IsBackground = true };
        var readError = new Thread(() => error = process.StandardError.ReadToEnd()) { IsBackground = true };
        readOutput.Start();
        readError.Start();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)) || !readOutput.Join(TimeSpan.FromMinutes(1)) || !readError.Join(TimeSpan.FromMinutes(1)))
        {
            process.Kill(entireProcessTree: true);
            return new CommandResult(-1, "dotnet did not exit");
        }
        return new CommandResult(process.ExitCode, output + error);
    }

    private static void AssertPackage(string nupkg, string version)
    {
        using var zip = ZipFile.OpenRead(nupkg);
        var names = zip.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
        var lib = "lib/net10.0-windows10.0.22621.0/";
        Assert.Equal([lib + "Winshell.Native.dll", lib + "Winshell.Services.dll", lib + "Winshell.Shell.dll"], names.Where(name => name.StartsWith("lib/", StringComparison.Ordinal)).Order());

        var services = zip.GetEntry(lib + "Winshell.Services.dll")!;
        var extracted = Path.Combine(Path.GetTempPath(), "winshell-package-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extracted);
        var dll = Path.Combine(extracted, "Winshell.Services.dll");
        services.ExtractToFile(dll);
        Assert.Equal(version, InformationalVersion(dll));
        Assert.False(IsPublic(dll, "Winshell", "ConfigurationProject"));
        Directory.Delete(extracted, true);

        var nuspec = zip.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = nuspec.Open();
        var dependencies = XDocument.Load(stream).Descendants().Where(element => element.Name.LocalName == "dependency")
            .ToDictionary(element => element.Attribute("id")!.Value, element => element.Attribute("version")!.Value);
        // A new project gets WindowsAppSDK through this package, so these are the versions it restores.
        var pinned = XDocument.Load(Path.Combine(Repo(), "Directory.Packages.props")).Descendants("PackageVersion")
            .ToDictionary(element => element.Attribute("Include")!.Value, element => element.Attribute("Version")!.Value);
        Assert.Equal(pinned["Microsoft.WindowsAppSDK.WinUI"], dependencies["Microsoft.WindowsAppSDK.WinUI"]);
        Assert.Equal(pinned["Microsoft.WindowsAppSDK.Foundation"], dependencies["Microsoft.WindowsAppSDK.Foundation"]);
        Assert.Equal(
            ["Microsoft.Win32.SystemEvents", "Microsoft.WindowsAppSDK.Foundation", "Microsoft.WindowsAppSDK.WinUI", "System.Management", "Tomlyn", "Vortice.Direct3D11", "Vortice.DXGI"],
            dependencies.Keys.Order(StringComparer.OrdinalIgnoreCase));
    }

    private static string InformationalVersion(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
                continue;
            var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference)
                continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (reader.GetString(type.Name) != "AssemblyInformationalVersionAttribute")
                continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1)
                continue;
            return blob.ReadSerializedString() ?? "";
        }
        throw new InvalidOperationException($"No informational version in {path}");
    }

    private static bool IsPublic(string path, string ns, string name)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (reader.GetString(type.Namespace) == ns && reader.GetString(type.Name) == name)
                return (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public;
        }
        throw new InvalidOperationException($"No type {ns}.{name} in {path}");
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "scripts", "pack.ps1")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"No scripts/pack.ps1 above {AppContext.BaseDirectory}");
    }
}
