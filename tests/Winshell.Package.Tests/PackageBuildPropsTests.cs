using System.Xml.Linq;

namespace Winshell.Package.Tests;

/// <summary>
/// Restore reads the target framework, runtime identifier and self-contained flag before a package's props exist.
/// The other build settings travel in the package, so a configuration picks them up when it upgrades.
/// </summary>
public class PackageBuildPropsTests
{
    [Fact]
    public void The_template_keeps_the_properties_restore_reads()
    {
        var template = XDocument.Load(Path.Combine(Repo.Root, "templates", "winshell", "MyShell.csproj"));
        var properties = template.Descendants("PropertyGroup").Elements().Select(element => element.Name.LocalName);

        Assert.Equal(
            new[]
            {
                "ImplicitUsings", "Nullable", "OutputType", "RootNamespace", "RuntimeIdentifier", "SelfContained",
                "TargetFramework"
            },
            properties.Order(StringComparer.Ordinal));
        Assert.Equal("Winshell", template.Descendants("PackageReference").Single().Attribute("Include")!.Value);
    }

    [Fact]
    public void The_package_props_carry_the_configuration_build_settings()
    {
        var props = XDocument.Load(Path.Combine(Repo.Root, "src", "Winshell.Package", "buildTransitive",
            "Winshell.props"));
        var values = props.Descendants("PropertyGroup").Elements()
            .Select(element => (Name: element.Name.LocalName, Value: element.Value.Trim()));

        Assert.Equal(
            new[]
            {
                ("ConcurrentGarbageCollection", "false"),
                ("SupportedOSPlatformVersion", "10.0.22621.0"),
                ("TieredPGO", "false"),
                ("UseWinUI", "true"),
                ("WindowsAppSDKSelfContained", "true"),
                ("WindowsPackageType", "None")
            },
            values.OrderBy(setting => setting.Name, StringComparer.Ordinal));
    }
}
