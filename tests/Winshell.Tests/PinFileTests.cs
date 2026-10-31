using System.Reflection;

namespace Winshell.Tests;

/// <summary>
/// Start's <c>launcher.toml</c> and quick settings' <c>quick-settings.toml</c> are each one component.
/// The surface and Winshell Settings both use that component, and the reorder rule lives in one place.
/// </summary>
public class PinFileTests
{
    [Fact]
    public void Start_pins_and_quick_settings_each_define_their_file_once()
    {
        var assembly = typeof(global::Winshell.DefaultShell).Assembly;
        var names = assembly.GetManifestResourceNames();
        Assert.Contains("DefaultShell/StartPins.qml", names);
        Assert.Contains("DefaultShell/QuickSettingsLayout.qml", names);

        var files = names
            .Where(name => name.StartsWith("DefaultShell/", StringComparison.Ordinal)
                && (name.EndsWith(".qml", StringComparison.Ordinal) || name.EndsWith(".js", StringComparison.Ordinal)))
            .Select(name => Read(assembly, name))
            .ToArray();

        Assert.Equal(1, files.Count(text => text.Contains("without this file Start pins its defaults", StringComparison.Ordinal)));
        Assert.Equal(1, files.Count(text => text.Contains("A tile left out is not shown", StringComparison.Ordinal)));
        Assert.Equal(1, files.Count(text => text.Contains("offset > 0 ? to + 1 : to", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            files,
            text => text.Contains(
                "[\"wifi\", \"bluetooth\", \"airplaneMode\", \"energySaver\", \"powerMode\", \"nightLight\", \"accessibility\"]",
                StringComparison.Ordinal));
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
