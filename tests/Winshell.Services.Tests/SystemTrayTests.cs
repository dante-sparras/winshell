using System.ComponentModel;
using Winshell.Tray;

namespace Winshell.Services.Tests;

/// <summary>
/// The tray's policy over a fake backend, on a real event queue with a fake clock. Nothing here changes the real
/// notification area; one test asks Explorer's taskbar whether it auto-hides.
/// </summary>
public sealed class SystemTrayTests
{
    private const nint App = 0x100;
    private readonly ManualEventLoop _loop = new();
    private readonly FakeTrayBackend _backend = new() { Cursor = (5, 6) };

    public SystemTrayTests()
    {
        _backend.Windows[App] = 42;
    }

    private int Broadcasts => _backend.Calls.Count(call => call == "broadcast");

    private SystemTray Tray()
    {
        var tray = new SystemTray(_loop, Log.Default, _backend);
        _loop.Process();
        return tray;
    }

    private static NotifyIconCall Call(uint message, uint flags = 0)
    {
        return new NotifyIconCall { Message = message, Flags = flags, Window = App, Id = 1 };
    }

    private static NotifyIconCall Add()
    {
        return Call(TrayProtocol.Add, TrayProtocol.NIF_TIP) with { Tip = "Tip" };
    }

    private static NotifyIconCall Hide(bool hidden)
    {
        return Call(TrayProtocol.Modify, TrayProtocol.NIF_STATE) with
        {
            State = hidden ? TrayProtocol.NIS_HIDDEN : 0, StateMask = TrayProtocol.NIS_HIDDEN
        };
    }

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    [Fact]
    public void An_icon_keeps_its_item_and_only_what_differs_changes()
    {
        using var tray = Tray();
        _backend.Send(Add());
        var item = tray.Items[0];
        var changes = Watch(item);

        var image = new PixelImage(1, 1, new byte[4]);
        _backend.Images[0x42] = image;
        var change = Call(TrayProtocol.Modify, TrayProtocol.NIF_TIP | TrayProtocol.NIF_ICON) with
        {
            Tip = "Changed", Icon = 0x42
        };
        _backend.Send(change);
        _backend.Send(change);

        Assert.Same(item, Assert.Single(tray.Items));
        Assert.Equal([nameof(SystemTrayItem.Icon), nameof(SystemTrayItem.Tooltip)], changes);
        Assert.Equal("Changed", item.Tooltip);
        Assert.Same(image, item.Icon);
    }

    [Fact]
    public void An_icon_the_app_hides_leaves_the_list_until_it_shows_it_again()
    {
        using var tray = Tray();
        _backend.Send(Add());

        _backend.Send(Hide(true));
        Assert.Empty(tray.Items);

        _backend.Send(Hide(false));
        Assert.Single(tray.Items);

        _backend.Send(Call(TrayProtocol.Delete));
        Assert.Empty(tray.Items);
    }

    [Fact]
    public void An_item_is_ready_once_it_is_listed()
    {
        using var tray = Tray();
        var ready = 0;
        _backend.Send(Add());
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
        _backend.Send(Add());
        var balloons = new List<(string, string, BalloonIcon)>();
        tray.Items[0].Balloon += (title, message, kind) => balloons.Add((title, message, kind));

        _backend.Send(Call(TrayProtocol.Modify, TrayProtocol.NIF_INFO) with
        {
            InfoTitle = "Title", Info = "Message", InfoFlags = flags
        });

        Assert.Equal(("Title", "Message", expected), Assert.Single(balloons));
    }

    [Fact]
    public void Clicks_reach_the_app_with_the_point_they_name()
    {
        using var tray = Tray();
        _backend.Send(Call(TrayProtocol.Add, TrayProtocol.NIF_MESSAGE) with { CallbackMessage = 0x8001 });
        // From version 4 on, each callback carries the point.
        _backend.Send(Call(TrayProtocol.SetVersion) with { TimeoutOrVersion = TrayProtocol.Version4 });
        var item = tray.Items[0];

        Action[] clicks =
        [
            item.Click,
            item.DoubleClick,
            item.MiddleClick,
            item.Display,
            () => item.Display(10, 20)
        ];

        Assert.Equal(
        [
            "201 202 400 at 5,6",
            "201 202 400 203 202 400 at 5,6",
            "207 208 at 5,6",
            "204 205 7b at 5,6",
            "204 205 7b at 10,20"
        ], clicks.Select(Clicked));
    }

    /// <summary>The mouse messages a click sent the app, and the point the first one carried.</summary>
    private string Clicked(Action click)
    {
        _backend.Notified.Clear();
        click();
        var point = _backend.Notified[0].WParam;
        return
            $"{string.Join(" ", _backend.Notified.Select(sent => $"{sent.LParam & 0xffff:x}"))} at {point & 0xffff},{point >> 16}";
    }

    [Fact]
    public void A_system_message_is_never_an_icons_callback()
    {
        using var tray = Tray();
        _backend.Send(Call(TrayProtocol.Add, TrayProtocol.NIF_MESSAGE) with { CallbackMessage = 0x8001 });
        tray.Items[0].Click();
        Assert.All(_backend.Notified, sent => Assert.Equal(0x8001u, sent.Message));
        Assert.NotEmpty(_backend.Notified);

        // WM_CLOSE: any app could name it for any window, and a click would close that window.
        _backend.Send(Call(TrayProtocol.Modify, TrayProtocol.NIF_MESSAGE) with { CallbackMessage = 0x0010 });
        _backend.Notified.Clear();
        tray.Items[0].Click();
        Assert.Empty(_backend.Notified);
    }

    [Fact]
    public void An_icon_with_no_resource_is_the_apps_own_bitmap_at_any_size()
    {
        using var tray = Tray();
        var image = new PixelImage(1, 1, new byte[4]);
        _backend.Images[0x42] = image;
        _backend.Send(Call(TrayProtocol.Add, TrayProtocol.NIF_ICON) with { Icon = 0x42 });

        Assert.Same(image, tray.Items[0].IconAt(32));
    }

    [Fact]
    public void An_icon_from_a_resource_is_drawn_again_at_a_new_size()
    {
        using var tray = Tray();
        var image = new PixelImage(16, 16, new byte[16 * 16 * 4]);
        var sharp = new PixelImage(32, 32, new byte[32 * 32 * 4]);
        var resource = new IconResource(@"C:\Apps\sync.exe", 1, "");
        _backend.Images[0x42] = image;
        _backend.Resources[0x42] = resource;
        _backend.Drawn[(resource, 32)] = sharp;
        _backend.Send(Call(TrayProtocol.Add, TrayProtocol.NIF_ICON) with { Icon = 0x42 });
        var item = tray.Items[0];

        Assert.Same(sharp, item.IconAt(32));
        Assert.Same(image, item.IconAt(16));
        // A resource that can't be drawn at that size leaves the app's own bitmap.
        Assert.Same(image, item.IconAt(48));
    }

    [Fact]
    public void Windows_settings_move_an_icon_onto_the_taskbar()
    {
        const string program = @"C:\Apps\sync.exe";
        _backend.Programs[App] = program;
        using var tray = Tray();
        _backend.Send(Add());
        Assert.True(tray.Items[0].Overflow);

        _backend.Settings.Add(new TrayIconSetting(Guid.Empty, program, 1, true));
        _backend.SignalSettingsChanged();

        Assert.False(tray.Items[0].Overflow);
    }

    [Fact]
    public void A_tray_waits_for_the_instance_that_has_the_notification_area_and_takes_over_when_it_exits()
    {
        _backend.Taken = true;
        using var tray = Tray();
        Assert.False(_backend.IsOpen);
        _loop.Advance(TimeSpan.FromSeconds(1));
        Assert.False(_backend.IsOpen);
        Assert.Equal(0, Broadcasts);

        _backend.Taken = false;
        _loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(_backend.IsOpen);
        Assert.Equal(1, Broadcasts);
    }

    [Fact]
    public void An_active_tray_has_no_half_second_poll()
    {
        using var tray = Tray();
        Assert.True(_backend.IsOpen);
        Assert.Equal(TimeSpan.FromSeconds(30), _loop.NextDue - _loop.Now);
    }

    [Fact]
    public void An_icon_whose_window_is_gone_leaves_at_the_next_check()
    {
        using var tray = Tray();
        _backend.Send(Add());
        // The app ended without removing its icon.
        _backend.Windows.Remove(App);

        _loop.Advance(TimeSpan.FromSeconds(29));
        Assert.Single(tray.Items);
        _loop.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(tray.Items);
    }

    [Fact]
    public void Explorer_coming_to_the_front_raises_the_tray_without_waiting_for_a_timer()
    {
        using var tray = Tray();
        _backend.Calls.Clear();

        _backend.SignalExplorerInFront();

        Assert.Equal(["raise"], _backend.Calls);
    }

    [Fact]
    public void A_restarted_explorer_is_answered_once()
    {
        using var tray = Tray();
        Assert.Equal(1, Broadcasts);

        // Its own broadcast comes back to it, and Explorer's taskbar is the one it already answered.
        _backend.SignalTaskbarCreated();
        _loop.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, Broadcasts);

        // Explorer ends, and a new one makes a new taskbar and asks apps to add their icons.
        _backend.Explorer = 0;
        _loop.Advance(TimeSpan.FromSeconds(1));
        _backend.Explorer = 0x600;
        _backend.SignalTaskbarCreated();
        Assert.Equal(1, Broadcasts);
        _loop.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, Broadcasts);

        // That answer comes back too, and is not answered.
        _backend.SignalTaskbarCreated();
        _loop.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Broadcasts);
    }

    [Fact]
    public void An_app_bar_message_lowers_each_open_tray_behind_Explorers_taskbar_and_raises_it_again()
    {
        using var tray = Tray();
        _backend.Calls.Clear();

        AppBarMessages.Autohide(0);

        Assert.Equal(["lower", "raise"], _backend.Calls);

        tray.Dispose();
        _backend.Calls.Clear();
        AppBarMessages.Autohide(0);

        Assert.Empty(_backend.Calls);
    }

    [Fact]
    public void A_disposed_tray_gives_the_notification_area_up_and_stops_following_it()
    {
        var tray = Tray();
        _backend.Send(Add());
        var ready = 0;
        tray.Items[0].Ready += () => ready++;
        _backend.Calls.Clear();

        tray.Dispose();
        _loop.Advance(TimeSpan.FromSeconds(30));

        Assert.Single(tray.Items);
        Assert.Equal(0, ready);
        Assert.False(_backend.IsOpen);
        // The window went first, then apps were asked to add their icons to whichever taskbar window is first now.
        Assert.Equal(["close", "broadcast", "dispose"], _backend.Calls);
    }

    [Fact]
    public void An_image_is_drawn_from_a_local_module_with_straight_alpha()
    {
        var image = IconImage.FromResource(
            new IconResource(Path.Combine(Environment.SystemDirectory, "shell32.dll"), 3, ""), 32);

        Assert.NotNull(image);
        Assert.Equal(32, image.Width);
        Assert.Equal(32 * 32 * 4, image.Bgra.Length);
        Assert.False(image.Premultiplied);
    }
}
