namespace Winshell.Runtime.Tests;

/// <summary>
/// A JavaScript file without <c>.pragma library</c> belongs to the component that imported it.
/// A <c>.import</c> that is not a JavaScript file fails the load.
/// </summary>
public class ScriptScopeTests
{
    [Fact]
    public void A_script_sees_the_component_that_imported_it()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                import "util.js" as Util
                QtObject {
                  id: root
                  property int w: 7
                  property int v: Util.wide()
                }
                """),
            ("util.js", "function wide() { return root.w }\n"));
        var root = shell.Load();

        Assert.Equal(7, root.ReadProperty("v"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Each_component_gets_its_own_script()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                QtObject {
                  property QtObject a: Piece { }
                  property QtObject b: Piece { }
                }
                """),
            ("Piece.qml", """
                import QtQml
                import "util.js" as Util
                QtObject {
                  property int n: 0
                  Component.onCompleted: n = Util.bump()
                }
                """),
            ("util.js", "var hits = 0\nfunction bump() { hits = hits + 1; return hits }\n"));
        var root = shell.Load();

        var a = (QObject)root.ReadProperty("a")!;
        var b = (QObject)root.ReadProperty("b")!;
        Assert.Equal(1, a.ReadProperty("n"));
        Assert.Equal(1, b.ReadProperty("n"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void An_imported_script_sees_the_same_component()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                import "util.js" as Util
                QtObject {
                  id: root
                  property int w: 7
                  property int v: Util.wide()
                }
                """),
            ("util.js", ".import \"inner.js\" as Inner\nfunction wide() { return Inner.wide() }\n"),
            ("inner.js", "function wide() { return root.w }\n"));
        var root = shell.Load();

        Assert.Equal(7, root.ReadProperty("v"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_library_does_not_see_the_component()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                import "util.js" as Util
                QtObject {
                  id: root
                  property int w: 7
                  property int v: Util.wide()
                }
                """),
            ("util.js", ".pragma library\nfunction wide() { return root.w }\n"));
        var root = shell.Load();

        Assert.Equal(0, root.ReadProperty("v"));
        Assert.Contains(shell.Messages, m => m.Contains("ReferenceError", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pragma_that_only_mentions_library_is_not_shared()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                import "util.js" as Util
                QtObject {
                  id: root
                  property int w: 7
                  property int v: Util.wide()
                }
                """),
            ("util.js", ".pragma use library\nfunction wide() { return root.w }\n"));
        var root = shell.Load();

        Assert.Equal(7, root.ReadProperty("v"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_quoted_import_path_may_contain_a_space()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                import "util.js" as Util
                QtObject { property int v: Util.value() }
                """),
            ("util.js", ".pragma library\n.import \"my file.js\" as Other\nfunction value() { return Other.n }\n"),
            ("my file.js", ".pragma library\nvar n = 4\n"));
        var root = shell.Load();

        Assert.Equal(4, root.ReadProperty("v"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_module_import_fails_the_load()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nimport \"bad.js\" as Bad\nQtObject { property int v: Bad.n }"),
            ("bad.js", ".import QtQuick 2.0 as Q\nvar n = 1\n"));

        var error = Assert.Throws<QmlException>(() => shell.Engine.LoadScript(Path.Combine(shell.Directory, "bad.js")));

        Assert.Contains("not a javascript file", error.Message, StringComparison.Ordinal);
        Assert.Contains("QtQuick", error.Message, StringComparison.Ordinal);
    }
}
