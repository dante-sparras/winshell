using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.ShellTests;

/// <summary>A control as the XAML compiler leaves it, with fields for its names and a handler to connect.</summary>
public sealed class LivePanel : UserControl
{
#pragma warning disable CA1805 // Only a reload assigns it, which the compiler can't see and would otherwise reject.
    internal TextBlock? Label = null;
#pragma warning restore CA1805

    public int Remembered { get; set; }

    public int Clicks { get; private set; }

    private void OnClick(object sender, RoutedEventArgs e) => Clicks++;
}

[Trait("Category", "Slow")]
public sealed class XamlReloadTests : IDisposable
{
    private const string Open = """<UserControl x:Class="Winshell.ShellTests.LivePanel" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public XamlReloadTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static PanelWindow Panel(UIElement content) => new()
    {
        Anchors = Edges.Top | Edges.Left,
        Width = 200,
        Height = 40,
        Content = content,
        Visible = true,
    };

    private string Saved(string markup)
    {
        var file = Path.Combine(_dir, "LivePanel.xaml");
        File.WriteAllText(file, markup);
        return file;
    }

    [Fact]
    public async Task A_saved_panel_shows_its_new_markup_and_keeps_its_state()
    {
        var file = Saved(Open + """><TextBlock x:Name="Label" Text="new" /></UserControl>""");

        var (same, problem, text, remembered, field) = await ShellFixture.On(shell =>
        {
            var live = new LivePanel { Remembered = 7, Content = new TextBlock { Text = "old" } };
            using var panel = Panel(live);

            var problem = XamlReload.Apply(shell, file);

            return (ReferenceEquals(panel.Content, live), problem, (live.Content as TextBlock)?.Text, live.Remembered, live.Label?.Text);
        });

        Assert.Null(problem);
        Assert.True(same, "The control itself stays, so its C# state does.");
        Assert.Equal("new", text);
        Assert.Equal(7, remembered);
        Assert.Equal("new", field);
    }

    [Fact]
    public async Task A_handler_in_the_markup_reaches_the_live_panel()
    {
        var file = Saved(Open + """><Button Content="Press" Click="OnClick" /></UserControl>""");

        var clicks = await ShellFixture.On(async shell =>
        {
            var live = new LivePanel();
            using var panel = Panel(live);
            XamlReload.Apply(shell, file);
            var button = (Button)live.Content;
            await ShellFixture.LaidOut(button);

            new ButtonAutomationPeer(button).Invoke();
            await ShellFixture.LaidOut(button);

            return live.Clicks;
        });

        Assert.Equal(1, clicks);
    }

    [Fact]
    public async Task A_value_on_the_root_and_its_resources_reach_the_live_panel()
    {
        var file = Saved(Open + """
             Width="123" Opacity="0.5">
                <UserControl.Resources>
                    <x:String x:Key="Word">from resources</x:String>
                </UserControl.Resources>
                <TextBlock Text="{StaticResource Word}" />
            </UserControl>
            """);

        var (width, opacity, word, text) = await ShellFixture.On(shell =>
        {
            var live = new LivePanel();
            using var panel = Panel(live);

            XamlReload.Apply(shell, file);

            return (live.Width, live.Opacity, live.Resources["Word"] as string, (live.Content as TextBlock)?.Text);
        });

        Assert.Equal((123d, 0.5d), (width, opacity));
        Assert.Equal("from resources", word);
        Assert.Equal("from resources", text);
    }

    [Fact]
    public async Task Markup_that_does_not_load_says_why_and_the_panel_stays_as_it_was()
    {
        var unclosed = Saved(Open + "><TextBlock></UserControl>");
        var (text, shown) = await ShellFixture.On(shell =>
        {
            var content = new TextBlock { Text = "old" };
            var live = new LivePanel { Content = content };
            using var panel = Panel(live);

            var problem = XamlReload.Apply(shell, unclosed);

            return (problem, ReferenceEquals(live.Content, content));
        });

        Assert.False(string.IsNullOrEmpty(text));
        Assert.True(shown);
    }

    [Fact]
    public async Task A_type_the_loader_does_not_know_says_why_and_the_panel_stays_as_it_was()
    {
        var unknown = Saved(Open + "><NoSuchControl /></UserControl>");
        var (problem, shown) = await ShellFixture.On(shell =>
        {
            var content = new TextBlock { Text = "old" };
            var live = new LivePanel { Content = content };
            using var panel = Panel(live);

            return (XamlReload.Apply(shell, unknown), ReferenceEquals(live.Content, content));
        });

        Assert.False(string.IsNullOrEmpty(problem));
        Assert.True(shown);
    }

    [Theory]
    [InlineData("""<TextBlock Text="{x:Bind Remembered}" />""")]
    [InlineData("""<TextBlock x:Load="False" />""")]
    public async Task Markup_only_the_compiler_can_read_asks_for_a_build_and_changes_nothing(string body)
    {
        var file = Saved(Open + ">" + body + "</UserControl>");

        var shown = await ShellFixture.On(shell =>
        {
            var content = new TextBlock { Text = "old" };
            var live = new LivePanel { Content = content };
            using var panel = Panel(live);
            var said = new List<string>();
            var was = shell.Log.Sink;
            shell.Log.Sink = (_, _, message) => said.Add(message);
            try
            {
                XamlReload.Apply(shell, file);
            }
            finally
            {
                shell.Log.Sink = was;
            }
            return (ReferenceEquals(live.Content, content), said.Single());
        });

        Assert.True(shown.Item1);
        Assert.Contains(XamlReload.NeedsBuild, shown.Item2, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_panel_made_after_a_save_gets_that_save()
    {
        var file = Saved(Open + """><TextBlock Text="saved" /></UserControl>""");

        var text = await ShellFixture.On(shell =>
        {
            XamlReload.Apply(shell, file);
            var made = new LivePanel { Content = new TextBlock { Text = "compiled" } };

            XamlReload.Refresh(made);

            return (made.Content as TextBlock)?.Text;
        });

        Assert.Equal("saved", text);
    }
}
