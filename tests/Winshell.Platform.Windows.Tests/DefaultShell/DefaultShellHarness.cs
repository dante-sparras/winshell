using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>
/// One of the default shell's QML files, laid out in a scene against <see cref="FakeServices"/>. The shell's files
/// are copied to a temporary folder next to a <c>test.qml</c> that uses them.
/// </summary>
public sealed class DefaultShellHarness : IDisposable
{
    public DefaultShellHarness(string testQml, double width = 360, double height = 640, FakeServices? services = null)
    {
        Services = services ?? new FakeServices();
        Directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(AppContext.BaseDirectory, "DefaultShell");
        foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Directory, Path.GetRelativePath(source, file));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        File.WriteAllText(Path.Combine(Directory, "test.qml"), testQml);

        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        ServiceModules.Register(registry);
        Services.Register(registry);
        Engine = new QmlEngine(registry, Loop, new Log { Sink = (level, category, message) => Messages.Add($"{level} {category}: {message}") }, Directory);
        Engine.AddService<IPlatformServices>(Urls);
        Root = (Item)Engine.CreateRoot(Path.Combine(Directory, "test.qml"));
        Scene.Root.SetLayoutWidth(width);
        Scene.Root.SetLayoutHeight(height);
        Root.SetParentItem(Scene.Root);
        Settle();
    }

    public string Directory { get; }

    public FakeServices Services { get; }

    public FakeUrls Urls { get; } = new();

    public ManualEventLoop Loop { get; } = new();

    public List<string> Messages { get; } = [];

    public QmlEngine Engine { get; }

    public Item Root { get; }

    public Scene Scene { get; } = new();

    /// <summary>Runs what is queued and lays the scene out.</summary>
    public void Settle()
    {
        Loop.Process();
        PolishQueue.Flush();
        Loop.Process();
    }

    /// <summary>The visible item with this object name.</summary>
    public Item Find(string objectName) =>
        FindAll(Root).FirstOrDefault(i => i.ObjectName == objectName && i.Visible)
        ?? throw new InvalidOperationException($"No visible item is named {objectName}.");

    public bool Has(string objectName) => FindAll(Root).Any(i => i.ObjectName == objectName && i.Visible);

    public static IEnumerable<Item> FindAll(Item item)
    {
        yield return item;
        foreach (var child in item.ChildItems)
            foreach (var found in FindAll(child))
                yield return found;
    }

    /// <summary>Clicks an item at its center, or at a fraction of its width.</summary>
    public void Click(Item item, double across = 0.5)
    {
        var p = item.MapToScene(item.Width * across, item.Height / 2);
        Scene.PointerPress(p.X, p.Y, QtEnums.LeftButton);
        Scene.PointerRelease(p.X, p.Y, QtEnums.LeftButton);
        Settle();
    }

    public void Key(int key, int modifiers = 0)
    {
        Scene.KeyPress(new KeyEvent { Key = key, Modifiers = modifiers });
        Settle();
    }

    /// <summary>Presses an item at its center, moves to a point in the scene, and lets go there.</summary>
    public void Drag(Item item, QPoint to, Action? whileHeld = null)
    {
        var from = item.MapToScene(item.Width / 2, item.Height / 2);
        Scene.PointerPress(from.X, from.Y, QtEnums.LeftButton);
        Scene.PointerMove((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        Scene.PointerMove(to.X, to.Y);
        Settle();
        whileHeld?.Invoke();
        Scene.PointerRelease(to.X, to.Y, QtEnums.LeftButton);
        Settle();
    }

    public SKBitmap Paint(SKColor background)
    {
        var bitmap = new SKBitmap(new SKImageInfo((int)Scene.Root.Width, (int)Scene.Root.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(background);
        Scene.Paint(canvas, TimeSpan.FromSeconds(1));
        return bitmap;
    }

    public void Dispose()
    {
        Engine.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, true);
        }
        catch (IOException)
        {
        }
    }
}
