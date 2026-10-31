using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Qt 6.10 behavior for the text members that used to load and then do nothing.</summary>
public class QtTextMemberTests
{
    [Fact]
    public void A_run_of_typing_is_one_undo_step()
    {
        var (field, scene, shell) = Show("width: 80");
        using var _ = shell;
        Assert.Same(field, scene.FocusItem);

        scene.TextInput("a");
        scene.TextInput("b");
        scene.TextInput("c");
        field.Undo();

        Assert.Equal("", field.Text_);
        field.Redo();
        Assert.Equal("abc", field.Text_);

        field.KeyPress(new KeyEvent { Key = 0x01000012 });
        scene.TextInput("Z");
        field.Undo();
        Assert.Equal("abc", field.Text_);
    }

    [Fact]
    public void Overwrite_mode_replaces_the_cluster_at_the_caret()
    {
        var (field, scene, shell) = Show("width: 80; text: 'abcd'");
        using var _ = shell;
        field.CursorPosition = 1;
        field.OverwriteMode = true;

        scene.TextInput("Z");
        scene.TextInput("Y");

        Assert.Equal("aZYd", field.Text_);
        field.CursorPosition = field.Text_.Length;
        scene.TextInput("Q");
        Assert.Equal("aZYdQ", field.Text_);
    }

    [Fact]
    public void Auto_scroll_off_leaves_the_caret_past_the_field()
    {
        var (field, _, shell) = Show("width: 20; text: 'hello world'; autoScroll: false");
        using var _ = shell;
        field.CursorPosition = field.Text_.Length;

        Assert.True(field.CursorRectangle.X > field.Width);

        field.AutoScroll = true;
        Assert.True(field.CursorRectangle.X <= field.Width + 1);
    }

    [Fact]
    public void Password_echo_on_edit_shows_what_was_just_typed_until_focus_leaves()
    {
        var (field, scene, shell) = Show("width: 80; echoMode: TextInput.PasswordEchoOnEdit; text: 'secret'");
        using var _ = shell;
        Assert.Equal(Masked(field, "secret"), field.DisplayText);

        scene.TextInput("a");
        scene.TextInput("b");

        Assert.Equal("ab", field.Text_);
        Assert.Equal("ab", field.DisplayText);

        scene.SetFocusItem(null);
        Assert.Equal("ab", field.Text_);
        Assert.Equal(Masked(field, "ab"), field.DisplayText);
    }

    [Fact]
    public void Can_paste_is_false_when_the_clipboard_has_no_text_or_the_field_is_read_only()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            TextInput { }
            """));
        var clip = new FakeClipboard();
        shell.Engine.AddService<IClipboard>(clip);
        var field = (TextInput)shell.Load();
        using var _ = shell;
        var notes = 0;
        field.Connect("canPasteChanged", _ => notes++);

        Assert.False(field.CanPaste);
        clip.Text = "hi";
        clip.Raise();
        Assert.True(field.CanPaste);
        Assert.Equal(1, notes);

        field.ReadOnly = true;
        Assert.False(field.CanPaste);
        Assert.Equal(2, notes);
    }

    private static string Masked(TextInput field, string text) =>
        string.Concat(Enumerable.Repeat(field.PasswordCharacter, new System.Globalization.StringInfo(text).LengthInTextElements));

    private static (TextInput Field, Scene Scene, TestShell Shell) Show(string body)
    {
        var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            TextInput { height: 30; {{body}} }
            """));
        var field = (TextInput)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(80);
        field.SetParentItem(scene.Root);
        shell.Loop.Process();
        field.ForceActiveFocus();
        return (field, scene, shell);
    }

    private sealed class FakeClipboard : IClipboard
    {
        public event Action? Changed;

        public string? Text { get; set; }

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;

        public void Raise() => Changed?.Invoke();
    }
}
