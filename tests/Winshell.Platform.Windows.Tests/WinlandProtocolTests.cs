using System.Text;
using System.Text.Json;
using Winshell.Platform.Windows.Services.Winland;

namespace Winshell.Platform.Windows.Tests;

public class WinlandProtocolTests
{
    [Fact]
    public void Pipe_names_follow_the_session_unless_overridden()
    {
        Assert.Equal(["winland-1", "winland"], WinlandProtocol.PipeNames(null, 1));
        Assert.Equal(["custom"], WinlandProtocol.PipeNames(" custom ", 1));
    }

    [Theory]
    [InlineData("workspacev2>>3,3,\\\\.\\DISPLAY1", "workspacev2", "3,3,\\\\.\\DISPLAY1")]
    [InlineData("configreloaded>>", "configreloaded", "")]
    [InlineData("activewindow>>Chrome_WidgetWin_1,a >> b", "activewindow", "Chrome_WidgetWin_1,a >> b")]
    [InlineData("noarrows", "noarrows", "")]
    public void Event_lines_split_at_the_first_arrows(string line, string name, string data)
    {
        Assert.Equal((name, data), WinlandProtocol.SplitEvent(line));
    }

    [Fact]
    public void The_last_argument_keeps_its_commas()
    {
        Assert.Equal(["708ba", "1", "\\\\.\\DISPLAY1", "Chrome_WidgetWin_1", "a, b, c"], WinlandProtocol.ParseArgs("708ba,1,\\\\.\\DISPLAY1,Chrome_WidgetWin_1,a, b, c", 5));
    }

    [Fact]
    public void Missing_arguments_are_empty()
    {
        Assert.Equal(["a", "", ""], WinlandProtocol.ParseArgs("a", 3));
        Assert.Equal(["", ""], WinlandProtocol.ParseArgs("", 2));
        Assert.Equal(["", "", "DISPLAY1"], WinlandProtocol.ParseArgs(",,DISPLAY1", 3));
    }

    [Theory]
    [InlineData("0x708ba", 0x708baUL)]
    [InlineData("708ba", 0x708baUL)]
    [InlineData("", 0UL)]
    [InlineData("nope", 0UL)]
    public void Addresses_read_with_or_without_0x(string text, ulong expected)
    {
        Assert.Equal(expected, WinlandProtocol.ParseAddress(text));
    }

    [Fact]
    public void Unnumbered_workspaces_get_the_ids_Winland_gives_them_in_events()
    {
        var ids = WinlandProtocol.UnnumberedIds(["1", "special:special", "special:b", "special:a", "zeta", "alpha", "2"]);
        Assert.Equal(-99, ids["special:special"]);
        Assert.Equal(-98, ids["special:a"]);
        Assert.Equal(-97, ids["special:b"]);
        Assert.Equal(-1, ids["alpha"]);
        Assert.Equal(-2, ids["zeta"]);
        Assert.False(ids.ContainsKey("1"));
    }

    [Theory]
    [InlineData("4", 4)]
    [InlineData("special:special", -99)]
    [InlineData("special:scratch", -1)]
    public void A_workspace_known_by_name_has_its_number_as_id(string name, int id)
    {
        Assert.Equal(id, WinlandProtocol.IdFromName(name));
    }

    [Fact]
    public async Task A_short_reply_is_one_line()
    {
        var reader = new WinlandReader(Stream("ok\n"));
        Assert.Equal("ok", await reader.ReadReplyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_reply_with_newlines_is_framed_by_its_length()
    {
        var body = "1\n\twindows: 2\nü";
        var reader = new WinlandReader(Stream($"#{Encoding.UTF8.GetByteCount(body)}\n{body}"));
        Assert.Equal(body, await reader.ReadReplyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_event_stream_reads_line_by_line()
    {
        var reader = new WinlandReader(Stream("openwindow>>a,1,M,c,t\r\nclosewindow>>a\nlast"));
        Assert.Equal("openwindow>>a,1,M,c,t", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal("closewindow>>a", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal("last", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Null(await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Json_becomes_maps_and_lists_for_qml()
    {
        // A monitors entry as Winland 0.3 writes it.
        using var doc = JsonDocument.Parse("""
            {"id":0,"name":"\\\\.\\DISPLAY2","scale":1.25,"focused":true,"reserved":[0,0,0,48],
             "activeWorkspace":{"address":"1","id":1,"type":"normal","name":"1","monitor":"\\\\.\\DISPLAY2"},
             "specialWorkspace":{"address":"","type":"","name":"","monitor":""},"solitaryBlockedBy":null}
            """);
        var map = Assert.IsType<Dictionary<string, object?>>(WinlandProtocol.ToQml(doc.RootElement));
        Assert.Equal(0, map["id"]);
        Assert.Equal(@"\\.\DISPLAY2", map["name"]);
        Assert.Equal(1.25, map["scale"]);
        Assert.Equal(true, map["focused"]);
        Assert.Equal([0, 0, 0, 48], Assert.IsType<List<object?>>(map["reserved"]));
        Assert.Equal("1", Assert.IsType<Dictionary<string, object?>>(map["activeWorkspace"])["name"]);
        Assert.Null(map["solitaryBlockedBy"]);
        Assert.Null(WinlandProtocol.Int(doc.RootElement.GetProperty("specialWorkspace"), "id"));
    }

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}
