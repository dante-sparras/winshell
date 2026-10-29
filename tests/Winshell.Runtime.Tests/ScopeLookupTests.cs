namespace Winshell.Runtime.Tests;

/// <summary>
/// Asking whether a scope has a name must not read the value, and must not load a script import. A property that
/// uses a global's name, such as <c>console</c>, is still that property.
/// </summary>
public class ScopeLookupTests
{
    [Fact]
    public void HasProperty_does_not_read_the_value()
    {
        using var shell = new TestShell(("shell.qml", "import QtQml\nQtObject { property string console: 'mine' }"));
        var root = shell.Load();
        var counter = new Counting { Engine = shell.Engine };
        var context = new ComponentContext(root.CreationContext, root.CreationContext!.Imports);
        context.ContextObject = counter;
        var scope = new ScopeObject(shell.Engine.Js, root, context);

        Assert.True(scope.HasProperty("value"));
        Assert.True(scope.HasProperty("console"));
        Assert.True(scope.HasProperty("QtObject"));
        Assert.False(scope.HasProperty("Math"));
        Assert.False(scope.HasProperty("parseInt"));
        Assert.False(scope.HasProperty("Math"));
        Assert.Equal(0, counter.Reads);

        _ = scope.Get("value");
        Assert.Equal(1, counter.Reads);
    }

    [Fact]
    public void HasProperty_does_not_load_a_script_import()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                import "util.js" as Util
                QtObject { }
                """),
            ("util.js", "throw new Error('loaded');\n"));
        var root = shell.Load();
        var scope = new ScopeObject(shell.Engine.Js, root, root.CreationContext!);

        Assert.True(scope.HasProperty("Util"));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("loaded", StringComparison.Ordinal));
    }

    /// <summary>A property whose getter counts, so a membership check that reads the value fails the test.</summary>
    private sealed class Counting : QObject
    {
        public int Reads;

        [Qml]
        public int Value
        {
            get
            {
                Reads++;
                return Reads;
            }
            set { }
        }
    }
}
