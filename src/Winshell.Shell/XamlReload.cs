using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Winshell;

/// <summary>
/// Under <c>winshell dev</c>, puts a saved <c>.xaml</c> file into the running panels of its class. <c>dotnet watch</c> does
/// not read XAML, and the XAML compiler takes seconds. So this loads the markup as it is and moves what it made into the
/// live control, which keeps its C# state. A control of that class made later gets the same markup. Markup only the
/// compiler can read asks <c>winshell dev</c> for a build instead, by logging <see cref="NeedsBuild"/>.
/// </summary>
internal static partial class XamlReload
{
    /// <summary>What <c>winshell dev</c> looks for in a shell's output, to build and start it again.</summary>
    internal const string NeedsBuild = "This XAML needs a build";

    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(50);
    private static readonly Dictionary<string, string> Saved = [];
    private static DirectoryWatch? _watch;
    private static IDisposable? _waiting;

    /// <summary>Follows the <c>.xaml</c> files under a project's folder, for the life of the process.</summary>
    public static void Watch(Shell shell, string folder)
    {
        _watch = DirectoryWatch.Start(
            folder,
            DirectoryWatch.FileChanges,
            name =>
            {
                if (name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) && !IsBuildOutput(name))
                    shell.Loop.Post(() => Changed(shell, Path.Combine(folder, name)));
            },
            () => { },
            subtree: true);
    }

    private static bool IsBuildOutput(string name) =>
        name.StartsWith(@"bin\", StringComparison.OrdinalIgnoreCase) || name.StartsWith(@"obj\", StringComparison.OrdinalIgnoreCase);

    /// <summary>An editor writes a file more than once, so the last write of a moment is the one to read.</summary>
    private static void Changed(Shell shell, string file)
    {
        _waiting?.Dispose();
        _waiting = shell.Loop.Schedule(Settle, () => shell.Notices?.ShowConfigError(file, Apply(shell, file) ?? ""));
    }

    /// <summary>Applies a file to the panels of its class, and gives what is wrong with it, or null.</summary>
    internal static string? Apply(Shell shell, string file)
    {
        string text;
        XDocument document;
        try
        {
            text = File.ReadAllText(file);
            document = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (IOException)
        {
            // Still being written. The write that ends it reports again.
            return null;
        }
        catch (XmlException e)
        {
            return e.Message;
        }
        if (document.Root?.Attribute(X + "Class")?.Value is not { } name || CompilerOnly().IsMatch(text))
        {
            shell.Log.Warn("dev", $"{NeedsBuild}: {Path.GetFileName(file)}");
            return null;
        }
        var panels = shell.Platform.Windows.Select(host => host.Presentation.Content).OfType<DependencyObject>()
            .SelectMany(root => Of(root, name)).ToList();
        foreach (var panel in panels)
        {
            if (Move(panel, document) is { } problem)
                return problem;
        }
        Saved[name] = text;
        shell.Log.Info("dev", $"Applied {Path.GetFileName(file)} to {panels.Count} of {name}.");
        return null;
    }

    /// <summary>Gives a control made after a save the markup of that save, not the compiled markup it just loaded.</summary>
    internal static void Refresh(UIElement made)
    {
        if (made is UserControl panel && made.GetType().FullName is { } name && Saved.TryGetValue(name, out var text))
            Move(panel, XDocument.Parse(text));
    }

    private static IEnumerable<UserControl> Of(DependencyObject element, string name)
    {
        if (element is UserControl panel && element.GetType().FullName == name)
            yield return panel;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            foreach (var found in Of(VisualTreeHelper.GetChild(element, i), name))
                yield return found;
        }
    }

    private static string? Move(UserControl panel, XDocument document)
    {
        var type = panel.GetType();
        var copy = new XDocument(document);
        var root = copy.Root!;
        root.Attribute(X + "Class")!.Remove();
        var handlers = TakeHandlers(root, type);
        try
        {
            var fresh = (UserControl)XamlReader.Load(copy.ToString());
            foreach (var element in root.Descendants())
            {
                if (element.Attribute(X + "Name")?.Value is { } name)
                    type.GetField(name, Members)?.SetValue(panel, fresh.FindName(name));
            }
            foreach (var (name, eventName, method) in handlers)
                Connect(fresh.FindName(name), eventName, panel, method);
            foreach (var attribute in root.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.Namespace == XNamespace.None))
            {
                if (typeof(UserControl).GetProperty(attribute.Name.LocalName + "Property", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null) is DependencyProperty property)
                    panel.SetValue(property, fresh.GetValue(property));
            }
            var resources = fresh.Resources;
            fresh.Resources = [];
            panel.Resources = resources;
            var content = fresh.Content;
            fresh.Content = null;
            panel.Content = content;
            return null;
        }
        catch (Exception e) when (e is XamlParseException or COMException or InvalidCastException)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// Takes out the attributes that name a method of the class, which a loader can't connect, and gives each element a
    /// name to find it by. A handler on the root is already connected: the panel is the same object.
    /// </summary>
    private static List<(string Name, string Event, MethodInfo Method)> TakeHandlers(XElement root, Type type)
    {
        var handlers = new List<(string, string, MethodInfo)>();
        var count = 0;
        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes().Where(a => a.Name.Namespace == XNamespace.None).ToList())
            {
                if (type.GetMethod(attribute.Value, Members) is not { } method || method.GetParameters().Length != 2)
                    continue;
                attribute.Remove();
                if (element == root)
                    continue;
                var name = element.Attribute(X + "Name")?.Value;
                if (name is null)
                {
                    name = $"Handler{count++}";
                    element.SetAttributeValue(X + "Name", name);
                }
                handlers.Add((name, attribute.Name.LocalName, method));
            }
        }
        return handlers;
    }

    private static void Connect(object element, string name, object target, MethodInfo method)
    {
        var added = element.GetType().GetEvent(name);
        added?.AddEventHandler(element, Delegate.CreateDelegate(added.EventHandlerType!, target, method));
    }

    [GeneratedRegex(@"\{x:Bind\b|\bx:(Load|Phase|DeferLoadStrategy)=")]
    private static partial Regex CompilerOnly();
}
