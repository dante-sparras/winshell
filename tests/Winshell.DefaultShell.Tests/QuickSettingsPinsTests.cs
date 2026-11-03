using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

/// <summary>The saved tile order, over tiles a test makes and a real layout file.</summary>
public sealed class QuickSettingsPinsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-quick-settings-pins-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Add_is_offered_while_a_tile_the_PC_can_show_is_unpinned()
    {
        var tiles = QuickSettingsKind.All.Select(kind => new QuickSettingsTile(kind.Key, kind.Title, kind.Glyph, "", false, "", "")).ToList();
        using var layout = new QuickSettingsLayout(Path.Combine(_folder, "quick-settings.toml"), new ManualEventLoop(), new Log { MinimumLevel = LogLevel.Fatal });
        var pins = new QuickSettingsPins(tiles, layout);
        var heard = 0;
        pins.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuickSettingsPins.HasUnpinned))
                heard++;
        };
        Assert.False(pins.HasUnpinned);

        pins.Unpin("wifi", fromKeyboard: false);
        Assert.True(pins.HasUnpinned);
        Assert.Equal(1, heard);

        // The Wi-Fi adapter went away, so the add page has nothing it can put back.
        tiles[0].Available = false;
        pins.Refresh();
        Assert.False(pins.HasUnpinned);
        Assert.Equal(2, heard);

        tiles[0].Available = true;
        pins.Refresh();
        Assert.True(pins.HasUnpinned);
        pins.Add("wifi");
        Assert.False(pins.HasUnpinned);
        Assert.Equal(4, heard);
    }
}
