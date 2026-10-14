using FluentAssertions;
using Winland.Ipc;
using Winshell.Core;

namespace Winshell.Tests;

/// <summary>The shell paints one look and opens one Menu, and a toast or a tray cell is decided in Core.</summary>
[TestClass]
public sealed class SurfaceContractTests
{
    [TestMethod]
    public void No_offer_paints_the_shell_look()
    {
        var look = SurfaceContract.Look();

        look.Should().BeSameAs(SurfaceLook.Shell);
        look.Material.Should().Be(SurfaceMaterial.Solid);
        look.Effects.Should().BeFalse();
        look.ThemeId.Should().BeNull();
        look.FontFace.Should().BeEmpty();
        look.BodySize.Should().Be(14);
        look.CaptionSize.Should().Be(12);
        look.CornerSize.Should().Be(8);
        look.ControlCorner.Should().Be(4);
        look.GutterSize.Should().Be(16);
        look.Tokens.Light.Should().BeFalse();
    }

    [TestMethod]
    public void The_first_offered_look_wins()
    {
        var first = Look(light: true, theme: "tokyo-night");
        var second = Look(light: false, theme: "kanagawa");

        SurfaceContract.Look(first, second).Should().BeSameAs(first);
        SurfaceContract.Look(null, second).Should().BeSameAs(second);
        SurfaceContract.Look(null, null).Should().BeSameAs(SurfaceLook.Shell);
    }

    [TestMethod]
    public void A_blank_theme_id_is_none_and_a_null_face_is_empty()
    {
        var look = new SurfaceLook(KitTokens.Blank, SurfaceMaterial.Acrylic, effects: true, themeId: "  ", fontFace: null!);

        look.ThemeId.Should().BeNull();
        look.FontFace.Should().BeEmpty();
        look.Material.Should().Be(SurfaceMaterial.Acrylic);
        look.Effects.Should().BeTrue();
    }

    [TestMethod]
    public void The_named_menu_wins_and_a_blank_name_falls_through_to_the_kind()
    {
        SurfaceContract.Menu(new SurfaceHands { MenuPluginId = " winoma.menu " }, "other.menu").Should().Be("winoma.menu");
        SurfaceContract.Menu(new SurfaceHands { MenuPluginId = "  " }, "winoma.menu").Should().Be("winoma.menu");
        SurfaceContract.Menu(null, " winoma.menu ").Should().Be("winoma.menu");
        SurfaceContract.Menu(SurfaceHands.None, "  ").Should().BeNull();
        SurfaceContract.Menu(null, null).Should().BeNull();
    }

    [TestMethod]
    public void Empty_hands_seek_nothing_and_claim_nothing()
    {
        var hands = SurfaceHands.None;

        hands.TrySeek(1).Should().BeFalse();
        hands.TryFocus(1).Should().BeFalse();
        hands.ScreenWithKeyboard().Should().BeNull();
        hands.IsCovered(new ShellRect(0, 0, 10, 10)).Should().BeFalse();
        hands.IsPaused().Should().BeFalse();
        hands.TryNotice("open").Should().BeFalse();
        var claim = hands.CurrentClaim();
        claim.PickerOpen.Should().BeFalse();
        claim.Escape.Should().BeFalse();
        claim.SessionLocked.Should().BeFalse();
        claim.Keys.Should().BeEmpty();
        ShellClaim.None.Keys.Should().BeEmpty();
    }

    [TestMethod]
    public void Hands_forward_the_seek_the_claim_and_a_bar_edit()
    {
        BarEdge? edge = null;
        string? moved = null;
        var flipped = false;
        var touched = false;
        var hands = new SurfaceHands
        {
            Seek = window => window == 7,
            Claim = () => new ShellClaim(pickerOpen: true, escape: false, sessionLocked: false, keys: [new ClaimedKey(0x25, true, false, false, 1)]),
            Covered = screen => screen.Width > 100,
            Paused = () => true,
            NoticeCommand = command => command == "open",
            MoveEdge = next => edge = next,
            MoveItem = (id, _) => moved = id,
            ToggleTransparent = () => flipped = true,
            BindTouch = touch =>
            {
                touch("winoma.clock");
            },
        };

        hands.TrySeek(7).Should().BeTrue();
        hands.TrySeek(8).Should().BeFalse();
        hands.CurrentClaim().PickerOpen.Should().BeTrue();
        hands.CurrentClaim().Keys.Should().ContainSingle().Which.Vk.Should().Be(0x25);
        hands.IsCovered(new ShellRect(0, 0, 200, 10)).Should().BeTrue();
        hands.IsPaused().Should().BeTrue();
        hands.TryNotice("open").Should().BeTrue();
        hands.TryNotice("nope").Should().BeFalse();
        hands.RequestEdge(BarEdge.Left);
        hands.RequestMove("winoma.clock", new BarDropTarget("left", null, null, 0));
        hands.RequestTransparent();
        hands.BindTouchLater(id => touched = id == "winoma.clock");
        edge.Should().Be(BarEdge.Left);
        moved.Should().Be("winoma.clock");
        flipped.Should().BeTrue();
        touched.Should().BeTrue();
    }

    [TestMethod]
    public void A_new_toast_is_refused_when_every_one_on_screen_stays()
    {
        SurfaceContract.Admit([false, true], out var room).Should().BeTrue();
        room.Should().Be(-1);
        SurfaceContract.Admit([false, true, false], out var leaves).Should().BeTrue();
        leaves.Should().Be(2);
        SurfaceContract.Admit([true, true, true], out var stayed).Should().BeFalse();
        stayed.Should().Be(-1);
    }

    [TestMethod]
    public void A_toast_sits_a_gap_in_from_the_corner_under_a_top_bar()
    {
        var screen = new PixelRect(0, 0, 2560, 1600);
        var bar = new PixelRect(0, 0, 2560, 72);

        ToastPlace.Seat(screen, bar, 540, 200, occupied: 0, gap: 12).Should().Be(new PixelRect(2008, 84, 540, 200));
        ToastPlace.Seat(screen, bar, 540, 200, occupied: 212, gap: 12).Should().Be(new PixelRect(2008, 296, 540, 200));
        ToastPlace.Room(screen, bar).Should().Be(1528);
        ToastPlace.Height(card: 40, scale: 1.5).Should().Be(60);
        ToastPlace.SlideOffset(travel: 100, time: 0, arriving: true).Should().Be(100);
        ToastPlace.SlideOffset(travel: 100, time: 1, arriving: false).Should().Be(100);
        ToastPlace.RestackOffset(from: 40, to: 10, TimeSpan.Zero).Should().Be(30);
        ToastPlace.RestackOffset(from: 40, to: 10, ToastPlace.RestackLength).Should().Be(0);
    }

    [TestMethod]
    public void A_tray_drop_lands_on_the_cell_and_a_gap_misses_the_icon()
    {
        const int stride = TrayCells.Hit + TrayCells.Gap;

        TrayCells.Cell(0, 0, 0).Should().Be(0);
        TrayCells.Cell(stride, 0, 4).Should().Be(1);
        TrayCells.Cell(400, 0, 4).Should().Be(3);
        TrayCells.Cell(0, stride, 5).Should().Be(4);
        TrayCells.IconAt(stride + TrayCells.Hit, 0, 4).Should().Be(-1);
        TrayCells.IconAt(stride, 0, 4).Should().Be(1);
        TrayCells.Width(4, gutter: 8).Should().Be((4 * TrayCells.Hit) + (3 * TrayCells.Gap) + 16);
        TrayCells.AutomationId.Should().Be("tray-overflow");
    }

    [TestMethod]
    public void A_host_that_draws_nothing_answers_the_new_calls()
    {
        IShellHost host = new BareHost();
        host.ApplyLook(SurfaceLook.Shell);
        host.UseHands(null);
        host.ReloadSettings();
        host.TryDispatch("focus", [], out var error).Should().BeFalse();
        error.Should().Be("Winland is not running");
        host.TryQuery("workspace", json: true, out var body).Should().BeFalse();
        body.Should().BeEmpty();
        host.Snapshot().Panel.Should().BeNull();
        host.Snapshot().Bars.Should().BeEmpty();
        host.PanelView.Should().BeNull();
        host.BarThickness.Should().Be(0);
        host.HoverArmed.Should().BeFalse();
        host.Click("winoma.clock").Should().BeFalse();
        host.SendPanel("winoma.clock", "open").Should().BeFalse();
        host.SummonFields("winoma.clock", "", null, null, null).Should().Be("winoma.clock is not running");
        host.ReadsOf(null).Should().Be(default(PanelReads));
        host.Open("winoma.clock");
        host.TakeEscape();
        host.PushNow();
        host.CloseDesk(replaced: false);

        IShellExtension extension = new BareExtension();
        extension.OnWinland("workspace", "{}");
        extension.TrySummon("setup", null).Should().BeFalse();
        extension.FillsSurfaces.Should().BeFalse();
    }

    private static SurfaceLook Look(bool light, string theme) =>
        new(new KitTokens(light, highContrast: false, static _ => default), SurfaceMaterial.Mica, effects: true, theme, "Segoe UI Variable");

    private sealed class BareHost : IShellHost
    {
        public string BaseDirectory => "";

        public string ConfigDirectory => "";

        public ShellConfig Config => throw new NotSupportedException();

        public string? OpenPanelId => null;

        public void OnUi(Action work) => work();

        public string Summon(string pluginId, string? command) => "";

        public string Dismiss() => "";

        public void RunPlugin(string pluginId, string command, bool down)
        {
        }

        public void UseServices(IPluginServiceSource? source)
        {
        }

        public void SetIndicator(string owner, PluginIndicator? indicator)
        {
        }

        public void ShowNotice(ToastFace notice)
        {
        }

        public void SetFacts(string extensionName, IReadOnlyDictionary<string, object?>? facts)
        {
        }
    }

    private sealed class BareExtension : IShellExtension
    {
        public string Name => "bare";

        public IReadOnlyList<string> Surfaces => [];

        public IReadOnlyList<string> Globals => [];

        public IPluginServiceSource? Services => null;

        public void Start(IShellHost host)
        {
        }

        public void Stop()
        {
        }

        public bool TryHandleGlobal(string name, bool down, bool repeat) => false;

        public IReadOnlyDictionary<string, object?>? Facts() => null;
    }
}
