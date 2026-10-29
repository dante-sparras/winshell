using Winshell.Runtime;
using Xunit;

namespace Winshell.Runtime.Tests;

public class InlineDocumentTests
{
    [Fact]
    public void Creating_the_same_qml_object_twice_does_not_add_a_loaded_file()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property var first
              property var second
              property var third
              Component.onCompleted: {
                var text = 'import QtQml; QtObject { objectName: "one" }'
                first = Qt.createQmlObject(text, null)
                second = Qt.createQmlObject(text, null)
                third = Qt.createQmlObject('import QtQml; QtObject { objectName: "two" }', null)
              }
            }
            """));
        var root = shell.Load();
        var first = (QObject)root.ReadProperty("first")!;
        var second = (QObject)root.ReadProperty("second")!;
        var third = (QObject)root.ReadProperty("third")!;
        Assert.Equal("one", first.ObjectName);
        Assert.Equal("one", second.ObjectName);
        Assert.Equal("two", third.ObjectName);
        Assert.DoesNotContain(shell.Engine.LoadedFiles, path => path.Contains("__inline_", StringComparison.Ordinal));
        Assert.Empty(shell.Messages);
    }
}
