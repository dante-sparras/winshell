namespace Winshell.Runtime.Tests;

public class QmldirTests
{
    [Fact]
    public void Tab_separated_and_internal_types_are_read_and_unknown_lines_are_reported()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nQtObject { property QtObject a: Fancy {}\n property QtObject b: Helper {} }"),
            ("qmldir", "module Mine\nFancy\t1.0\tFancyImpl.qml\ninternal Helper HelperImpl.qml\nfancy-typo here\n"),
            ("FancyImpl.qml", "import QtQml\nQtObject { objectName: \"fancy\" }"),
            ("HelperImpl.qml", "import QtQml\nQtObject { objectName: \"helper\" }"));

        var root = shell.Load();

        Assert.Equal("fancy", ((QObject)root.ReadProperty("a")!).ObjectName);
        Assert.Equal("helper", ((QObject)root.ReadProperty("b")!).ObjectName);
        var warning = Assert.Single(shell.Messages, m => m.Contains("qmldir", StringComparison.Ordinal));
        Assert.Contains("qmldir:4: qmldir line is not understood and is ignored: fancy-typo here", warning, StringComparison.Ordinal);
    }
}
