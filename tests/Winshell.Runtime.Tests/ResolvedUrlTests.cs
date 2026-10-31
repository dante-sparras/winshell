namespace Winshell.Runtime.Tests;

/// <summary>Qt.resolvedUrl and Qt.createComponent use the directory of the file that called them.</summary>
public class ResolvedUrlTests
{
    [Fact]
    public void A_relative_url_and_component_resolve_next_to_the_caller()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                import "./widgets"
                Panel {}
                """),
            ("Piece.qml", """
                import QtQml
                QtObject { property string from: "root" }
                """),
            ("widgets/Panel.qml", """
                import QtQml
                QtObject {
                  property string url: Qt.resolvedUrl("icon.svg")
                  property string web: Qt.resolvedUrl("https://example.com/a")
                  property string from: Qt.createComponent("Piece.qml").createObject().from
                }
                """),
            ("widgets/Piece.qml", """
                import QtQml
                QtObject { property string from: "widgets" }
                """));
        var root = shell.Load();

        var url = Assert.IsType<string>(root.ReadProperty("url"));
        Assert.EndsWith(Path.Combine("widgets", "icon.svg"), url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://example.com/a", root.ReadProperty("web"));
        Assert.Equal("widgets", root.ReadProperty("from"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_url_written_where_a_type_is_used_resolves_next_to_that_file()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                import "widgets"
                Bar {
                  icon: Qt.resolvedUrl("assets/x.svg")
                  Component.onCompleted: handled = Qt.resolvedUrl("assets/y.svg")
                }
                """),
            ("widgets/Bar.qml", """
                import QtQml
                QtObject {
                  property string icon
                  property string handled
                  property string own: Qt.resolvedUrl("own.svg")
                }
                """));
        var root = shell.Load();

        Assert.Equal(Path.Combine(shell.Directory, "assets", "x.svg"), root.ReadProperty("icon") as string, ignoreCase: true);
        Assert.Equal(Path.Combine(shell.Directory, "assets", "y.svg"), root.ReadProperty("handled") as string, ignoreCase: true);
        Assert.Equal(Path.Combine(shell.Directory, "widgets", "own.svg"), root.ReadProperty("own") as string, ignoreCase: true);
        Assert.Empty(shell.Messages);
    }
}
