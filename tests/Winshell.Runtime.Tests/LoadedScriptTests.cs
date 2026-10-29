namespace Winshell.Runtime.Tests;

public class LoadedScriptTests
{
    [Fact]
    public void An_imported_script_is_a_loaded_file()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nimport \"lib/util.js\" as Util\nQtObject { objectName: Util.name() }"),
            ("lib/util.js", "function name() { return \"util\" }"));

        var root = shell.Load();

        Assert.Equal("util", root.ObjectName);
        Assert.Contains(Path.GetFullPath(Path.Combine(shell.Directory, "lib", "util.js")), shell.Engine.LoadedFiles, StringComparer.OrdinalIgnoreCase);
    }
}
