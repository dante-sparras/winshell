using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class ClipboardTests
{
    private sealed class FakeClipboard : IClipboard
    {
        public event Action? Changed;

        public string? Text { get; private set; } = "one";

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;

        public void ChangeFromOutside(string text)
        {
            Text = text;
            Changed?.Invoke();
        }
    }

    [Fact]
    public void An_outside_clipboard_change_updates_the_shell_singleton()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            import Winshell
            QtObject { property string clip: Shell.clipboardText }
            """));
        var clip = new FakeClipboard();
        shell.Engine.AddService<IClipboard>(clip);
        var root = shell.Load();

        Assert.Equal("one", root.ReadProperty("clip"));

        clip.ChangeFromOutside("two");

        Assert.Equal("two", root.ReadProperty("clip"));
    }
}
