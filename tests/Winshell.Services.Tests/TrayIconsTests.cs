using Winshell.Tray;

namespace Winshell.Services.Tests;

/// <summary>The icon table over plain <c>Shell_NotifyIcon</c> calls. No window, no app, no registry.</summary>
public sealed class TrayIconsTests
{
    private const string Program = @"C:\Apps\sync.exe";
    private readonly TrayIcons _icons = new(_ => (null, null), _ => (42, Program));

    private static NotifyIconCall Call(uint message, uint flags = 0, nint window = 0x100, uint id = 1, Guid guid = default) =>
        new() { Message = message, Flags = flags, Window = window, Id = id, Guid = guid };

    [Fact]
    public void An_icon_added_by_guid_is_the_same_icon_when_an_app_names_it_by_its_window()
    {
        var added = new List<TrayIcon>();
        _icons.Added += added.Add;
        var guid = Guid.NewGuid();

        _icons.Apply(Call(TrayProtocol.Add, TrayProtocol.NIF_GUID, guid: guid));
        // NIM_SETVERSION and later changes often leave the GUID flag off.
        _icons.Apply(Call(TrayProtocol.SetVersion) with { TimeoutOrVersion = TrayProtocol.Version4 });
        _icons.Apply(Call(TrayProtocol.Modify, TrayProtocol.NIF_TIP) with { Tip = "Synced" });

        var icon = Assert.Single(_icons.Icons);
        Assert.Same(icon, Assert.Single(added));
        Assert.Equal(TrayProtocol.Version4, icon.Version);
        Assert.Equal("Synced", icon.Tip);
    }

    [Fact]
    public void A_deleted_icon_is_gone_and_a_change_to_it_adds_nothing()
    {
        var removed = new List<TrayIcon>();
        _icons.Removed += removed.Add;
        _icons.Apply(Call(TrayProtocol.Modify, TrayProtocol.NIF_TIP) with { Tip = "Never added" });
        Assert.Empty(_icons.Icons);

        _icons.Apply(Call(TrayProtocol.Add));
        var icon = Assert.Single(_icons.Icons);
        _icons.Apply(Call(TrayProtocol.Delete));
        _icons.Apply(Call(TrayProtocol.Modify, TrayProtocol.NIF_TIP) with { Tip = "After delete" });

        Assert.Empty(_icons.Icons);
        Assert.Same(icon, Assert.Single(removed));
    }

    [Fact]
    public void Hidden_changes_only_when_the_state_mask_names_it()
    {
        _icons.Apply(Call(TrayProtocol.Add, TrayProtocol.NIF_STATE) with { State = TrayProtocol.NIS_HIDDEN, StateMask = TrayProtocol.NIS_HIDDEN });
        var icon = Assert.Single(_icons.Icons);
        Assert.True(icon.Hidden);

        _icons.Apply(Call(TrayProtocol.Modify, TrayProtocol.NIF_STATE) with { State = 0, StateMask = 0 });
        Assert.True(icon.Hidden);

        _icons.Apply(Call(TrayProtocol.Modify, TrayProtocol.NIF_STATE) with { State = 0, StateMask = TrayProtocol.NIS_HIDDEN });
        Assert.False(icon.Hidden);
    }

    [Fact]
    public void A_balloon_needs_its_text()
    {
        var balloons = new List<(string Title, string Text, uint Flags)>();
        _icons.Balloon += (_, title, text, flags) => balloons.Add((title, text, flags));
        _icons.Apply(Call(TrayProtocol.Add));

        _icons.Apply(Call(TrayProtocol.Modify, TrayProtocol.NIF_INFO) with { InfoTitle = "Backup", Info = "Done", InfoFlags = 1 });
        // An empty text takes the balloon down.
        _icons.Apply(Call(TrayProtocol.Modify, TrayProtocol.NIF_INFO) with { InfoTitle = "Backup", Info = "" });

        Assert.Equal(("Backup", "Done", 1u), Assert.Single(balloons));
    }

    [Fact]
    public void Windows_settings_move_icons_and_only_a_moved_icon_changes()
    {
        _icons.Apply(Call(TrayProtocol.Add, id: 1));
        _icons.Apply(Call(TrayProtocol.Add, id: 2));
        var changed = new List<uint>();
        _icons.Changed += icon => changed.Add(icon.Id);
        // Windows has no choice for either yet, so both start in the flyout.
        Assert.All(_icons.Icons, icon => Assert.True(icon.Overflow));

        List<TrayIconSetting> promoted = [new(Guid.Empty, Program, 1, Promoted: true)];
        _icons.UseSettings(promoted);
        _icons.UseSettings(promoted);

        Assert.Equal([1u], changed);
        Assert.False(_icons.Icons.Single(icon => icon.Id == 1).Overflow);
        Assert.True(_icons.Icons.Single(icon => icon.Id == 2).Overflow);
    }

    [Fact]
    public void Only_nim_setversion_sets_the_version()
    {
        // A balloon timeout of 4 is not interface version 4.
        _icons.Apply(Call(TrayProtocol.Add, TrayProtocol.NIF_INFO) with { Info = "Hi", TimeoutOrVersion = TrayProtocol.Version4 });
        var icon = Assert.Single(_icons.Icons);
        Assert.Equal(0u, icon.Version);

        _icons.Apply(Call(TrayProtocol.SetVersion) with { TimeoutOrVersion = TrayProtocol.Version4 });
        Assert.Equal(TrayProtocol.Version4, icon.Version);

        _icons.Apply(Call(TrayProtocol.Modify, TrayProtocol.NIF_INFO) with { Info = "Again", TimeoutOrVersion = TrayProtocol.Version });
        Assert.Equal(TrayProtocol.Version4, icon.Version);
    }
}
