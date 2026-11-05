using System.Text.RegularExpressions;
using System.Xml.Linq;
using Winshell.Updates;

namespace Winshell.Services.Tests;

/// <summary>
/// The binaries Winshell signs are named in three places: the update check, SignPath's artifact configuration and the
/// package script. A signed install refuses an update that lacks one of them, so all three must agree, and every
/// assembly built from <c>src/</c> must be among them.
/// </summary>
public sealed partial class SignedBinariesTests
{
    private static readonly XNamespace SignPath = "http://signpath.io/artifact-configuration/v1";

    [Fact]
    public void SignPath_and_the_package_script_sign_what_an_update_must_have()
    {
        var signed = UpdateSignature.Signed.Order(StringComparer.Ordinal);

        var includes = XDocument.Load(Path.Combine(Repo.Root, "packaging", "signpath", "artifact-configuration.xml"))
            .Descendants(SignPath + "include")
            .Select(include => ((string)include.Attribute("path")!).Split('/'))
            .GroupBy(parts => parts[0], parts => parts[1]);
        Assert.Equal(["win-arm64", "win-x64"], includes.Select(runtime => runtime.Key).Order(StringComparer.Ordinal));
        foreach (var runtime in includes)
            Assert.Equal(signed, runtime.Order(StringComparer.Ordinal));

        var script = File.ReadAllText(Path.Combine(Repo.Root, "scripts", "package.ps1"));
        var own = OwnBinaries().Match(script);
        Assert.True(own.Success, "scripts/package.ps1 no longer sets $ownBinaries.");
        Assert.Equal(signed, Quoted().Matches(own.Groups[1].Value).Select(name => name.Groups[1].Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_assembly_built_from_src_is_signed()
    {
        // The package project only packs the others; it builds nothing that ships.
        var assemblies = Directory.EnumerateFiles(Path.Combine(Repo.Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(project => (Path: project, Text: File.ReadAllText(project)))
            .Where(project => !project.Text.Contains("<IsPackable>true</IsPackable>", StringComparison.Ordinal))
            .Select(project => (AssemblyName().Match(project.Text) is { Success: true } name
                ? name.Groups[1].Value
                : Path.GetFileNameWithoutExtension(project.Path)) + ".dll");

        Assert.All(assemblies, assembly => Assert.Contains(assembly, UpdateSignature.Signed));
    }

    [GeneratedRegex(@"\$ownBinaries\s*=\s*(.+)")]
    private static partial Regex OwnBinaries();

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex Quoted();

    [GeneratedRegex("<AssemblyName>([^<]+)</AssemblyName>")]
    private static partial Regex AssemblyName();
}
