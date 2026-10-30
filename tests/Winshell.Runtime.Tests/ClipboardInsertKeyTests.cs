using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class ClipboardInsertKeyTests
{
    private const int Insert = 0x01000006;
    private const int Delete = 0x01000007;

    private sealed class FakeClipboard : IClipboard
    {
        public event Action? Changed { add { } remove { } }

        public string? Text { get; set; }

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }

    private static (TestShell Shell, TextInput Field, FakeClipboard Clipboard) Load(string text)
    {
        var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            TextInput { text: "{{text}}" }
            """));
        var clipboard = new FakeClipboard();
        shell.Engine.AddService<IClipboard>(clipboard);
        return (shell, (TextInput)shell.Load(), clipboard);
    }

    [Fact]
    public void Ctrl_insert_copies_the_selection()
    {
        var (shell, field, clipboard) = Load("hello");
        using var _ = shell;
        field.SelectAll();

        field.KeyPress(new KeyEvent { Key = Insert, Modifiers = QtEnums.ControlModifier });

        Assert.Equal("hello", clipboard.Text);
        Assert.Equal("hello", field.Text_);
    }

    [Fact]
    public void Shift_delete_cuts_the_selection()
    {
        var (shell, field, clipboard) = Load("hello");
        using var _ = shell;
        field.SelectAll();

        field.KeyPress(new KeyEvent { Key = Delete, Modifiers = QtEnums.ShiftModifier });

        Assert.Equal("hello", clipboard.Text);
        Assert.Equal("", field.Text_);
    }

    [Fact]
    public void Shift_insert_pastes_at_the_cursor()
    {
        var (shell, field, clipboard) = Load("ab");
        using var _ = shell;
        clipboard.Text = "X";
        field.CursorPosition = 1;

        field.KeyPress(new KeyEvent { Key = Insert, Modifiers = QtEnums.ShiftModifier });

        Assert.Equal("aXb", field.Text_);
    }

    [Fact]
    public void Delete_without_shift_still_deletes_without_copying()
    {
        var (shell, field, clipboard) = Load("hello");
        using var _ = shell;
        field.SelectAll();

        field.KeyPress(new KeyEvent { Key = Delete });

        Assert.Null(clipboard.Text);
        Assert.Equal("", field.Text_);
    }
}
