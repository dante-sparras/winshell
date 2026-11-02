using System.ComponentModel;
using Winshell.Tray;

namespace Winshell.Services.Tests;

/// <summary>The tray over a fake host. Nothing here touches the real notification area.</summary>
public sealed class SystemTrayTests
{
    private readonly ManualEventLoop _loop = new();
    private readonly FakeHost _host = new();

    private SystemTray Tray() => new(_loop, Log.Default, _host);

    private static TrayIcon Icon(string key = "a", bool hidden = false) => new() { Key = key, Tip = "Tip", Hidden = hidden };

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    [Fact]
    public void Icons_the_host_already_has_are_listed()
    {
        _host.Known.Add(Icon("a"));
        _host.Known.Add(Icon("b", hidden: true));

        using var tray = Tray();

        Assert.Equal("a", Assert.Single(tray.Items).Id);
    }

    [Fact]
    public void An_icon_keeps_its_item_and_only_what_differs_changes()
    {
        using var tray = Tray();
        var icon = Icon();
        _host.Add(icon);
        var item = tray.Items[0];
        var changes = Watch(item);

        icon.Tip = "Changed";
        icon.Image = new PixelImage(1, 1, new byte[4]);
        _host.Change(icon);
        _host.Change(icon);

        Assert.Same(item, Assert.Single(tray.Items));
        Assert.Equal([nameof(SystemTrayItem.Icon), nameof(SystemTrayItem.Tooltip)], changes);
        Assert.Equal("Changed", item.Tooltip);
        Assert.Same(icon.Image, item.Icon);
    }

    [Fact]
    public void An_icon_the_app_hides_leaves_the_list_until_it_shows_it_again()
    {
        using var tray = Tray();
        var icon = Icon();
        _host.Add(icon);

        icon.Hidden = true;
        _host.Change(icon);
        Assert.Empty(tray.Items);

        icon.Hidden = false;
        _host.Change(icon);
        Assert.Single(tray.Items);

        _host.Remove(icon);
        Assert.Empty(tray.Items);
    }

    [Fact]
    public void An_item_is_ready_once_it_is_listed()
    {
        using var tray = Tray();
        var ready = 0;
        _host.Add(Icon());
        tray.Items[0].Ready += () => ready++;

        _loop.Process();
        _loop.Process();

        Assert.Equal(1, ready);
    }

    [Theory]
    [InlineData(0u, BalloonIcon.None)]
    [InlineData(2u | 0x10u, BalloonIcon.Warning)]
    [InlineData(4u, BalloonIcon.App)]
    [InlineData(9u, BalloonIcon.None)]
    public void A_balloon_reaches_its_item(uint flags, BalloonIcon expected)
    {
        using var tray = Tray();
        var icon = Icon();
        _host.Add(icon);
        var balloons = new List<(string, string, BalloonIcon)>();
        tray.Items[0].Balloon += (title, message, kind) => balloons.Add((title, message, kind));

        _host.ShowBalloon(icon, "Title", "Message", flags);

        Assert.Equal(("Title", "Message", expected), Assert.Single(balloons));
    }

    [Fact]
    public void Clicks_reach_the_host_with_the_point_they_name()
    {
        using var tray = Tray();
        _host.Add(Icon());
        var item = tray.Items[0];

        item.Click();
        item.DoubleClick();
        item.MiddleClick();
        item.Display();
        item.Display(10, 20);
        item.Menu.Display();
        item.Menu.Display(30, 40);

        Assert.Equal(
        [
            (TrayClick.Left, null),
            (TrayClick.Double, null),
            (TrayClick.Middle, null),
            (TrayClick.Menu, null),
            (TrayClick.Menu, (10, 20)),
            (TrayClick.Menu, null),
            (TrayClick.Menu, (30, 40)),
        ], _host.Clicks);
    }

    [Fact]
    public void An_icon_with_no_resource_is_the_apps_own_bitmap_at_any_size()
    {
        using var tray = Tray();
        var image = new PixelImage(1, 1, new byte[4]);
        var icon = Icon();
        icon.Image = image;
        _host.Add(icon);

        Assert.Same(image, tray.Items[0].IconAt(32));
    }

    [Fact]
    public void A_disposed_tray_stops_following_the_host()
    {
        var tray = Tray();
        _host.Add(Icon("a"));
        var ready = 0;
        tray.Items[0].Ready += () => ready++;

        tray.Dispose();
        _host.Add(Icon("b"));
        _loop.Process();

        Assert.Single(tray.Items);
        Assert.Equal(0, ready);
        Assert.Equal(0, _host.Listeners);
    }

    [Fact]
    public void A_system_message_is_never_an_icons_callback()
    {
        using var host = new TrayHost(_loop, Log.Default, Names(), () => { });
        host.OnNotifyIcon(new NotifyIconCall { Message = TrayProtocol.Add, Window = 1, Id = 1, Flags = TrayProtocol.NIF_MESSAGE, CallbackMessage = 0x8001 });
        var icon = Assert.Single(host.Icons);
        Assert.Equal(0x8001u, icon.CallbackMessage);

        // WM_CLOSE: any app could name it for any window, and a click would close that window.
        host.OnNotifyIcon(new NotifyIconCall { Message = TrayProtocol.Modify, Window = 1, Id = 1, Flags = TrayProtocol.NIF_MESSAGE, CallbackMessage = 0x0010 });

        Assert.Equal(0u, icon.CallbackMessage);
    }

    [Fact]
    public void An_image_is_not_loaded_from_a_network_module() =>
        Assert.Null(TrayHost.ResourceImage(new IconResource(@"\\nonexistent-host.invalid\share\x.dll", 1, ""), 32));

    [Fact]
    public void An_image_is_drawn_from_a_local_module_with_straight_alpha()
    {
        var image = TrayHost.ResourceImage(new IconResource(Path.Combine(Environment.SystemDirectory, "shell32.dll"), 3, ""), 32);

        Assert.NotNull(image);
        Assert.Equal(32, image.Width);
        Assert.Equal(32 * 32 * 4, image.Bgra.Length);
        Assert.False(image.Premultiplied);
    }

    private static TrayNames Names()
    {
        var id = Guid.NewGuid().ToString("N");
        return new TrayNames($"WinshellTestTray-{id}", $"WinshellTestNone-{id}", $@"Local\winshell-test-tray-{id}");
    }

    private sealed class FakeHost : ITrayHost
    {
        public List<TrayIcon> Known { get; } = [];

        public List<(TrayClick Click, (int X, int Y)? At)> Clicks { get; } = [];

        public int Listeners => (Added?.GetInvocationList().Length ?? 0) + (Changed?.GetInvocationList().Length ?? 0)
            + (Removed?.GetInvocationList().Length ?? 0) + (Balloon?.GetInvocationList().Length ?? 0);

        public IReadOnlyCollection<TrayIcon> Icons => Known;

        public event Action<TrayIcon>? Added;

        public event Action<TrayIcon>? Changed;

        public event Action<TrayIcon>? Removed;

        public event Action<TrayIcon, string, string, uint>? Balloon;

        public void Add(TrayIcon icon) => Added?.Invoke(icon);

        public void Change(TrayIcon icon) => Changed?.Invoke(icon);

        public void Remove(TrayIcon icon) => Removed?.Invoke(icon);

        public void ShowBalloon(TrayIcon icon, string title, string message, uint flags) => Balloon?.Invoke(icon, title, message, flags);

        public void Click(TrayIcon icon, TrayClick click, (int X, int Y)? at) => Clicks.Add((click, at));
    }
}
