using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Script values are the QML value types. Another CLR object is undefined, and the log says so once.</summary>
public class ScriptValueTests
{
    private sealed class UnscriptedProbe(int n)
    {
        public int N { get; } = n;
    }

    [Fact]
    public void A_color_keeps_its_fields_and_another_clr_object_is_undefined()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item { }
            """, out var shell);
        using var _ = shell;
        Assert.Equal(0.25, shell.Eval(root, "Qt.rgba(0.25, 0.5, 0.75, 1).r"));
        Assert.Equal(4d, shell.Eval(root, "Qt.point(3, 4).y"));
        Assert.Equal(8d, shell.Eval(root, "Qt.rect(0, 0, 8, 9).width"));
        Assert.Equal(2d, shell.Eval(root, "Qt.size(2, 3).width"));
        Assert.IsType<AnchorLine>(ValueConverter.FromJs(shell.Engine.Js.ToJs(new AnchorLine((Item)root, AnchorEdge.Right))));

        Assert.Null(ValueConverter.FromJs(shell.Engine.Js.ToJs(new UnscriptedProbe(9))));
        Assert.Contains("Warn script: UnscriptedProbe is not a script value, so it is undefined", shell.Messages);
        Assert.Null(ValueConverter.FromJs(shell.Engine.Js.ToJs(new UnscriptedProbe(1))));
        Assert.Equal(1, shell.Messages.Count(m => m.Contains("UnscriptedProbe", StringComparison.Ordinal)));
    }
}
