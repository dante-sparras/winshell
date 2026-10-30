using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class TextInputRedoTests
{
    private sealed class FakeClipboard : IClipboard
    {
        public event Action? Changed { add { } remove { } }

        public string? Text { get; set; }

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }

    private const int Ctrl = QtEnums.ControlModifier;
    private const int Shift = QtEnums.ShiftModifier;
    private const int Alt = QtEnums.AltModifier;
    private const int Insert = 0x01000006;
    private const int Delete = 0x01000007;

    [Fact]
    public void Setting_text_from_code_clears_the_undo_and_redo_history()
    {
        var (field, _, shell) = Field();
        using var _s = shell;
        field.InsertText("a");
        field.InsertText("b");
        field.Undo();
        Assert.True(field.CanUndo);
        Assert.True(field.CanRedo);

        field.Text_ = "code";

        Assert.False(field.CanUndo);
        Assert.False(field.CanRedo);
        field.Undo();
        field.Redo();
        Assert.Equal("code", field.Text_);
    }

    [Fact]
    public void Redo_restores_what_undo_took_back()
    {
        var (field, _, shell) = Field();
        using var _s = shell;
        field.InsertText("ab");
        field.InsertText("c");

        field.Undo();
        field.Undo();
        Assert.Equal("", field.Text_);

        field.Redo();
        Assert.Equal("ab", field.Text_);
        Assert.Equal(2, field.CursorPosition);
        field.Redo();
        Assert.Equal("abc", field.Text_);
        Assert.False(field.CanRedo);
        field.Undo();
        Assert.Equal("ab", field.Text_);
    }

    [Fact]
    public void A_new_edit_after_undo_clears_redo()
    {
        var (field, _, shell) = Field();
        using var _s = shell;
        field.InsertText("a");
        field.Undo();
        Assert.True(field.CanRedo);

        field.InsertText("b");

        Assert.False(field.CanRedo);
        field.Redo();
        Assert.Equal("b", field.Text_);
    }

    [Fact]
    public void Redo_does_nothing_while_read_only()
    {
        var (field, _, shell) = Field();
        using var _s = shell;
        field.InsertText("a");
        field.Undo();
        field.ReadOnly = true;

        field.Redo();

        Assert.Equal("", field.Text_);
        Assert.True(field.CanRedo);
    }

    [Theory]
    [InlineData('Y', Ctrl)]
    [InlineData('Z', Ctrl | Shift)]
    public void Redo_shortcuts_redo(int key, int modifiers)
    {
        var (field, _, shell) = Field();
        using var _s = shell;
        field.InsertText("a");
        field.KeyPress(new KeyEvent { Key = 'Z', Modifiers = Ctrl });
        Assert.Equal("", field.Text_);

        Assert.True(field.KeyPress(new KeyEvent { Key = key, Modifiers = modifiers }));

        Assert.Equal("a", field.Text_);
    }

    [Fact]
    public void AltGr_y_is_not_redo()
    {
        var (field, _, shell) = Field();
        using var _s = shell;
        field.InsertText("a");
        field.Undo();

        Assert.False(field.KeyPress(new KeyEvent { Key = 'Y', Modifiers = Ctrl | Alt }));

        Assert.Equal("", field.Text_);
    }

    [Fact]
    public void CanUndo_and_canRedo_notify_when_they_change()
    {
        var (field, _, shell) = Field();
        using var _s = shell;
        var undo = 0;
        var redo = 0;
        field.Connect("canUndoChanged", _ => undo++);
        field.Connect("canRedoChanged", _ => redo++);

        field.InsertText("a");
        Assert.Equal(1, undo);

        field.Undo();
        Assert.False(field.CanUndo);
        Assert.True(field.CanRedo);
        Assert.Equal(2, undo);
        Assert.Equal(1, redo);

        field.Redo();
        Assert.True(field.CanUndo);
        Assert.False(field.CanRedo);
        Assert.Equal(3, undo);
        Assert.Equal(2, redo);
    }

    [Fact]
    public void Ctrl_insert_copies_and_shift_insert_pastes()
    {
        var (field, clip, shell) = Field();
        using var _s = shell;
        field.InsertText("abc");
        field.Select(0, 2);

        Assert.True(field.KeyPress(new KeyEvent { Key = Insert, Modifiers = Ctrl }));
        Assert.Equal("ab", clip.Text);

        field.Select(3, 3);
        Assert.True(field.KeyPress(new KeyEvent { Key = Insert, Modifiers = Shift }));
        Assert.Equal("abcab", field.Text_);
    }

    [Fact]
    public void Shift_delete_cuts_the_selection()
    {
        var (field, clip, shell) = Field();
        using var _s = shell;
        field.InsertText("abc");
        field.Select(1, 3);

        Assert.True(field.KeyPress(new KeyEvent { Key = Delete, Modifiers = Shift }));

        Assert.Equal("a", field.Text_);
        Assert.Equal("bc", clip.Text);
    }

    private static (TextInput Field, FakeClipboard Clip, TestShell Shell) Field()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            TextInput { objectName: "field"; text: "" }
            """));
        var clip = new FakeClipboard();
        shell.Engine.AddService<IClipboard>(clip);
        var root = (TextInput)shell.Load();
        return (root, clip, shell);
    }
}
