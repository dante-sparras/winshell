using System.Runtime.InteropServices;
using Windows.Win32.Media.Audio;
using Winshell.Audio;
using Winshell.Native;

namespace Winshell.Services.Tests;

/// <summary>The default playback device, so it needs one: on a PC without any, the test says so and skips.</summary>
[Trait("Category", "Slow")]
public sealed class AudioRulesTests
{
    [Fact]
    public void A_device_node_releases_its_com_objects_once()
    {
        var enumerator = CoreAudio.CreateEnumerator();
        var found = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var device);
        Assert.SkipUnless(found == 0, "This PC has no playback device.");

        device.GetId(out var id);
        using var graph = new AudioGraph(new ManualEventLoop(), new Log());
        var node = new EndpointNode(graph, device, id, true);
        var meter = node.Meter;
        Assert.NotNull(meter);

        node.Release();
        node.Release();

        Assert.Throws<InvalidComObjectException>(() => device.GetId(out _));
        Assert.Throws<InvalidComObjectException>(() => meter.ReadPeaks());
        Assert.Null(node.Meter);
        node.SetMuted(true);
        Assert.Null(node.KeepRunning());
    }

    [Fact]
    public void A_device_the_graph_holds_survives_the_reads_it_releases()
    {
        var loop = new ManualEventLoop();
        using var graph = new AudioGraph(loop, new Log());
        var node = graph.DefaultPlayback;
        Assert.SkipUnless(node is not null, "This PC has no playback device.");

        graph.RequestSync();
        loop.Process();

        Assert.Same(node, graph.DefaultPlayback);
        // The meter is activated from the device the node holds.
        Assert.NotNull(node.Meter);
    }
}
