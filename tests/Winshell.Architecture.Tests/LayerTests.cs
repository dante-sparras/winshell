using System.Reflection;
using System.Xml.Linq;

namespace Winshell.Architecture.Tests;

/// <summary>The production layers depend only downward, as docs/architecture.md decides.</summary>
public class LayerTests
{
    // From the top down. A layer may use any layer below it, and none above it.
    private static readonly string[] Layers =
        ["Winshell.Shell", "Winshell.Services", "Winshell.Core", "Winshell.Native"];

    [Fact]
    public void A_project_references_only_layers_below_it()
    {
        var upward = Layers.SelectMany((layer, i) => XDocument
            .Load(Path.Combine(Repo.Root, "src", layer, layer + ".csproj"))
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
            .Where(name => !Layers.Skip(i + 1).Contains(name))
            .Select(name => $"{layer} -> {name}"));

        Assert.Empty(upward);
    }

    // A <Reference> to a built DLL, or a package reference to the Winshell package, reaches a higher layer without a
    // project reference.
    [Fact]
    public void An_assembly_references_only_layers_below_it()
    {
        var upward = Layers.SelectMany((layer, i) => Assembly.Load(layer).GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Winshell", StringComparison.OrdinalIgnoreCase) &&
                           !Layers.Skip(i + 1).Contains(name))
            .Select(name => $"{layer} -> {name}"));

        Assert.Empty(upward);
    }
}
