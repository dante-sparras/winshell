using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Winshell.DefaultShell;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.ShellTests;

/// <summary>Start, laid out in a real window. Nothing is read from this PC's apps.</summary>
[Trait("Category", "Slow")]
public sealed class StartShellTests
{
    [Fact]
    public async Task The_search_placeholder_sits_on_the_line_of_the_magnifier_beside_it()
    {
        var directory = Directory.CreateTempSubdirectory("winshell-start-shell-").FullName;
        try
        {
            var (text, magnifier) = await ShellFixture.On(async shell =>
            {
                using var start = new Start(Path.Combine(directory, "launcher.toml"), new ManualEventLoop(), shell.Log);
                var view = new StartView(new StartPopup(start));
                using var window = new FloatingWindow { Title = "Winshell test", Width = 640, Height = 720, Content = view };
                window.Visible = true;
                await ShellFixture.LaidOut(view);
                var box = view.FindName("SearchBox") as TextBox ?? throw new InvalidOperationException("Start has no search box.");
                var placeholder = Find<TextBlock>(box, found => found.Name == "PlaceholderTextContentPresenter")
                    ?? throw new InvalidOperationException("The search box shows no placeholder.");
                var host = view.FindName("SearchHost") as Grid ?? throw new InvalidOperationException("Start has no search row.");
                var icon = host.Children.OfType<FontIcon>().Single();
                return (LineMiddle(placeholder, view), Middle(icon, view));
            });

            Assert.Equal(magnifier, text, 1.0);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The middle of the line a text block draws, from its first character.</summary>
    private static double LineMiddle(TextBlock text, UIElement root)
    {
        var line = text.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        return text.TransformToVisual(root).TransformPoint(default).Y + line.Y + line.Height / 2;
    }

    private static double Middle(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(default).Y + element.ActualHeight / 2;

    private static T? Find<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        if (root is T hit && (match is null || match(hit)))
            return hit;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (Find(VisualTreeHelper.GetChild(root, i), match) is { } found)
                return found;
        }

        return null;
    }
}
