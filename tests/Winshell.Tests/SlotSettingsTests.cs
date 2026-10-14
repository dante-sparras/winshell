using FluentAssertions;

namespace Winshell.Tests;

[TestClass]
public sealed class SlotSettingsTests
{
    [TestMethod]
    public void A_number_is_stored_in_its_shortest_round_trip_form()
    {
        var manifest = Manifest();

        SlotSettings.TryStore(manifest, "acme.now", "count", "0.1", out var tenth, out _).Should().BeTrue();
        tenth.Should().Be("0.1", "the Host reads this value back in settings.json and in winoma bar set's reply");
        SlotSettings.TryStore(manifest, "acme.now", "count", "1.50", out var trimmed, out _).Should().BeTrue();
        trimmed.Should().Be("1.5");
        SlotSettings.TryStore(manifest, "acme.now", "count", "1e3", out var thousand, out _).Should().BeTrue();
        thousand.Should().Be("1000");
        SlotSettings.TryStore(manifest, "acme.now", "count", "1e400", out _, out var warning).Should().BeFalse();
        warning.Should().Be("acme.now setting count is not a number");
    }

    private static PluginManifest Manifest()
    {
        var folder = Path.Combine(Path.GetTempPath(), "winoma-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "manifest.json");
        File.WriteAllText(path, """
            {
              "id": "acme.now",
              "kinds": ["bar-widget"],
              "barWidget": { "schema": [ { "key": "count", "type": "number" } ] }
            }
            """);
        PluginManifest.TryRead(path, firstParty: false, out var manifest, out var error).Should().BeTrue(error);
        return manifest!;
    }
}
