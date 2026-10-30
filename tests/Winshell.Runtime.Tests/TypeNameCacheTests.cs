using Winshell.Runtime;
using Xunit;

namespace Winshell.Runtime.Tests;

public class TypeNameCacheTests
{
    [Fact]
    public void A_type_or_namespace_name_in_a_script_is_the_same_object_each_time()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQuick
                import QtQuick as Q
                Item {
                  function textType() { return Text }
                  function paletteType() { return Palette }
                  function namespaced() { return Q }
                  function namespacedText() { return Q.Text }
                }
                """),
            ("Palette.qml", """
                import QtQuick
                Item {
                  enum Tone { Light, Dark }
                }
                """));
        var root = shell.Load();

        Assert.Equal(true, shell.Eval(root, "textType() === textType()"));
        Assert.Equal(true, shell.Eval(root, "paletteType() === paletteType()"));
        Assert.Equal(true, shell.Eval(root, "namespaced() === namespaced()"));
        Assert.Equal(true, shell.Eval(root, "namespacedText() === namespacedText()"));
        Assert.Equal(1.0, shell.Eval(root, "Palette.Dark"));
        Assert.Equal(1.0, shell.Eval(root, "Palette.Tone.Dark"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_type_with_an_attached_object_still_reads_its_own_scope_objects_attached_values()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            import QtQuick.Layouts
            Item {
              Item { id: a; Layout.fillWidth: true; property bool fill: Layout.fillWidth }
              Item { id: b; Layout.fillWidth: false; property bool fill: Layout.fillWidth }
            }
            """));
        var root = shell.Load();

        Assert.Equal(true, shell.Eval(root, "a.fill"));
        Assert.Equal(false, shell.Eval(root, "b.fill"));
        Assert.Empty(shell.Messages);
    }
}
