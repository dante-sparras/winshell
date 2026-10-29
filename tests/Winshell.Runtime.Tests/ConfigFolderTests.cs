namespace Winshell.Runtime.Tests;

/// <summary>
/// A configuration loads QML and JavaScript only from its own folder. An import, <c>Qt.createComponent</c> or a
/// <c>Loader</c> that names a file outside it fails like a file that cannot be read.
/// </summary>
public sealed class ConfigFolderTests : IDisposable
{
    // A folder next to the shell's, with a type and a script the shell must not load.
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public ConfigFolderTests()
    {
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "Evil.qml"), "import QtQml\nQtObject { property int v: 7 }\n");
        File.WriteAllText(Path.Combine(_outside, "evil.js"), "var v = 7\n");
    }

    public void Dispose() => Directory.Delete(_outside, true);

    private string Outside(string name) => Path.Combine(_outside, name).Replace('\\', '/');

    [Fact]
    public void Relative_imports_inside_the_folder_load()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nimport \"widgets\"\nimport \"lib/util.js\" as Util\nQtObject { property int v: Util.n + label.v\n property var label: Label {} }"),
            ("widgets/Label.qml", "import QtQml\nimport \"../lib/util.js\" as Util\nQtObject { property int v: Util.n }"),
            ("lib/util.js", "var n = 2\n"));

        var root = shell.Load();

        Assert.Equal(4, root.ReadProperty("v"));
    }

    [Fact]
    public void A_folder_import_by_absolute_path_outside_is_refused()
    {
        using var shell = new TestShell(("shell.qml", $"import QtQml\nimport \"{Outside("")}\"\nEvil {{}}"));

        var error = Assert.Throws<QmlCompileException>(() => shell.Load());

        Assert.Contains("outside the configuration folder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_import_by_absolute_path_outside_is_refused()
    {
        using var shell = new TestShell(("shell.qml", $"import QtQml\nimport \"{Outside("evil.js")}\" as Evil\nQtObject {{ property int v: Evil.v }}"));

        var error = Assert.Throws<QmlCompileException>(() => shell.Load());

        Assert.Contains("outside the configuration folder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_import_that_climbs_out_with_dots_is_refused()
    {
        var sibling = Path.GetFileName(_outside);
        using var shell = new TestShell(("shell.qml", $"import QtQml\nimport \"../{sibling}\"\nEvil {{}}"));

        var error = Assert.Throws<QmlCompileException>(() => shell.Load());

        Assert.Contains("outside the configuration folder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_whose_name_starts_with_the_shell_folder_is_outside()
    {
        using var shell = new TestShell(("shell.qml", "import QtQml\nQtObject {}"));
        var lookalike = shell.Directory + "-evil";

        Assert.False(shell.Engine.IsInShellDir(Path.Combine(lookalike, "Evil.qml")));
        Assert.True(shell.Engine.IsInShellDir(Path.Combine(shell.Directory, "widgets", "..", "Bar.qml")));
        Assert.True(shell.Engine.IsInShellDir(shell.Directory.ToUpperInvariant()));
    }

    [Fact]
    public void A_unc_import_is_refused_before_the_share_is_read()
    {
        // .invalid never resolves. The import fails on its path, without a lookup.
        using var shell = new TestShell(("shell.qml", "import QtQml\nimport \"//winshell.invalid/share/a.js\" as A\nQtObject { property int v: A.v }"));

        var error = Assert.Throws<QmlCompileException>(() => shell.Load());

        Assert.Contains("outside the configuration folder", error.Message, StringComparison.Ordinal);
        Assert.False(shell.Engine.IsInShellDir(@"\\winshell.invalid\share\Evil.qml"));
    }

    [Fact]
    public void A_javascript_import_outside_the_folder_is_refused()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nQtObject {}"),
            ("lib.js", $".import \"{Outside("evil.js")}\" as Evil\nvar v = Evil.v\n"));

        var error = Assert.Throws<QmlException>(() => shell.Engine.LoadScript(Path.Combine(shell.Directory, "lib.js")));

        Assert.Contains("outside the configuration folder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void createComponent_with_a_file_url_outside_the_folder_is_an_error_component()
    {
        using var shell = new TestShell(("shell.qml", $$"""
            import QtQml
            QtObject {
              property var component: Qt.createComponent("file:///{{Outside("Evil.qml")}}")
              property int status: component.status
              property string error: component.errorString()
            }
            """));

        var root = shell.Load();

        Assert.Equal(3, root.ReadProperty("status"));
        Assert.Contains("outside the configuration folder", (string)root.ReadProperty("error")!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loader_source_outside_the_folder_is_an_error()
    {
        using var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            Item {
              Loader { id: loader; source: "{{Outside("Evil.qml")}}" }
              property int status: loader.status
            }
            """));

        var root = shell.Load();

        Assert.Equal(3, root.ReadProperty("status"));
        Assert.Contains(shell.Messages, m => m.Contains("outside the configuration folder", StringComparison.Ordinal));
    }
}
