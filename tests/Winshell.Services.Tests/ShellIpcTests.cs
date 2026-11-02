using Windows.UI;

namespace Winshell.Services.Tests;

/// <summary>A C# registration has the checks an <c>IpcHandler</c> has: declared types, an exact argument count, and no eval.</summary>
public sealed class ShellIpcTests
{
    private readonly List<string> _warnings = [];

    private ShellIpc Ipc() => new(new Log { Sink = (_, _, message) => _warnings.Add(message) });

    [Fact]
    public void A_handler_gets_its_arguments_as_the_types_it_declares()
    {
        var ipc = Ipc();
        (string, int, bool, double, float, Color)? seen = null;
        ipc.Handle("t", "all", (string text, int number, bool flag, double wide, float narrow, Color color) => { seen = (text, number, flag, wide, narrow, color); });

        ipc.Call("t", "all", ["a b", "-7", "1", "2.5", "0.5", "#ff102030"]);

        Assert.Equal(("a b", -7, true, 2.5, 0.5f, Color.FromArgb(255, 0x10, 0x20, 0x30)), seen);
    }

    [Theory]
    [InlineData("f00", 255, 255, 0, 0)]
    [InlineData("#8f00", 136, 255, 0, 0)]
    [InlineData("#102030", 255, 0x10, 0x20, 0x30)]
    [InlineData("80102030", 0x80, 0x10, 0x20, 0x30)]
    public void A_color_is_hex_with_or_without_its_hash(string text, int a, int r, int g, int b)
    {
        var ipc = Ipc();
        Color? seen = null;
        ipc.Handle("t", "paint", (Color color) => { seen = color; });

        ipc.Call("t", "paint", [text]);

        Assert.Equal(Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b), seen);
    }

    [Fact]
    public void A_result_comes_back_as_text()
    {
        var ipc = Ipc();
        ipc.Handle("t", "number", () => 1234);
        ipc.Handle("t", "real", () => 1.5);
        ipc.Handle("t", "flag", () => true);
        ipc.Handle("t", "color", () => Color.FromArgb(1, 2, 3, 4));
        ipc.Handle("t", "text", () => (string)null!);
        ipc.Handle("t", "nothing", () => { });

        Assert.Equal("1234", ipc.Call("t", "number", []));
        Assert.Equal("1.5", ipc.Call("t", "real", []));
        Assert.Equal("true", ipc.Call("t", "flag", []));
        Assert.Equal("#01020304", ipc.Call("t", "color", []));
        Assert.Equal("", ipc.Call("t", "text", []));
        Assert.Equal("", ipc.Call("t", "nothing", []));
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("true", "false")]
    public void A_bool_is_a_word_or_a_number(string on, string other)
    {
        var ipc = Ipc();
        var flags = new List<bool>();
        ipc.Handle("t", "set", (bool flag) => flags.Add(flag));

        ipc.Call("t", "set", [on]);
        ipc.Call("t", "set", [other]);

        Assert.NotEqual(flags[0], flags[1]);
    }

    [Fact]
    public void The_wrong_number_of_arguments_is_refused_and_nothing_runs()
    {
        var ipc = Ipc();
        var ran = false;
        ipc.Handle("t", "one", (string text) => ran = true);

        Assert.Equal("Function t.one takes 1 argument, 0 given", Assert.Throws<IpcException>(() => ipc.Call("t", "one", [])).Message);
        Assert.Equal("Function t.one takes 1 argument, 2 given", Assert.Throws<IpcException>(() => ipc.Call("t", "one", ["a", "b"])).Message);
        Assert.False(ran);
    }

    [Theory]
    [InlineData("many", "Argument n is not an int: many")]
    [InlineData("1.5", "Argument n is not an int: 1.5")]
    public void Text_that_is_not_the_declared_type_is_refused(string text, string message)
    {
        var ipc = Ipc();
        ipc.Handle("t", "count", (int n) => n);

        Assert.Equal(message, Assert.Throws<IpcException>(() => ipc.Call("t", "count", [text])).Message);
    }

    [Fact]
    public void A_color_that_is_not_hex_is_refused()
    {
        var ipc = Ipc();
        ipc.Handle("t", "paint", (Color color) => { });

        Assert.Equal("Argument color is not a color: 12", Assert.Throws<IpcException>(() => ipc.Call("t", "paint", ["12"])).Message);
    }

    [Fact]
    public void A_handler_with_a_type_ipc_cant_pass_is_refused_when_it_registers()
    {
        Delegate[] unpassable =
        [
            (object value) => { },
            () => new object(),
            (string[] many) => { },
            () => Task.CompletedTask,
            (long big) => { },
        ];

        foreach (var handler in unpassable)
        {
            var error = Assert.Throws<ArgumentException>(() => Ipc().Handle("t", "bad", handler));
            Assert.Contains("Function t.bad has a type IPC can't pass.", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void More_than_ten_parameters_is_refused()
    {
        var eleven = (int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k) => { };

        Assert.Throws<ArgumentException>(() => Ipc().Handle("t", "bad", eleven));
    }

    [Fact]
    public void What_is_missing_is_named_as_an_IpcHandler_names_it()
    {
        var ipc = Ipc();
        ipc.Handle("t", "known", () => { });

        Assert.Equal("Target not found: other", Assert.Throws<IpcException>(() => ipc.Call("other", "known", [])).Message);
        Assert.Equal("Function not found: t.other", Assert.Throws<IpcException>(() => ipc.Call("t", "other", [])).Message);
        Assert.Equal("Property not found: t.other", Assert.Throws<IpcException>(() => ipc.GetProperty("t", "other")).Message);
        Assert.Equal("Signal not found: t.other", Assert.Throws<IpcException>(() => ipc.Listen("t", "other", _ => { })).Message);
    }

    [Fact]
    public void Show_lists_targets_in_order_with_their_declared_types()
    {
        var ipc = Ipc();
        ipc.Handle("settings", "open", (string page, int tab) => true);
        ipc.Property("settings", "page", () => "home");
        ipc.Signal("settings", "changed");
        ipc.Handle("audio", "mute", () => { });

        Assert.Equal(
            "target audio\n  function mute(): void\n"
            + "target settings\n  function open(page: string, tab: int): bool\n  property page: string\n  signal changed()\n",
            ipc.Show());
    }

    [Fact]
    public void A_property_reads_its_value_each_time()
    {
        var ipc = Ipc();
        var value = 1;
        ipc.Property("t", "value", () => value);

        Assert.Equal("1", ipc.GetProperty("t", "value"));
        value = 2;
        Assert.Equal("2", ipc.GetProperty("t", "value"));
    }

    [Fact]
    public void A_property_of_a_type_ipc_cant_pass_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Ipc().Property("t", "value", () => new object()));
    }

    [Fact]
    public void A_signal_reaches_its_listeners_until_they_leave()
    {
        var ipc = Ipc();
        var signal = ipc.Signal("t", "changed");
        var seen = new List<string>();
        var listening = ipc.Listen("t", "changed", seen.Add);

        signal.Emit("one");
        listening.Dispose();
        signal.Emit("two");

        Assert.Equal(["one"], seen);
    }

    [Fact]
    public void A_later_handler_wins_and_says_so_and_the_earlier_ones_removal_leaves_it()
    {
        var ipc = Ipc();
        var earlier = ipc.Handle("t", "name", () => "earlier");
        ipc.Handle("t", "name", () => "later");

        earlier.Dispose();

        Assert.Equal("later", ipc.Call("t", "name", []));
        Assert.Contains("Duplicate IPC name \"t.name\"; the later one wins.", _warnings);
    }

    [Fact]
    public void A_target_with_nothing_left_is_gone()
    {
        var ipc = Ipc();
        var handler = ipc.Handle("t", "name", () => "x");

        handler.Dispose();

        Assert.Empty(ipc.Describe());
        Assert.Throws<IpcException>(() => ipc.Call("t", "name", []));
    }

    [Fact]
    public void A_handler_that_throws_fails_with_its_own_exception()
    {
        var ipc = Ipc();
        ipc.Handle("t", "fail", new Action(() => throw new InvalidOperationException("no")));

        Assert.Equal("no", Assert.Throws<InvalidOperationException>(() => ipc.Call("t", "fail", [])).Message);
    }
}
