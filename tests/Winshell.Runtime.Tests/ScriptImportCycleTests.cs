namespace Winshell.Runtime.Tests;

/// <summary>
/// A JavaScript <c>.import</c> cycle used to recurse in C# until the process died. The load fails with a
/// <see cref="QmlException"/> that names the files instead.
/// </summary>
public class ScriptImportCycleTests
{
    [Fact]
    public void Two_libraries_that_import_each_other_fail_with_the_cycle()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nimport \"a.js\" as A\nQtObject { property int v: A.n }"),
            ("a.js", ".pragma library\n.import \"b.js\" as B\nvar n = B.n\n"),
            ("b.js", ".pragma library\n.import \"a.js\" as A\nvar n = A.n\n"));

        var error = Assert.Throws<QmlException>(() => shell.Engine.LoadScript(Path.Combine(shell.Directory, "a.js")));

        Assert.Contains("imports itself through", error.Message, StringComparison.Ordinal);
        Assert.Contains("a.js", error.Message, StringComparison.Ordinal);
        Assert.Contains("b.js", error.Message, StringComparison.Ordinal);
        // The same cycle through a binding must not take the process down.
        var root = shell.Load();
        Assert.NotNull(root);
        Assert.Contains(shell.Messages, m => m.Contains("imports itself through", StringComparison.Ordinal));
    }

    [Fact]
    public void A_library_that_imports_itself_fails_with_the_cycle()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nimport \"a.js\" as A\nQtObject { property int v: A.n }"),
            ("a.js", ".pragma library\n.import \"a.js\" as Me\nvar n = 1\n"));

        var error = Assert.Throws<QmlException>(() => shell.Engine.LoadScript(Path.Combine(shell.Directory, "a.js")));

        Assert.Contains("imports itself through", error.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(shell.Directory, "a.js"), error.Message, StringComparison.Ordinal);
    }
}
