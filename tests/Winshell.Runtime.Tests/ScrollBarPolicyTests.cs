using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

public class ScrollBarPolicyTests
{
    [Fact]
    public void Rebinding_keeps_one_policy_listener_and_destroy_drops_it()
    {
        var host = new Flickable { Width = 100, Height = 80 };
        var attached = (ScrollBarAttached)host.GetAttached(typeof(ScrollBarAttached), owner => new ScrollBarAttached(owner));
        var first = new ScrollBar { Width = 12, Height = 80 };
        attached.Vertical = first;
        attached.Rebind();
        attached.Rebind();
        Assert.Equal(1, first.PolicyListenerCount);

        var second = new ScrollBar { Width = 12, Height = 80 };
        attached.Vertical = second;
        Assert.Equal(0, first.PolicyListenerCount);
        Assert.Equal(1, second.PolicyListenerCount);

        host.Destroy();
        Assert.Equal(0, second.PolicyListenerCount);
    }

    [Fact]
    public void Destroying_a_scroll_bar_cancels_its_expand_timer()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(new ModuleRegistry(), loop);
        var scene = new Scene();
        var bar = new ScrollBar { Engine = engine, Width = 12, Height = 100 };
        bar.MarkComplete();
        bar.SetParentItem(scene.Root);
        using var bitmap = new SKBitmap(16, 16);
        using var canvas = new SKCanvas(bitmap);
        scene.Paint(canvas, TimeSpan.FromSeconds(1));
        bar.HoverEnter(1, 1, 0);
        scene.Paint(canvas, TimeSpan.FromSeconds(1.1));
        Assert.NotNull(loop.NextDue);

        bar.Destroy();
        Assert.Null(loop.NextDue);
    }
}
