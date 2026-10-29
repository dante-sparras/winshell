using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class TextInputUndoTests
{
    [Fact]
    public void Undo_after_select_all_keeps_the_selection_inside_the_text()
    {
        var (field, shell) = Field();
        using var _ = shell;
        field.InsertText("a");
        field.InsertText("b");
        field.InsertText("c");
        field.SetComposition("っ");
        field.SelectAll();
        var lengthNoted = false;
        var undoNoted = false;
        field.Connect("lengthChanged", _ => lengthNoted = true);
        field.Connect("canUndoChanged", _ => undoNoted = true);

        field.Undo();

        Assert.Equal("ab", field.Text_);
        Assert.Equal("", field.CompositionText);
        Assert.Equal(field.CursorPosition, field.SelectionStart);
        Assert.Equal(field.CursorPosition, field.SelectionEnd);
        Assert.True(lengthNoted);
        Assert.True(undoNoted);
        Assert.Equal("", field.SelectedText);
        field.Copy();
        field.KeyPress(new KeyEvent { Key = 0x01000003 });
        Assert.Equal("a", field.Text_);
    }

    [Fact]
    public void Undo_does_nothing_while_read_only()
    {
        var (field, shell) = Field();
        using var _ = shell;
        field.InsertText("ab");
        field.ReadOnly = true;
        field.Undo();
        Assert.Equal("ab", field.Text_);
        Assert.True(field.CanUndo);
    }

    private static (TextInput Field, TestShell Shell) Field()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            TextInput { objectName: "field"; text: "" }
            """));
        var root = (TextInput)shell.Load();
        return (root, shell);
    }
}
