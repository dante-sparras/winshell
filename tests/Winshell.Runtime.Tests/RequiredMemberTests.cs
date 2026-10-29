namespace Winshell.Runtime.Tests;

public class RequiredMemberTests
{
    [Fact]
    public void Required_name_warns_when_the_inherited_property_is_unset()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nQtObject { property Child c: Child { } }"),
            ("Base.qml", "import QtQml\nQtObject { property string label }"),
            ("Child.qml", "import QtQml\nBase { required label }"));
        shell.Load();
        Assert.Contains(shell.Messages, m => m.Contains("Required property label was not initialized", StringComparison.Ordinal));
    }

    [Fact]
    public void Required_name_is_quiet_when_the_property_is_set()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nQtObject { property Child c: Child { label: \"hi\" } }"),
            ("Base.qml", "import QtQml\nQtObject { property string label }"),
            ("Child.qml", "import QtQml\nBase { required label }"));
        var root = shell.Load();
        var child = (QObject)root.ReadProperty("c")!;
        Assert.Equal("hi", child.ReadProperty("label"));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("Required property", StringComparison.Ordinal));
    }
}
