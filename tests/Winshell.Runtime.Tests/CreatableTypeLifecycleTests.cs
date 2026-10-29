using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// Every creatable type in the runtime registry has to load, paint, survive a hard reload, and be destroyed.
/// Windows service modules register through <see cref="StandardModules.ExtraModules"/> and stay out of this list.
/// </summary>
public class CreatableTypeLifecycleTests
{
    public static TheoryData<string, string> Types()
    {
        var cases = new TheoryData<string, string>();
        var previous = StandardModules.ExtraModules;
        StandardModules.ExtraModules = null;
        try
        {
            foreach (var module in StandardModules.Create().All.OrderBy(m => m.Name, StringComparer.Ordinal))
                foreach (var type in module.Types.Values.Where(t => t.Creatable).OrderBy(t => t.Name, StringComparer.Ordinal))
                    cases.Add(module.Name, type.Name);
        }
        finally
        {
            StandardModules.ExtraModules = previous;
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void A_creatable_type_loads_paints_reloads_and_is_destroyed(string module, string type)
    {
        using var files = new TestShell();
        var root = files.Directory.Replace('\\', '/');
        // Component is the one creatable type whose body must be exactly one object.
        var instance = type == "Component"
            ? "Tested.Component { id: candidate; Item {} }"
            : $"Tested.{type} {{ id: candidate }}";
        files.Write("shell.qml", $$"""
            //@ pragma DataDir {{root}}/data
            //@ pragma StateDir {{root}}/state
            //@ pragma CacheDir {{root}}/cache
            import Winshell
            import QtQuick
            import {{module}} as Tested
            ShellRoot {
              property alias candidate: candidate
              Item {
                objectName: "surface"
                width: 64
                height: 64
                {{instance}}
              }
            }
            """);
        var platform = new FakePlatform();
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, platform, files.Log);
        host.WatchFiles = false;
        Assert.True(host.Start(), string.Join("\n", files.Messages));
        files.Loop.Process();
        var first = Assert.IsAssignableFrom<QObject>(host.Root!.ReadProperty("candidate"));
        Paint(host);

        host.Reload(hard: true);
        files.Loop.Process();

        Assert.True(first.IsDestroyed);
        var second = Assert.IsAssignableFrom<QObject>(host.Root!.ReadProperty("candidate"));
        Assert.NotSame(first, second);
        Paint(host);
        host.Dispose();

        Assert.True(second.IsDestroyed);
        Assert.All(platform.Windows, window => Assert.True(window.Disposed));
        Assert.DoesNotContain(files.Messages, line => line.StartsWith("Error ", StringComparison.Ordinal) || line.StartsWith("Fatal ", StringComparison.Ordinal));
    }

    private static void Paint(ShellHost host)
    {
        var surface = host.Root!.OwnedObjects.OfType<Item>().Single(item => item.ObjectName == "surface");
        var scene = new Scene();
        surface.SetParentItem(scene.Root);
        scene.Root.SetLayoutWidth(64);
        scene.Root.SetLayoutHeight(64);
        PolishQueue.Flush();
        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);
        scene.Paint(canvas, TimeSpan.Zero);
    }
}
