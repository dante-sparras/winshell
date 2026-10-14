using System.Text.Json;
using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

/// <summary>The shell's own words: where a bar sits, what a client can ask, and what the lifetime file means.</summary>
[TestClass]
public sealed class ShellHostTests
{
    [TestMethod]
    public void A_bar_takes_its_edge_and_leaves_the_screen_a_pixel()
    {
        var screen = new ShellRect(0, 0, 1920, 1080);
        BarStrip.Place(screen, BarEdge.Top, 48).Should().Be(new ShellRect(0, 0, 1920, 48));
        BarStrip.Place(screen, BarEdge.Bottom, 48).Should().Be(new ShellRect(0, 1032, 1920, 48));
        BarStrip.Place(screen, BarEdge.Left, 48).Should().Be(new ShellRect(0, 0, 48, 1080));
        BarStrip.Place(screen, BarEdge.Right, 48).Should().Be(new ShellRect(1872, 0, 48, 1080));
        BarStrip.Place(screen, BarEdge.Top, 0).Should().Be(new ShellRect(0, 0, 1920, 0));
        BarStrip.Place(new ShellRect(0, 0, 100, 40), BarEdge.Top, 48).Height.Should().Be(39);
    }

    [TestMethod]
    public void A_bar_edge_is_named_for_the_push()
    {
        ShellEdge.Name(BarEdge.Top).Should().Be("top");
        ShellEdge.Name(BarEdge.Bottom).Should().Be("bottom");
        ShellEdge.Name(BarEdge.Left).Should().Be("left");
        ShellEdge.Name(BarEdge.Right).Should().Be("right");
    }

    [TestMethod]
    public void A_global_name_splits_into_a_plugin_or_an_extension()
    {
        ShellGlobal.TryPlugin("winshell:demo.sheet:close", out var plugin, out var command).Should().BeTrue();
        plugin.Should().Be("demo.sheet");
        command.Should().Be("close");
        ShellGlobal.TryPlugin("winshell:winoma.clock", out _, out _).Should().BeFalse();
        ShellGlobal.TryWinoma("winoma:menu", out var id).Should().BeTrue();
        id.Should().Be("menu");
        ShellGlobal.TryWinoma("winoma:", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_client_line_is_a_summon_a_plugin_command_or_state()
    {
        ShellWords.TryParse("summon demo.sheet", out var open, out _).Should().BeTrue();
        open.Should().Be(new ShellWord.Summon("demo.sheet", null));
        ShellWords.TryParse("summon demo.sheet close", out var acted, out _).Should().BeTrue();
        acted.Should().Be(new ShellWord.Summon("demo.sheet", "close"));
        ShellWords.TryParse("dismiss", out var dismiss, out _).Should().BeTrue();
        dismiss.Should().BeOfType<ShellWord.Dismiss>();
        ShellWords.TryParse("plugin.run demo.sheet toggle down", out var down, out _).Should().BeTrue();
        down.Should().Be(new ShellWord.Plugin("demo.sheet", "toggle", CommandPhase.Down));
        ShellWords.TryParse("plugin run demo.sheet toggle up", out var up, out _).Should().BeTrue();
        up.Should().Be(new ShellWord.Plugin("demo.sheet", "toggle", CommandPhase.Up));
        ShellWords.TryParse("plugin.run demo.sheet toggle", out var once, out _).Should().BeTrue();
        once.Should().Be(new ShellWord.Plugin("demo.sheet", "toggle", CommandPhase.Run));
        ShellWords.TryParse("state -j", out var state, out _).Should().BeTrue();
        state.Should().Be(new ShellWord.State(true));
        ShellWords.TryParse("stop", out var stop, out _).Should().BeTrue();
        stop.Should().BeOfType<ShellWord.Stop>();
        ShellWords.TryParse("nope", out _, out var error).Should().BeFalse();
        error.Should().Contain("Unknown command");
    }

    [TestMethod]
    public void State_names_the_open_panel_the_bars_and_the_facts()
    {
        var report = new ShellReport(
            "demo.sheet",
            ["Hello"],
            [new ShellBarReport(0, "top", ["demo.sheet"])],
            new Dictionary<string, IReadOnlyDictionary<string, object>>
            {
                ["demo.sheet"] = new Dictionary<string, object> { ["ready"] = true },
            },
            new Dictionary<string, IReadOnlyDictionary<string, object>>
            {
                ["winoma"] = new Dictionary<string, object> { ["menu"] = "closed" },
            });

        report.Text().Should().Contain("panel demo.sheet").And.Contain("row Hello").And.Contain("bar 0 top demo.sheet")
            .And.Contain("plugin demo.sheet ready=true").And.Contain("extension winoma menu=closed");
        using var json = JsonDocument.Parse(report.Json());
        json.RootElement.GetProperty("panel").GetString().Should().Be("demo.sheet");
        json.RootElement.GetProperty("rows")[0].GetString().Should().Be("Hello");
        json.RootElement.GetProperty("bars")[0].GetProperty("edge").GetString().Should().Be("top");
        json.RootElement.GetProperty("plugins").GetProperty("demo.sheet").GetProperty("ready").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("extensions").GetProperty("winoma").GetProperty("menu").GetString().Should().Be("closed");

        new ShellReport(null, null, null, null).Text().Should().StartWith("panel none");
    }

    [TestMethod]
    public void A_panel_row_is_its_type_its_label_and_its_command()
    {
        ShellRows.Text(new PanelLine("heading", "Clock")).Should().Be("heading Clock");
        ShellRows.Text(new PanelLine("option", "Mute", Command: "mute")).Should().Be("option Mute mute");
        ShellRows.Texts(null).Should().BeEmpty();
    }

    [TestMethod]
    public void A_winshell_line_is_answered_with_the_same_words_either_host_prints()
    {
        string? summoned = null;
        string? plugin = null;
        var stopped = false;
        var report = new ShellReport("demo.sheet", ["heading Clock"], [new ShellBarReport(0, "top", ["demo.sheet"])], null);

        ShellDispatch.Reply(
            "summon demo.sheet",
            (id, command) =>
            {
                summoned = id + ":" + (command ?? "");
                return "ok";
            },
            () => "dismissed",
            (_, _, _) => { },
            () => report,
            () => { }).Should().Be("ok");
        summoned.Should().Be("demo.sheet:");

        ShellDispatch.Reply("dismiss", (_, _) => "ok", () => "dismissed", (_, _, _) => { }, () => report, () => { })
            .Should().Be("dismissed");

        ShellDispatch.Reply(
            "plugin.run demo.sheet toggle down",
            (_, _) => "ok",
            () => "dismissed",
            (id, command, phase) => plugin = id + " " + command + " " + phase,
            () => report,
            () => { }).Should().Be("ok");
        plugin.Should().Be("demo.sheet toggle Down");

        ShellDispatch.Reply("state", (_, _) => "ok", () => "dismissed", (_, _, _) => { }, () => report, () => { })
            .Should().Contain("panel demo.sheet").And.Contain("row heading Clock").And.Contain("bar 0 top demo.sheet");
        ShellDispatch.Reply("state -j", (_, _) => "ok", () => "dismissed", (_, _, _) => { }, () => report, () => { })
            .Should().Contain("\"panel\":\"demo.sheet\"");

        ShellDispatch.Reply("stop", (_, _) => "ok", () => "dismissed", (_, _, _) => { }, () => report, () => stopped = true)
            .Should().Be("stopped");
        stopped.Should().BeTrue();

        ShellDispatch.Reply("", (_, _) => "ok", () => "dismissed", (_, _, _) => { }, () => report, () => { })
            .Should().Be(ShellWords.Usage);
    }

    [TestMethod]
    public void A_client_prints_the_reply_when_the_shell_closes_the_pipe()
    {
        var name = "Winshell.Test." + Guid.NewGuid().ToString("N");
        using var listening = new ManualResetEventSlim(false);
        // The server runs on a thread of its own: a cold CI runner can take seconds to give the thread pool one.
        var server = Task.Factory.StartNew(() =>
        {
            using var pipe = new System.IO.Pipes.NamedPipeServerStream(
                name,
                System.IO.Pipes.PipeDirection.InOut,
                1,
                System.IO.Pipes.PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
            listening.Set();
            pipe.WaitForConnection();
            using var reader = new StreamReader(pipe, leaveOpen: true);
            reader.ReadLine().Should().Be("state -j");
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine("panel none\nbar 0 top demo.sheet");
        }, TaskCreationOptions.LongRunning);

        listening.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = ShellClient.Ask(name, "state -j", output, error);
        server.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        code.Should().Be(0);
        output.ToString().TrimEnd().Should().Be("panel none\nbar 0 top demo.sheet");
        error.ToString().Should().BeEmpty();
    }

    [TestMethod]
    public void A_stopped_shell_stays_down_and_a_crashed_one_restarts()
    {
        ShellLifetime.Read(null).Should().Be(ShellLife.Running);
        ShellLifetime.Read("stopped").Should().Be(ShellLife.Stopped);
        ShellLifetime.Text(ShellLife.Replacing).Should().Be("replacing");
        ShellLifetime.AfterExit(ShellLife.Running, arrived: false, TimeSpan.Zero).Should().Be(ShellExit.Restart);
        ShellLifetime.AfterExit(ShellLife.Stopped, arrived: false, TimeSpan.Zero).Should().Be(ShellExit.StayDown);
        ShellLifetime.AfterExit(ShellLife.Replacing, arrived: false, TimeSpan.FromSeconds(1)).Should().Be(ShellExit.Wait);
        ShellLifetime.AfterExit(ShellLife.Replacing, arrived: true, TimeSpan.FromSeconds(1)).Should().Be(ShellExit.Leave);
        ShellLifetime.AfterExit(ShellLife.Replacing, arrived: false, ShellLifetime.ReplaceLimit).Should().Be(ShellExit.StayDown);
        ShellPipe.Name.Should().StartWith(ShellPipe.Prefix + ".");
        ConfigFolder.DefaultPath.Should().EndWith(Path.Combine(".config", "winshell"));
    }
}
