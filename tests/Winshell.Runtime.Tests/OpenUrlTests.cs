namespace Winshell.Runtime.Tests;

/// <summary><c>Qt.openUrlExternally</c> reports whether the platform accepted the string.</summary>
public class OpenUrlTests
{
    [Fact]
    public void Open_url_returns_whether_the_platform_accepted_it()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property bool opened: false
              property bool rejected: false
              property bool missing: Qt.openUrlExternally()
              Component.onCompleted: {
                opened = Qt.openUrlExternally("https://example.com/a");
                rejected = Qt.openUrlExternally("notes.txt");
              }
            }
            """));
        var urls = new RecordingUrls();
        shell.Engine.AddService<IPlatformServices>(urls);
        var root = shell.Load();

        Assert.Equal(true, root.ReadProperty("opened"));
        Assert.Equal(false, root.ReadProperty("rejected"));
        Assert.Equal(false, root.ReadProperty("missing"));
        Assert.Equal(["https://example.com/a", "notes.txt"], urls.Opened);
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Open_url_without_a_platform_returns_false()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject { property bool ok: Qt.openUrlExternally("https://example.com") }
            """));
        var root = shell.Load();

        Assert.Equal(false, root.ReadProperty("ok"));
        Assert.Empty(shell.Messages);
    }

    private sealed class RecordingUrls : IPlatformServices
    {
        public List<string> Opened { get; } = [];

        public bool OpenUrl(string url)
        {
            Opened.Add(url);
            return url.StartsWith("https://", StringComparison.Ordinal);
        }

        public void Quit(int code)
        {
        }
    }
}
