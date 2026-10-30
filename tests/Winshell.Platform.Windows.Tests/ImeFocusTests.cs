using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// The IME is associated only with an editable field that shows what is typed. A process key is the IME's, so it
/// is not also a shortcut.
/// </summary>
public class ImeFocusTests
{
    private const string Config = """
        import QtQuick
        import QtQuick.Controls
        import Winshell
        ShellRoot {
          PanelWindow {
            visible: false
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 120
            implicitHeight: 40
            TextField { objectName: "field"; width: 40; height: 20 }
            TextField { objectName: "secret"; echoMode: TextInput.Password; width: 40; height: 20 }
            Item {
              objectName: "catch"
              property int presses: 0
              Keys.onPressed: event => { presses = presses + 1 }
            }
          }
        }
        """;

    [Fact]
    public void Only_an_editable_field_that_shows_its_text_takes_the_ime()
    {
        Assert.False(ImeSession.AcceptsComposition(null));
        Assert.False(ImeSession.AcceptsComposition(new Item()));
        var field = new TextInput();
        Assert.True(ImeSession.AcceptsComposition(field));
        field.ReadOnly = true;
        Assert.False(ImeSession.AcceptsComposition(field));
        field.ReadOnly = false;
        field.EchoMode = (int)TextInput.EchoModeValue.NoEcho;
        Assert.False(ImeSession.AcceptsComposition(field));
        field.EchoMode = (int)TextInput.EchoModeValue.Password;
        Assert.False(ImeSession.AcceptsComposition(field));
        field.EchoMode = (int)TextInput.EchoModeValue.PasswordEchoOnEdit;
        Assert.False(ImeSession.AcceptsComposition(field));
        field.EchoMode = (int)TextInput.EchoModeValue.Normal;
        Assert.True(ImeSession.AcceptsComposition(field));
        Assert.True(ImeSession.AcceptsComposition(new TextEdit()));
    }

    [Fact]
    public void A_text_field_associates_the_ime_and_a_process_key_is_not_a_shortcut()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), Config);
        var log = new Log { Sink = (_, _, _) => { } };
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(Path.Combine(dir, "shell.qml"), loop, platform, log) { WatchFiles = false };
            Assert.True(host.Start());
            var window = Assert.Single(platform.Windows);
            var scene = ((ShellWindow)window.Client).Scene;
            var field = Find(scene.Root, "field");
            var secret = Find(scene.Root, "secret");
            var catcher = Find(scene.Root, "catch");

            Assert.False(ImeNative.HasContext(window.Handle));
            scene.SetFocusItem(field);
            Assert.True(ImeNative.HasContext(window.Handle));
            scene.SetFocusItem(secret);
            Assert.False(ImeNative.HasContext(window.Handle));
            scene.SetFocusItem(catcher);
            Assert.False(ImeNative.HasContext(window.Handle));

            Assert.True(Win32.PostMessageW(window.Handle, Win32.WM_KEYDOWN, 0x41, 1));
            Assert.True(Win32.PostMessageW(window.Handle, Win32.WM_KEYDOWN, ImeSession.VkProcessKey, 1));
            loop.Schedule(TimeSpan.FromMilliseconds(300), () => loop.Quit(0));
            loop.Run();

            Assert.Equal(1, catcher.ReadProperty("presses"));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Item Find(Item item, string name)
    {
        foreach (var candidate in Walk(item))
        {
            if (candidate.ObjectName == name)
                return candidate;
        }
        throw new InvalidOperationException($"No item is named {name}.");

        static IEnumerable<Item> Walk(Item current)
        {
            yield return current;
            foreach (var child in current.ChildItems)
            {
                foreach (var found in Walk(child))
                    yield return found;
            }
        }
    }
}
