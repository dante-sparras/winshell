using Winshell.DefaultShell.Taskbar;
using Winshell.Network;
using Winshell.Tray;
using Winshell.Updates;

namespace Winshell.DefaultShell.Tests;

public sealed class TrayAreaTests
{
    [Theory]
    [InlineData("One\nTwo", "App", "One", "One\nTwo")]
    [InlineData("", "App", "App", "App")]
    [InlineData("\nTwo", "App", "", "\nTwo")]
    [InlineData("Only", "App", "Only", "Only")]
    [InlineData("", "App\nName", "App", "App\nName")]
    [InlineData(" ", "App", " ", " ")]
    public void The_name_is_the_first_line_and_the_tip_is_the_whole_text(string tooltip, string appName, string name, string tip)
    {
        Assert.Equal(name, TrayArea.IconName(tooltip, appName));
        Assert.Equal(tip, TrayArea.IconTip(tooltip, appName));
    }

    [Fact]
    public void The_chevron_glyph_follows_whether_the_hidden_icons_are_open()
    {
        Assert.Equal("\uE70D", TrayArea.ChevronGlyph(true));
        Assert.Equal("\uE70E", TrayArea.ChevronGlyph(false));
    }

    [Fact]
    public void Only_a_ready_update_is_shown_and_its_label_names_the_version()
    {
        foreach (var status in Enum.GetValues<UpdateStatus>())
            Assert.Equal(status == UpdateStatus.Ready, TrayArea.ShowsUpdate(status));
        Assert.Equal("Winshell 1.2.3 is ready. Restart Winshell to update", TrayArea.UpdateCaption("1.2.3"));
    }

    [Fact]
    public void Icons_on_the_bar_and_in_the_overflow_follow_the_tray_in_order()
    {
        using var rig = new StatusRig();
        var backend = new FakeTrayBackend();
        using var tray = new SystemTray(rig.Loop, rig.Log, backend);
        rig.Loop.Process();
        using var updates = new UpdatesService(rig.Loop, rig.Log, null);
        var area = new TrayArea(tray, rig.Status, updates);

        Assert.False(area.HasOverflow);
        Assert.Empty(area.OnBar);
        Assert.Empty(area.Hidden);
        Assert.False(area.UpdateReady);
        Assert.Equal("", area.UpdateLabel);

        var plain = backend.Add(Program("plain"), tip: "Tip");
        Assert.Equal("plain", Assert.Single(area.Hidden).Id);
        Assert.Empty(area.OnBar);
        Assert.True(area.HasOverflow);

        backend.Add(Program("a"), tip: "a", onBar: true);
        var b = backend.Add(Program("b"), tip: "b");
        var c = backend.Add(Program("c"), tip: "c", onBar: true);
        var d = backend.Add(Program("d"), tip: "d");
        Assert.Equal(["plain", "b", "d"], Ids(area.Hidden));
        Assert.Equal(["a", "c"], Ids(area.OnBar));

        var hidden = area.Hidden.ToList();
        var changes = 0;
        area.Hidden.CollectionChanged += (_, _) => changes++;
        backend.Add(Program("e"), tip: "e", onBar: true);
        Assert.Equal(0, changes);
        Assert.Equal(hidden, area.Hidden);
        Assert.Equal(["a", "c", "e"], Ids(area.OnBar));

        backend.Move(c, onBar: false);
        Assert.Equal(["a", "e"], Ids(area.OnBar));
        Assert.Equal(["plain", "b", "c", "d"], Ids(area.Hidden));

        backend.Remove(b);
        Assert.Equal(["plain", "c", "d"], Ids(area.Hidden));
        Assert.Equal(["a", "e"], Ids(area.OnBar));

        backend.Add(Program("secret"), tip: "secret", onBar: true, hidden: true);
        Assert.DoesNotContain(area.OnBar, item => item.Id == "secret");
        Assert.DoesNotContain(area.Hidden, item => item.Id == "secret");

        backend.Move(plain, onBar: true);
        backend.Move(c, onBar: true);
        backend.Move(d, onBar: true);
        Assert.False(area.HasOverflow);
        Assert.Empty(area.Hidden);
        Assert.Equal(["plain", "a", "c", "d", "e"], Ids(area.OnBar));
    }

    [Fact]
    public void The_update_icon_raises_its_event()
    {
        using var rig = new StatusRig();
        using var tray = new SystemTray(rig.Loop, rig.Log, new FakeTrayBackend());
        using var updates = new UpdatesService(rig.Loop, rig.Log, null);
        var area = new TrayArea(tray, rig.Status, updates);
        var update = 0;
        area.UpdatesRequested += (_, _) => update++;

        area.RequestUpdates();

        Assert.Equal(1, update);
    }

    private static string Program(string name) => $@"C:\Apps\{name}.exe";

    private static List<string> Ids(IEnumerable<SystemTrayItem> items) => [.. items.Select(item => item.Id)];
}
