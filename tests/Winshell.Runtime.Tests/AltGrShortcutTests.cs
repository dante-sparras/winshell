using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class AltGrShortcutTests
{
    [Fact]
    public void Ctrl_alt_does_not_run_editing_shortcuts()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            TextInput { text: "hello" }
            """));
        using var _ = shell;
        var field = (TextInput)shell.Load();
        field.KeyPress(new KeyEvent { Key = 'A', Modifiers = QtEnums.ControlModifier | QtEnums.AltModifier });
        Assert.Equal("hello", field.Text_);
        Assert.Equal(field.CursorPosition, field.SelectionStart);
        Assert.Equal(field.CursorPosition, field.SelectionEnd);

        field.KeyPress(new KeyEvent { Key = 'A', Modifiers = QtEnums.ControlModifier });
        Assert.Equal(0, field.SelectionStart);
        Assert.Equal(5, field.SelectionEnd);

        field.KeyPress(new KeyEvent { Key = 'Z', Modifiers = QtEnums.ControlModifier | QtEnums.AltModifier });
        Assert.Equal("hello", field.Text_);
    }
}
