using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Winshell.ShellTests;

[Trait("Category", "Slow")]
public sealed class InspectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public InspectorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static PanelWindow Panel(FrameworkElement content) => new()
    {
        Anchors = Edges.Top | Edges.Left,
        Width = 220,
        Height = 90,
        Content = content,
        Visible = true,
    };

    private static JsonElement Node(JsonElement node, string name)
    {
        if (node.TryGetProperty("name", out var own) && own.GetString() == name)
            return node;
        if (node.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                var found = Node(child, name);
                if (found.ValueKind != JsonValueKind.Undefined)
                    return found;
            }
        }
        return default;
    }

    private static JsonElement OpenWindow(string tree, string content) =>
        JsonDocument.Parse(tree).RootElement.EnumerateArray()
            .Single(window => window.GetProperty("content").GetProperty("type").GetString() == content).Clone();

    public sealed class Page : StackPanel;

    private static Page Filled(out Button button, out TextBlock label)
    {
        label = new TextBlock { Name = "Label", Text = "hello" };
        button = new Button { Name = "Press", Content = "Go", Margin = new Thickness(10, 0, 0, 0) };
        var page = new Page();
        page.Children.Add(label);
        page.Children.Add(button);
        page.Children.Add(new Slider { Name = "Level", Minimum = 0, Maximum = 10, Value = 4 });
        page.Children.Add(new ToggleSwitch { Name = "Wifi", IsOn = true });
        page.Children.Add(new PasswordBox { Name = "Secret", Password = "hunter2" });
        return page;
    }

    [Fact]
    public async Task The_tree_has_names_sizes_positions_and_what_each_element_holds()
    {
        var tree = await ShellFixture.On(async shell =>
        {
            var page = Filled(out var button, out _);
            using var panel = Panel(page);
            await ShellFixture.LaidOut(button);
            return await Inspector.Run(shell, "tree", []);
        });

        var window = OpenWindow(tree, nameof(Page));
        var content = window.GetProperty("content");
        Assert.Equal("PanelWindow", window.GetProperty("kind").GetString());
        Assert.Equal(220, window.GetProperty("width").GetDouble());
        Assert.Equal("hello", Node(content, "Label").GetProperty("text").GetString());
        var press = Node(content, "Press");
        Assert.Equal("Button", press.GetProperty("type").GetString());
        Assert.Equal("Go", press.GetProperty("text").GetString());
        Assert.Equal(10, press.GetProperty("x").GetDouble());
        Assert.True(press.GetProperty("width").GetDouble() > 0);
        Assert.Equal(4, Node(content, "Level").GetProperty("value").GetDouble());
        Assert.True(Node(content, "Wifi").GetProperty("on").GetBoolean());
    }

    [Fact]
    public async Task A_password_stays_out_of_the_tree()
    {
        var tree = await ShellFixture.On(async shell =>
        {
            var page = Filled(out var button, out _);
            using var panel = Panel(page);
            await ShellFixture.LaidOut(button);
            return await Inspector.Run(shell, "tree", []);
        });

        Assert.DoesNotContain("hunter2", tree, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_click_runs_the_buttons_handler_through_its_own_event()
    {
        var clicks = await ShellFixture.On(async shell =>
        {
            var page = Filled(out var button, out _);
            var count = 0;
            button.Click += (_, _) => count++;
            using var panel = Panel(page);
            await ShellFixture.LaidOut(button);

            await Inspector.Run(shell, "click", ["Press"]);
            await ShellFixture.LaidOut(button);

            return count;
        });

        Assert.Equal(1, clicks);
    }

    [Fact]
    public async Task A_click_toggles_what_toggles()
    {
        var on = await ShellFixture.On(async shell =>
        {
            var toggle = new ToggleButton { Name = "Mute", Content = "Mute" };
            using var panel = Panel(toggle);
            await ShellFixture.LaidOut(toggle);

            await Inspector.Run(shell, "click", ["Mute"]);

            return toggle.IsChecked;
        });

        Assert.True(on);
    }

    [Fact]
    public async Task A_click_finds_an_element_by_its_automation_name()
    {
        var clicks = await ShellFixture.On(async shell =>
        {
            var button = new Button { Content = "Go" };
            AutomationProperties.SetName(button, "Start");
            var count = 0;
            button.Click += (_, _) => count++;
            using var panel = Panel(button);
            await ShellFixture.LaidOut(button);

            await Inspector.Run(shell, "click", ["Start"]);
            await ShellFixture.LaidOut(button);

            return count;
        });

        Assert.Equal(1, clicks);
    }

    [Fact]
    public async Task A_disabled_button_refuses_a_click_as_it_refuses_a_pointer()
    {
        var (clicks, refused) = await ShellFixture.On(async shell =>
        {
            var button = new Button { Name = "Off", Content = "Go", IsEnabled = false };
            var count = 0;
            button.Click += (_, _) => count++;
            using var panel = Panel(button);
            await ShellFixture.LaidOut(button);

            var failed = false;
            try
            {
                await Inspector.Run(shell, "click", ["Off"]);
            }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException)
            {
                failed = true;
            }
            await ShellFixture.LaidOut(button);
            return (count, failed);
        });

        Assert.Equal(0, clicks);
        Assert.True(refused);
    }

    [Theory]
    [InlineData("click", "Missing", "No element is named Missing")]
    [InlineData("hover", "Missing", "No element is named Missing")]
    [InlineData("click", "Label", "takes no click")]
    [InlineData("hover", "Label", "has no hover look")]
    public async Task What_cannot_be_done_says_why(string command, string name, string why)
    {
        var message = await ShellFixture.On(async shell =>
        {
            var page = Filled(out var button, out _);
            using var panel = Panel(page);
            await ShellFixture.LaidOut(button);
            try
            {
                await Inspector.Run(shell, command, [name]);
                return "";
            }
            catch (IpcException e)
            {
                return e.Message;
            }
        });

        Assert.Contains(why, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hover_puts_a_control_in_its_pointer_look()
    {
        var said = await ShellFixture.On(async shell =>
        {
            var page = Filled(out var button, out _);
            using var panel = Panel(page);
            await ShellFixture.LaidOut(button);
            return await Inspector.Run(shell, "hover", ["Press"]);
        });

        Assert.Equal("Hovering Press", said);
    }

    [Fact]
    public async Task A_screenshot_is_a_png_of_that_window_with_what_it_draws()
    {
        var file = Path.Combine(_dir, "panel.png");

        await ShellFixture.On(async shell =>
        {
            var page = new Page { Background = new SolidColorBrush(Colors.Red) };
            var fill = new Grid { Width = 220, Height = 90 };
            page.Children.Add(fill);
            using var panel = Panel(page);
            await ShellFixture.LaidOut(fill);
            return await Inspector.Run(shell, "screenshot", [file, nameof(Page)]);
        });

        var png = File.ReadAllBytes(file);
        var (width, height, pixels) = await Decoded(png);
        Assert.Equal((220, 90), (width, height));
        var middle = ((height / 2 * width) + (width / 2)) * 4;
        Assert.Equal((0, 0, 255, 255), ((int)pixels[middle], (int)pixels[middle + 1], (int)pixels[middle + 2], (int)pixels[middle + 3]));
    }

    [Fact]
    public async Task An_open_menu_is_in_the_tree_takes_a_click_and_has_a_screenshot()
    {
        var file = Path.Combine(_dir, "menu.png");

        var (tree, shot) = await ShellFixture.On(async shell =>
        {
            var anchor = new Button { Name = "Anchor", Content = "Menu" };
            var page = new Page();
            page.Children.Add(anchor);
            using var panel = Panel(page);
            await ShellFixture.LaidOut(anchor);
            var item = new MenuFlyoutItem { Text = "Reload" };
            AutomationProperties.SetAutomationId(item, "reload");
            var clicked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            item.Click += (_, _) => clicked.TrySetResult();
            var menu = new MenuFlyout();
            menu.Items.Add(item);
            menu.ShowAt(anchor);
            await ShellFixture.LaidOut(item);

            var tree = await Inspector.Run(shell, "tree", []);
            var shot = await Inspector.Run(shell, "screenshot", [file, "MenuFlyoutPresenter"]);
            // The item's own Click runs, as a pointer's would.
            await Inspector.Run(shell, "click", ["reload"]);
            await clicked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            return (tree, shot);
        });

        var popup = Assert.Single(OpenWindow(tree, nameof(Page)).GetProperty("popups").EnumerateArray());
        Assert.Equal("MenuFlyoutPresenter", popup.GetProperty("type").GetString());
        Assert.Contains("\"automationId\": \"reload\"", popup.GetRawText(), StringComparison.Ordinal);
        Assert.EndsWith(file, shot, StringComparison.Ordinal);
        var (width, height, _) = await Decoded(File.ReadAllBytes(file));
        Assert.True(width > 0 && height > 0);
    }

    [Fact]
    public void A_screenshot_goes_on_a_solid_color_so_text_for_a_dark_backdrop_can_be_read()
    {
        // Clear, half white as premultiplied, and solid white.
        byte[] pixels = [0, 0, 0, 0, 128, 128, 128, 128, 255, 255, 255, 255];

        var flat = Inspector.Flattened(pixels, 32);

        Assert.Equal([32, 32, 32, 255, 143, 143, 143, 255, 255, 255, 255, 255], flat);
    }

    private static async Task<(int Width, int Height, byte[] Bgra)> Decoded(byte[] png)
    {
        using var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
        await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(png));
        stream.Seek(0);
        var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var data = await decoder.GetPixelDataAsync(
            global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            global::Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
            new global::Windows.Graphics.Imaging.BitmapTransform(),
            global::Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            global::Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData());
    }

    [Fact]
    public async Task A_window_that_is_not_open_is_named_as_missing()
    {
        var message = await ShellFixture.On(async shell =>
        {
            using var panel = Panel(new Grid());
            try
            {
                await Inspector.Run(shell, "screenshot", [Path.Combine(_dir, "none.png"), "NoSuchView"]);
                return "";
            }
            catch (IpcException e)
            {
                return e.Message;
            }
        });

        Assert.Contains("No open window shows NoSuchView", message, StringComparison.Ordinal);
    }
}
