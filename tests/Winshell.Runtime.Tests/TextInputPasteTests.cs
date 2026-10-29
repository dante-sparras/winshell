using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class TextInputPasteTests
{
    private sealed class FakeClipboard : IClipboard
    {
        public event Action? Changed { add { } remove { } }

        public string? Text { get; set; }

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }

    [Fact]
    public void A_paste_with_a_tab_keeps_the_other_characters()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            TextInput { objectName: "field"; text: "" }
            """));
        var clip = new FakeClipboard { Text = "a\tb\nc" };
        shell.Engine.AddService<IClipboard>(clip);
        var field = (TextInput)shell.Load();
        using var _ = shell;

        field.Paste();

        Assert.Equal("a b c", field.Text_);
        field.InsertText("x\ty");
        Assert.Equal("a b cx y", field.Text_);
    }
}
