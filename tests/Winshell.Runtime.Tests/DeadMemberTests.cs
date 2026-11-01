using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// Members that used to be present and do nothing are either gone, so a configuration that names them fails to
/// load, or they do what their name says. <c>FloatingWindow.fullscreen</c>, <c>FloatingWindow.parentWindow</c>,
/// <c>ShellWindow.surfaceFormat</c>, <c>ShellWindow.resourcesLost</c> and the screen pixel densities work again,
/// so this test no longer names them.
/// </summary>
public class DeadMemberTests
{
    [Theory]
    [InlineData("MenuEntry { }", "MenuEntry")]
    [InlineData("MenuOpener { }", "MenuOpener")]
    [InlineData("MenuAnchor { }", "MenuAnchor")]
    public void A_removed_member_fails_to_load(string body, string name)
    {
        using var shell = new TestShell(("shell.qml", "import Winshell\n" + body + "\n"));
        var error = Assert.Throws<QmlCompileException>(() => shell.Load());
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_screen_reports_its_manufacturer_and_serial()
    {
        var props = MetaType.Of(typeof(ShellScreen)).Properties;
        Assert.True(props.ContainsKey("manufacturer"));
        Assert.True(props.ContainsKey("serialNumber"));
    }

    [Fact]
    public void An_IconTheme_pragma_fails_the_load()
    {
        using var shell = new TestShell(("shell.qml", """
            //@ pragma IconTheme Papirus
            import Winshell
            ShellRoot { objectName: "loaded" }
            """));
        using var host = Host(shell);
        Assert.False(host.Start());
        Assert.Null(host.Root);
        Assert.Contains(shell.Messages, m => m.Contains("Failed to load configuration", StringComparison.Ordinal)
            && m.Contains("IconTheme", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reload_that_adds_IconTheme_keeps_the_running_generation()
    {
        using var shell = new TestShell(("shell.qml", """
            import Winshell
            ShellRoot { objectName: "good" }
            """));
        using var host = Host(shell);
        Assert.True(host.Start());
        var running = host.Root;

        shell.Write("shell.qml", """
            //@ pragma IconTheme Papirus
            import Winshell
            ShellRoot { objectName: "next" }
            """);
        Assert.False(host.TryReload(false));
        Assert.Same(running, host.Root);
        Assert.Contains(shell.Messages, m => m.Contains("Failed to reload configuration", StringComparison.Ordinal)
            && m.Contains("IconTheme", StringComparison.Ordinal));
    }

    [Fact]
    public void Another_pragma_still_loads()
    {
        using var shell = new TestShell(("shell.qml", """
            //@ pragma ShellId kept
            //@ pragma NotAThing value
            import Winshell
            ShellRoot { objectName: "loaded" }
            """));
        using var host = Host(shell);
        Assert.True(host.Start());
        Assert.Equal("loaded", host.Root?.ObjectName);
        Assert.Equal("kept", host.ShellId);
    }

    [Fact]
    public void A_set_anchor_rect_replaces_the_item_size()
    {
        var item = new Item { Width = 40, Height = 20 };
        var anchor = new PopupAnchor(() => { });
        anchor.Item = item;
        var fromItem = anchor.AnchorRectInWindow();
        Assert.Equal(40, fromItem.Width);
        Assert.Equal(20, fromItem.Height);

        // Width is already 1, so the setter must remember that it was set even when the value does not change.
        anchor.Rect.Width = 1;
        var replaced = anchor.AnchorRectInWindow();
        Assert.Equal(1, replaced.Width);
        Assert.Equal(20, replaced.Height);

        var offset = new PopupAnchor(() => { });
        offset.Item = new Item { Width = 40, Height = 20 };
        offset.Rect.X = 5;
        var moved = offset.AnchorRectInWindow();
        Assert.Equal(5, moved.X);
        Assert.Equal(40, moved.Width);
        Assert.Equal(20, moved.Height);
    }

    [Fact]
    public void A_region_radius_cuts_the_corner_and_an_ellipse_ignores_it()
    {
        using (var sharp = new Region { Width = 100, Height = 100 }.ToSkRegion(null))
            Assert.True(sharp.Contains(0, 0));

        using (var round = new Region { Width = 100, Height = 100, Radius = 40 }.ToSkRegion(null))
        {
            Assert.False(round.Contains(0, 0));
            Assert.True(round.Contains(50, 2));
        }

        using (var corner = new Region { Width = 100, Height = 100, TopLeftRadius = 40 }.ToSkRegion(null))
        {
            Assert.False(corner.Contains(0, 0));
            Assert.True(corner.Contains(0, 99));
            Assert.True(corner.Contains(99, 0));
        }

        using var plain = new Region { Width = 100, Height = 100, Shape = (int)Region.ShapeValue.Ellipse }.ToSkRegion(null);
        using var curved = new Region { Width = 100, Height = 100, Shape = (int)Region.ShapeValue.Ellipse, Radius = 80 }.ToSkRegion(null);
        for (var y = 0; y <= 100; y += 5)
        {
            for (var x = 0; x <= 100; x += 5)
                Assert.Equal(plain.Contains(x, y), curved.Contains(x, y));
        }
    }

    private static ShellHost Host(TestShell shell) =>
        new(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, null, shell.Log) { WatchFiles = false };
}
