using System.Runtime.ExceptionServices;
using Winshell.Platform.Windows.Accessibility;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A text field tells UI Automation which characters are on screen, and where.
/// Text the field has scrolled or clipped away is left out, including a line below the field.
/// </summary>
public class UiaTextGeometryTests
{
    private const string Config = """
        import QtQuick
        import Winshell
        ShellRoot {
          PanelWindow {
            visible: false
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 420
            implicitHeight: 200
            TextInput {
              objectName: "field"
              x: 12
              y: 16
              width: 360
              height: 80
              text: "Hello"
            }
          }
        }
        """;

    [Fact]
    public void Overflow_reports_the_visible_prefix_and_no_rectangle_for_the_hidden_tail() => Run(window =>
    {
        var field = Field(window);
        field.Width = 48;
        field.Height = 32;
        field.Text_ = new string('m', 40);
        field.CursorPosition = 0;

        var text = Provider(window, field);
        var document = Document(text);
        var all = RangeText(document);
        Assert.Equal(field.Text_, all);

        Assert.Equal(Uia.S_OK, text.GetVisibleRanges(out var ranges));
        var visible = (ITextRangeProvider)Assert.Single(ranges!);
        var shown = RangeText(visible);
        Assert.True(shown.Length > 0 && shown.Length < all.Length);
        Assert.Equal(shown, all[..shown.Length]);

        var screen = ScreenOf(window, field);
        var rects = Rectangles(document);
        Assert.Equal(4, rects.Length);
        Inside(screen, rects);

        Assert.Equal(Uia.S_OK, document.Clone(out var hidden));
        Assert.NotNull(hidden);
        Assert.Equal(Uia.S_OK, hidden.MoveEndpointByRange(Uia.EndpointStart, visible, Uia.EndpointEnd));
        Assert.True(RangeText(hidden).Length > 0);
        Assert.Empty(Rectangles(hidden));
    });

    [Fact]
    public void A_range_on_two_lines_has_a_rectangle_for_each_visible_line() => Run(window =>
    {
        var field = Field(window);
        field.Width = 200;
        field.Height = 80;
        field.Text_ = "alpha\nbeta";
        field.CursorPosition = 0;

        var text = Provider(window, field);
        var document = Document(text);
        Assert.Equal(field.Text_, RangeText(document));
        Assert.Equal(field.Text_, VisibleText(text));

        var rects = Rectangles(document);
        Assert.True(rects.Length >= 8 && rects.Length % 4 == 0);
        Inside(ScreenOf(window, field), rects);
        Assert.True(Tops(rects).Max() - Tops(rects).Min() > 1);
    });

    [Fact]
    public void A_line_below_the_field_and_a_field_with_no_area_are_omitted() => Run(window =>
    {
        var field = Field(window);
        field.Width = 200;
        field.Height = 4;
        field.Text_ = "alpha\nbeta";
        field.CursorPosition = 0;

        var text = Provider(window, field);
        var document = Document(text);
        Assert.Equal("alpha\nbeta", RangeText(document));
        var shown = VisibleText(text);
        Assert.Contains("alpha", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("beta", shown, StringComparison.Ordinal);

        var screen = ScreenOf(window, field);
        var rects = Rectangles(document);
        Assert.NotEmpty(rects);
        Inside(screen, rects);

        var at = field.Text_.IndexOf('b');
        Assert.Equal(Uia.S_OK, document.Clone(out var beta));
        Assert.NotNull(beta);
        Assert.Equal(Uia.S_OK, beta.MoveEndpointByUnit(Uia.EndpointStart, TextUnits.Character, at, out var moved));
        Assert.Equal(at, moved);
        Assert.Equal("beta", RangeText(beta));
        Assert.Empty(Rectangles(beta));

        field.Width = 80;
        field.Height = 0;
        field.Text_ = "hello";
        Assert.Equal(Uia.S_OK, text.GetVisibleRanges(out var none));
        Assert.NotNull(none);
        Assert.Empty(none);
        Assert.Empty(Rectangles(Document(text)));
    });

    private static ITextRangeProvider Document(ITextProvider text)
    {
        Assert.Equal(Uia.S_OK, text.get_DocumentRange(out var document));
        Assert.NotNull(document);
        return document;
    }

    private static ITextProvider Provider(Win32Window window, TextInput field)
    {
        var element = window.Automation.ElementFor(field);
        Assert.NotNull(element);
        return (ITextProvider)element;
    }

    private static string VisibleText(ITextProvider text)
    {
        Assert.Equal(Uia.S_OK, text.GetVisibleRanges(out var ranges));
        Assert.NotNull(ranges);
        return string.Concat(ranges!.Cast<ITextRangeProvider>().Select(RangeText));
    }

    private static string RangeText(ITextRangeProvider range)
    {
        Assert.Equal(Uia.S_OK, range.GetText(-1, out var text));
        return text;
    }

    private static double[] Rectangles(ITextRangeProvider range)
    {
        Assert.Equal(Uia.S_OK, range.GetBoundingRectangles(out var rects));
        Assert.NotNull(rects);
        return rects!;
    }

    private static UiaRect ScreenOf(Win32Window window, TextInput field)
    {
        var origin = field.MapToScene(0, 0);
        return window.Automation.ToScreen(new QRect(origin.X, origin.Y, field.Width, field.Height));
    }

    private static List<double> Tops(double[] rects)
    {
        var tops = new List<double>();
        for (var i = 0; i < rects.Length; i += 4)
            tops.Add(rects[i + 1]);
        return tops;
    }

    private static void Inside(UiaRect screen, double[] rects)
    {
        for (var i = 0; i < rects.Length; i += 4)
        {
            var left = rects[i];
            var top = rects[i + 1];
            var width = rects[i + 2];
            var height = rects[i + 3];
            Assert.True(width > 0, $"width {width}");
            Assert.True(height > 0, $"height {height}");
            Assert.InRange(left, screen.Left - 1, screen.Left + screen.Width + 1);
            Assert.InRange(top, screen.Top - 1, screen.Top + screen.Height + 1);
            Assert.True(left + width <= screen.Left + screen.Width + 3, $"right {left + width} past {screen.Left + screen.Width}");
            Assert.True(top + height <= screen.Top + screen.Height + 3, $"bottom {top + height} past {screen.Top + screen.Height}");
        }
    }

    private static TextInput Field(Win32Window window)
    {
        var shell = (ShellWindow)window.Client;
        return (TextInput)Find(shell.ContentItem, "field");
    }

    private static void Run(Action<Win32Window> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), Config);
        var log = new Log { Sink = (_, _, _) => { } };
        Exception? failure = null;
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(Path.Combine(dir, "shell.qml"), loop, platform, log) { WatchFiles = false };
            Assert.True(host.Start());
            var window = Assert.Single(platform.Windows);
            loop.Schedule(TimeSpan.FromMilliseconds(50), () =>
            {
                try
                {
                    check(window);
                }
                catch (Exception e)
                {
                    failure = e;
                }
                loop.Quit(0);
            });
            loop.Schedule(TimeSpan.FromMilliseconds(5000), () => loop.Quit(1));
            Assert.Equal(0, loop.Run());
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
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
        if (item.ObjectName == name)
            return item;
        foreach (var child in item.ChildItems)
        {
            var found = Find(child, name);
            if (found is not null)
                return found;
        }
        return null!;
    }
}
