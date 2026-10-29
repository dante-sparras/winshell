namespace Winshell.Runtime.Tests;

public class RequiredAliasTests
{
    private const string Labeled = """
        import QtQml
        QtObject {
          required property alias label: inner.text
          property QtObject holder: QtObject { id: inner; property string text: "" }
        }
        """;

    [Fact]
    public void A_required_alias_that_is_not_set_is_reported()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nQtObject { property Labeled l: Labeled {} }"),
            ("Labeled.qml", Labeled));
        shell.Load();
        Assert.Contains(shell.Messages, m => m.Contains("Required property label was not initialized", StringComparison.Ordinal));
    }

    [Fact]
    public void A_required_alias_that_is_set_is_not_reported()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nQtObject { property Labeled l: Labeled { label: \"hi\" } }"),
            ("Labeled.qml", Labeled));
        var root = shell.Load();
        var labeled = (QObject)root.ReadProperty("l")!;
        Assert.Equal("hi", labeled.ReadProperty("label"));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("Required property", StringComparison.Ordinal));
    }
}
