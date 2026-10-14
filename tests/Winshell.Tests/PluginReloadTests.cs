using FluentAssertions;

namespace Winshell.Tests;

[TestClass]
public sealed class PluginReloadTests
{
    [TestMethod]
    public void A_built_assembly_under_the_user_folder_is_a_rebuild()
    {
        var plugins = Path.Combine(Path.GetTempPath(), "winoma-plugins");

        PluginReload.Rebuilt(plugins, Path.Combine(plugins, "acme.now", "acme.now.dll")).Should().BeTrue();
        PluginReload.Rebuilt(plugins, Path.Combine(plugins, "acme.now", "bin", "Debug", "acme.now.pdb")).Should().BeTrue();
        PluginReload.Rebuilt(plugins, Path.Combine(plugins, "acme.now", "obj", "Debug", "acme.now.dll")).Should().BeFalse();
        PluginReload.Rebuilt(plugins, Path.Combine(plugins, "acme.now", "manifest.json")).Should().BeFalse();
        PluginReload.Rebuilt(plugins, Path.Combine(Path.GetTempPath(), "elsewhere", "x.dll")).Should().BeFalse();
    }

    [TestMethod]
    public void A_rebuilt_user_plugin_loads_again_and_a_first_load_does_not()
    {
        var root = Path.Combine(Path.GetTempPath(), "winoma-plugin-reload-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "acme.now", firstParty: false);
            Write(root, "winoma.clock", firstParty: true);
            var catalog = PluginFolders.Discover(Path.Combine(root, "built-in"), Path.Combine(root, "plugins")).Plugins;
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(Path.Combine(root, "plugins", "acme.now", "acme.now.dll"), now.UtcDateTime);
            File.SetLastWriteTimeUtc(Path.Combine(root, "built-in", "winoma.clock", "winoma.clock.dll"), now.UtcDateTime);
            var loaded = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)
            {
                ["acme.now"] = now.AddHours(-1),
                ["winoma.clock"] = now.AddHours(-1),
            };

            var newer = PluginReload.NewerThan(catalog, loaded);
            newer.Should().Equal("acme.now");
            PluginReload.NewerThan(catalog, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)).Should().BeEmpty();

            var firstLoad = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)
            {
                ["acme.now"] = now.AddMinutes(1),
            };
            PluginReload.NewerThan(catalog, firstLoad).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void An_assembly_name_that_holds_a_null_character_has_no_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "winoma-plugin-reload-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "acme.now", firstParty: false, assembly: "acme\\u0000.dll");
            var manifest = PluginFolders.Discover(null, Path.Combine(root, "plugins")).Plugins.Should().ContainSingle().Subject;

            PluginReload.AssemblyPath(manifest).Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, string id, bool firstParty, string? assembly = null)
    {
        var folder = Path.Combine(root, firstParty ? "built-in" : "plugins", id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), """
            {
              "schemaVersion": 1,
              "id": "ID",
              "name": "ID",
              "kinds": ["bar-widget"],
              "assembly": "ASSEMBLY",
              "entry": "Acme.Plugin"
            }
            """.Replace("ASSEMBLY", assembly ?? "ID.dll", StringComparison.Ordinal).Replace("ID", id, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(folder, id + ".dll"), "");
    }
}
