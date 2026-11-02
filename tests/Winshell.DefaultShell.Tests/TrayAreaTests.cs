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
        Assert.Equal("Winshell  is ready. Restart Winshell to update", TrayArea.UpdateCaption(""));
    }

    [Fact]
    public void Icons_on_the_bar_and_in_the_overflow_follow_the_tray_in_order()
    {
        using var rig = new StatusRig();
        var host = new FakeTrayHost();
        using var tray = new SystemTray(rig.Loop, rig.Log, host);
        using var updates = new UpdatesService(rig.Loop, rig.Log, null);
        using var area = new TrayArea(tray, rig.Status, updates);

        Assert.False(area.HasOverflow);
        Assert.Empty(area.OnBar);
        Assert.Empty(area.Hidden);
        Assert.False(area.UpdateReady);
        Assert.Equal(TrayArea.UpdateCaption(""), area.UpdateLabel);

        var plain = new TrayIcon { Key = "plain", Tip = "Tip" };
        host.Add(plain);
        Assert.Equal("plain", Assert.Single(area.Hidden).Id);
        Assert.Empty(area.OnBar);
        Assert.True(area.HasOverflow);

        var a = Icon("a", overflow: false);
        var b = Icon("b", overflow: true);
        var c = Icon("c", overflow: false);
        var d = Icon("d", overflow: true);
        host.Add(a);
        host.Add(b);
        host.Add(c);
        host.Add(d);
        Assert.Equal(["plain", "b", "d"], Ids(area.Hidden));
        Assert.Equal(["a", "c"], Ids(area.OnBar));

        var hidden = area.Hidden.ToList();
        var changes = 0;
        area.Hidden.CollectionChanged += (_, _) => changes++;
        var e = Icon("e", overflow: false);
        host.Add(e);
        Assert.Equal(0, changes);
        Assert.Equal(hidden, area.Hidden);
        Assert.Equal(["a", "c", "e"], Ids(area.OnBar));

        c.Overflow = true;
        host.Change(c);
        Assert.Equal(["a", "e"], Ids(area.OnBar));
        Assert.Equal(["plain", "b", "c", "d"], Ids(area.Hidden));

        host.Remove(b);
        Assert.Equal(["plain", "c", "d"], Ids(area.Hidden));
        Assert.Equal(["a", "e"], Ids(area.OnBar));

        var secret = Icon("secret", overflow: false);
        secret.Hidden = true;
        host.Add(secret);
        Assert.DoesNotContain(area.OnBar, item => item.Id == "secret");
        Assert.DoesNotContain(area.Hidden, item => item.Id == "secret");

        plain.Overflow = false;
        c.Overflow = false;
        d.Overflow = false;
        host.Change(plain);
        host.Change(c);
        host.Change(d);
        Assert.False(area.HasOverflow);
        Assert.Empty(area.Hidden);
        Assert.Equal(["plain", "a", "c", "d", "e"], Ids(area.OnBar));
    }

    [Fact]
    public void Quick_settings_and_the_update_each_raise_their_own_event()
    {
        using var rig = new StatusRig();
        var host = new FakeTrayHost();
        using var tray = new SystemTray(rig.Loop, rig.Log, host);
        using var updates = new UpdatesService(rig.Loop, rig.Log, null);
        using var area = new TrayArea(tray, rig.Status, updates);
        var quick = 0;
        var update = 0;
        area.QuickSettingsRequested += (_, _) => quick++;
        area.UpdatesRequested += (_, _) => update++;

        area.RequestQuickSettings();
        Assert.Equal(1, quick);
        Assert.Equal(0, update);

        area.RequestUpdates();
        Assert.Equal(1, quick);
        Assert.Equal(1, update);
    }

    [Fact]
    public void Disposing_the_area_twice_stops_it_following_the_tray_and_the_status()
    {
        using var rig = new StatusRig();
        var host = new FakeTrayHost();
        using var tray = new SystemTray(rig.Loop, rig.Log, host);
        using var updates = new UpdatesService(rig.Loop, rig.Log, null);
        var area = new TrayArea(tray, rig.Status, updates);
        var glyph = rig.Status.NetworkGlyph;

        area.Dispose();
        area.Dispose();
        host.Add(Icon("z", overflow: false));
        rig.Show(new NetworkSnapshot(true, [], NetworkConnectivity.InternetAccess, true, true));

        Assert.Empty(area.OnBar);
        Assert.Equal(glyph, rig.Status.NetworkGlyph);
    }

    private static TrayIcon Icon(string key, bool overflow) => new() { Key = key, Tip = key, Overflow = overflow };

    private static List<string> Ids(IEnumerable<SystemTrayItem> items) => [.. items.Select(item => item.Id)];
}
