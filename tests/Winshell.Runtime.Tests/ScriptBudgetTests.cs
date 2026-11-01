using Jint.Native;

namespace Winshell.Runtime.Tests;

/// <summary>A script that never returns is stopped, and the engine keeps running.</summary>
public class ScriptBudgetTests
{
    [Fact]
    public async Task A_runaway_binding_is_stopped_and_logged()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              id: root
              property int v: runaway()
              property string after: "ready"
              function runaway() { while (true) {} return 1 }
            }
            """));
        using var engine = new QmlEngine(StandardModules.Create(), shell.Loop, shell.Log, shell.Directory, TimeSpan.FromMilliseconds(200));
        var load = Task.Run(() => engine.CreateRoot(Path.Combine(shell.Directory, "shell.qml")));
        var root = await load.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        shell.Loop.Process();

        Assert.Contains(shell.Messages, m => m.Contains("TimeoutException", StringComparison.Ordinal) && m.Contains("shell.qml", StringComparison.Ordinal));
        Assert.Equal(0, root.ReadProperty("v"));
        Assert.Equal("ready", root.ReadProperty("after"));
        var fn = engine.Js.Compile("return ('still');", [], new ScopeObject(engine.Js, root, root.CreationContext!), "test");
        Assert.Equal("still", ValueConverter.FromJs(engine.Js.Engine.Call(fn, JsValue.Undefined, [])));
        Assert.False(root.IsDestroyed);
    }
}
