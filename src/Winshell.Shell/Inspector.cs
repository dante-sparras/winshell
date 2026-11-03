using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Winshell;

/// <summary>
/// <c>winshell inspect</c>: what is on screen, seen from inside the shell. It reads the live XAML tree, and acts through
/// the same automation peers Narrator uses, so nothing here reaches Windows' input queue. A window is named by its
/// content's type, or its place in <see cref="Tree"/>'s list.
/// </summary>
internal static class Inspector
{
    private const byte DarkBack = 32;
    private const byte LightBack = 243;

    public static async Task<string> Run(Shell shell, string command, IReadOnlyList<string> args)
    {
        var window = args.Skip(1).ToList();
        return command switch
        {
            "tree" => Tree(shell),
            "screenshot" when args.Count > 0 => await Screenshot(Window(shell, window), args[0], shell.IsDark),
            "click" when args.Count > 0 => Click(Find(shell, args[0], window), args[0]),
            "hover" when args.Count > 0 => Hover(Find(shell, args[0], window), args[0]),
            _ => throw new IpcException($"Unknown inspect command {command}"),
        };
    }

    /// <summary>Premultiplied BGRA over a solid color, opaque.</summary>
    internal static byte[] Flattened(byte[] bgra, byte back)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var through = 255 - bgra[i + 3];
            for (var channel = i; channel < i + 3; channel++)
                bgra[channel] = (byte)Math.Min(255, bgra[channel] + (back * through / 255));
            bgra[i + 3] = 255;
        }
        return bgra;
    }

    private static List<ShellWindow> Windows(Shell shell) =>
        [.. shell.Platform.Windows.Where(host => host.IsVisible).Select(host => host.Client).OfType<ShellWindow>()];

    /// <summary>The windows open now, each with its content's tree, as JSON.</summary>
    private static string Tree(Shell shell)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartArray();
            var windows = Windows(shell);
            for (var i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                json.WriteStartObject();
                json.WriteNumber("window", i);
                json.WriteString("kind", window.GetType().Name);
                json.WriteNumber("x", window.Origin.X);
                json.WriteNumber("y", window.Origin.Y);
                json.WriteNumber("width", window.ActualWidth);
                json.WriteNumber("height", window.ActualHeight);
                if (window.Content is { } content)
                {
                    json.WritePropertyName("content");
                    Write(json, content, content);
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter json, DependencyObject element, UIElement root)
    {
        json.WriteStartObject();
        json.WriteString("type", element.GetType().Name);
        if (element is FrameworkElement framed)
        {
            if (framed.Name.Length > 0)
                json.WriteString("name", framed.Name);
            if (AutomationProperties.GetAutomationId(framed) is { Length: > 0 } id)
                json.WriteString("automationId", id);
            if (AutomationProperties.GetName(framed) is { Length: > 0 } label)
                json.WriteString("label", label);
            var at = framed.TransformToVisual(root).TransformPoint(new Point(0, 0));
            json.WriteNumber("x", at.X);
            json.WriteNumber("y", at.Y);
            json.WriteNumber("width", framed.ActualWidth);
            json.WriteNumber("height", framed.ActualHeight);
            if (framed.Visibility != Visibility.Visible)
                json.WriteBoolean("visible", false);
        }
        Values(json, element);
        var count = VisualTreeHelper.GetChildrenCount(element);
        if (count > 0)
        {
            json.WriteStartArray("children");
            for (var i = 0; i < count; i++)
                Write(json, VisualTreeHelper.GetChild(element, i), root);
            json.WriteEndArray();
        }
        json.WriteEndObject();
    }

    /// <summary>What an element shows and holds. A PasswordBox has no case here on purpose.</summary>
    private static void Values(Utf8JsonWriter json, DependencyObject element)
    {
        switch (element)
        {
            case TextBlock { Text: { Length: > 0 } text }:
                json.WriteString("text", text);
                break;
            case TextBox box:
                json.WriteString("text", box.Text);
                break;
            case RangeBase range:
                json.WriteNumber("value", range.Value);
                break;
            case ToggleSwitch toggle:
                json.WriteBoolean("on", toggle.IsOn);
                break;
            case Selector selector:
                json.WriteNumber("selectedIndex", selector.SelectedIndex);
                break;
        }
        if (element is ToggleButton { IsChecked: { } isChecked })
            json.WriteBoolean("checked", isChecked);
        if (element is ContentControl { Content: string content })
            json.WriteString("text", content);
        if (element is Control { IsEnabled: false })
            json.WriteBoolean("enabled", false);
    }

    private static ShellWindow Window(Shell shell, IReadOnlyList<string> named)
    {
        var windows = Windows(shell);
        if (windows.Count == 0)
            throw new IpcException("No window is open.");
        if (named is not [var name, ..])
            return windows[0];
        if (int.TryParse(name, out var index) && index >= 0 && index < windows.Count)
            return windows[index];
        return windows.FirstOrDefault(window => window.Content?.GetType().Name == name)
            ?? throw new IpcException($"No open window shows {name}.");
    }

    private static FrameworkElement Find(Shell shell, string name, IReadOnlyList<string> window)
    {
        var windows = window.Count > 0 ? [Window(shell, window)] : Windows(shell);
        return windows.Select(open => open.Content).OfType<DependencyObject>().Select(root => Find(root, name)).FirstOrDefault(found => found is not null)
            ?? throw new IpcException($"No element is named {name}. winshell inspect tree lists them.");
    }

    private static FrameworkElement? Find(DependencyObject element, string name)
    {
        if (element is FrameworkElement framed
            && (framed.Name == name || AutomationProperties.GetAutomationId(framed) == name || AutomationProperties.GetName(framed) == name))
            return framed;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (Find(VisualTreeHelper.GetChild(element, i), name) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>What Narrator does to activate an element. A disabled control refuses it, as it refuses a click.</summary>
    private static string Click(FrameworkElement element, string name)
    {
        ShellWindow.Of(element)?.Host?.Press();
        var peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        switch (peer?.GetPattern(PatternInterface.Invoke), peer?.GetPattern(PatternInterface.Toggle), peer?.GetPattern(PatternInterface.ExpandCollapse))
        {
            case (IInvokeProvider invoke, _, _):
                invoke.Invoke();
                break;
            case (_, IToggleProvider toggle, _):
                toggle.Toggle();
                break;
            case (_, _, IExpandCollapseProvider expand):
                expand.Expand();
                break;
            default:
                throw new IpcException($"{name} is a {element.GetType().Name}, which takes no click.");
        }
        return $"Clicked {name}";
    }

    /// <summary>
    /// Puts a control in the look it has under a pointer. A pointer event can't be raised from code, so a handler of
    /// <c>PointerEntered</c> does not run. Only a pointer, or a method of your own, does that.
    /// </summary>
    private static string Hover(FrameworkElement element, string name)
    {
        if (element is Control control && VisualStateManager.GoToState(control, "PointerOver", false))
            return $"Hovering {name}";
        throw new IpcException($"{name} is a {element.GetType().Name}, which has no hover look.");
    }

    /// <summary>
    /// A window's content as a PNG. The system draws a backdrop, so a capture has none, and text made for a dark one would
    /// be white on nothing. So it goes on the plain color of the theme, which is what makes the picture readable.
    /// </summary>
    private static async Task<string> Screenshot(ShellWindow window, string file, bool dark)
    {
        if (window.Content is not { } content)
            throw new IpcException("That window is empty.");
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(content);
        var pixels = await bitmap.GetPixelsAsync();
        var image = new PixelImage(bitmap.PixelWidth, bitmap.PixelHeight, Flattened(pixels.ToArray(), dark ? DarkBack : LightBack));
        await File.WriteAllBytesAsync(file, image.EncodePng());
        return $"{bitmap.PixelWidth}x{bitmap.PixelHeight} {file}";
    }
}
