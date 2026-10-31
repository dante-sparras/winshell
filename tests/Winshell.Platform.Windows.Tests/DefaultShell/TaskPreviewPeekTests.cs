using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>When a preview asks for a peek. Its windows are fakes, so nothing reaches DWM.</summary>
[Collection(ThemeCollection.Name)]
public class TaskPreviewPeekTests
{
    private const string Tasks = """
        import QtQuick
        import Winshell.Windows
        import Winshell.Apps
        TaskList {
          windows: WindowList.windows.values
          activeWindow: WindowList.activeWindow
          apps: Apps
        }
        """;

    private static void Hover(DefaultShellHarness shell, Item item)
    {
        var p = item.MapToScene(item.Width / 2, item.Height / 2);
        shell.Scene.PointerMove(p.X, p.Y);
        shell.Settle();
    }

    private static (PopupWindow Preview, Item Card, WindowThumbnail Thumbnail) Open(DefaultShellHarness shell)
    {
        var preview = (PopupWindow)shell.Root.ReadProperty("preview")!;
        Hover(shell, shell.Find("task:window:tool"));
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(preview.Visible);
        preview.OnResized(preview.ImplicitWidth, preview.ImplicitHeight, 1);
        shell.Settle();
        var card = DefaultShellHarness.FindAll(preview.Scene.Root).Single(i => i.ObjectName == "preview:Tool");
        return (preview, card, DefaultShellHarness.FindAll(card).OfType<WindowThumbnail>().Single());
    }

    private static void Rest(DefaultShellHarness shell, PopupWindow preview, Item card, int milliseconds)
    {
        var p = card.MapToScene(card.Width / 2, card.Height / 2);
        preview.Scene.PointerMove(p.X, p.Y);
        shell.Settle();
        shell.Loop.Advance(TimeSpan.FromMilliseconds(milliseconds));
        shell.Settle();
    }

    private static DefaultShellHarness Shell()
    {
        var services = new FakeServices();
        services.WindowList.Windows.Add(new FakeAppWindow("Tool", "tool", @"C:\Tools\tool.exe"));
        return new DefaultShellHarness(Tasks, width: 600, height: 48, services: services);
    }

    [Fact]
    public void A_pointer_resting_on_a_preview_peeks_and_leaving_ends_it()
    {
        using var shell = Shell();
        var (preview, card, thumbnail) = Open(shell);

        Rest(shell, preview, card, 100);
        Assert.False(thumbnail.Peek);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(400));
        shell.Settle();
        Assert.True(thumbnail.Peek);

        preview.Scene.PointerMove(-10, -10);
        shell.Settle();
        Assert.False(thumbnail.Peek);
    }

    [Fact]
    public void A_pointer_that_only_passes_over_a_preview_does_not_peek()
    {
        using var shell = Shell();
        var (preview, card, thumbnail) = Open(shell);

        Rest(shell, preview, card, 100);
        preview.Scene.PointerMove(-10, -10);
        shell.Settle();
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        shell.Settle();

        Assert.False(thumbnail.Peek);
    }

    [Fact]
    public void Previews_that_close_end_the_peek_and_do_not_bring_it_back()
    {
        using var shell = Shell();
        var (preview, card, thumbnail) = Open(shell);
        Rest(shell, preview, card, 500);
        Assert.True(thumbnail.Peek);

        preview.Visible = false;
        shell.Settle();
        Assert.False(thumbnail.Peek);

        preview.Visible = true;
        shell.Settle();
        Assert.False(thumbnail.Peek);
    }
}
