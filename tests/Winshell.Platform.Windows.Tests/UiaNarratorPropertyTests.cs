using Winshell.Platform.Windows.Accessibility;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>UI Automation properties Narrator uses for headings, sets, dialogs, notices, and switches.</summary>
public class UiaNarratorPropertyTests
{
    private const string Config = """
        import QtQuick
        import QtQuick.Controls
        import Winshell
        ShellRoot {
          PanelWindow {
            visible: false
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 200
            implicitHeight: 120
            Text { objectName: "title"; text: "Network"; Accessible.role: Accessible.Heading; Accessible.headingLevel: 1 }
            Text { objectName: "section"; text: "Wi-Fi"; Accessible.role: Accessible.Heading }
            Item { objectName: "one"; Accessible.role: Accessible.ListItem; Accessible.name: "One"; width: 8; height: 8 }
            Item { objectName: "two"; Accessible.role: Accessible.ListItem; Accessible.name: "Two"; width: 8; height: 8 }
            Item { objectName: "ask"; Accessible.role: Accessible.Dialog; Accessible.name: "Save?"; width: 8; height: 8 }
            Item { objectName: "toast"; Accessible.role: Accessible.Notification; Accessible.name: "Saved"; width: 8; height: 8 }
            Switch { objectName: "wifi"; text: "Wi-Fi" }
          }
        }
        """;

    [Fact]
    public void Narrator_hears_the_heading_level_the_set_and_no_english_switch_type()
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

            UiaElement Element(string name)
            {
                var item = Find(scene.Root, name);
                return window.Automation.ElementFor(item) ?? throw new InvalidOperationException($"No element for {name}.");
            }

            Assert.Equal(Uia.HeadingLevelValue(1), Property(Element("title"), Uia.HeadingLevelProperty));
            Assert.Equal(Uia.HeadingLevelValue(2), Property(Element("section"), Uia.HeadingLevelProperty));
            Assert.Equal(1, Property(Element("one"), Uia.PositionInSetProperty));
            Assert.Equal(2, Property(Element("one"), Uia.SizeOfSetProperty));
            Assert.Equal(2, Property(Element("two"), Uia.PositionInSetProperty));
            Assert.Equal(true, Property(Element("ask"), Uia.IsDialogProperty));
            Assert.Equal(Uia.LiveSettingAssertive, Property(Element("toast"), Uia.LiveSettingProperty));
            Assert.Null(Property(Element("wifi"), Uia.LocalizedControlTypeProperty));
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

    private static object? Property(UiaElement element, int id)
    {
        var hr = ((IRawElementProviderSimple)element).GetPropertyValue(id, out var value);
        Assert.Equal(Uia.S_OK, hr);
        return value;
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
