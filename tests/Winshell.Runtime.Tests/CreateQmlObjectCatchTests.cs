namespace Winshell.Runtime.Tests;

/// <summary>
/// <c>Qt.createQmlObject</c> throws a script error a <c>try/catch</c> can handle. <c>Qt.createComponent</c>
/// reports a file that cannot be read as an error component, the same as a missing file.
/// </summary>
public class CreateQmlObjectCatchTests
{
    [Fact]
    public void A_script_can_catch_a_bad_createQmlObject()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              property bool caught: false
              property string made: ""
              Component.onCompleted: {
                try {
                  var o = Qt.createQmlObject("import QtQml\nQtObject { objectName: 'made' }", this)
                  made = o.objectName
                } catch (e) {
                  made = "threw"
                }
                try { Qt.createQmlObject("bad") } catch (e) { caught = true }
              }
            }
            """, out var shell);
        using var _ = shell;
        Assert.Equal("made", root.ReadProperty("made"));
        Assert.True((bool)root.ReadProperty("caught")!);
    }

    [Fact]
    public void createComponent_reports_a_file_the_account_cannot_read()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property int status: Qt.createComponent("secret.qml").status
              property string error: Qt.createComponent("secret.qml").errorString()
            }
            """));
        // Reading a directory throws UnauthorizedAccessException, which used to escape the helper.
        Directory.CreateDirectory(Path.Combine(shell.Directory, "secret.qml"));
        var root = shell.Load();
        Assert.Equal(3, root.ReadProperty("status"));
        Assert.False(string.IsNullOrEmpty((string)root.ReadProperty("error")!));
    }
}
