using System.Runtime.InteropServices;
using Winshell.Audio;

namespace Winshell.Services.Tests;

public sealed class AudioRulesTests
{
    [Fact]
    public void A_device_node_releases_its_com_objects_once()
    {
        var enumerator = CoreAudio.Create<IMMDeviceEnumerator>(CoreAudio.MMDeviceEnumeratorClsid);
        if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out var device) != 0)
            return;

        device.GetId(out var id);
        using var graph = new AudioGraph(new ManualEventLoop(), new Log());
        var node = new EndpointNode(graph, device, id, render: true);
        var meter = node.Meter;
        Assert.NotNull(meter);

        node.Release();
        node.Release();

        Assert.Throws<InvalidComObjectException>(() => device.GetId(out _));
        Assert.Throws<InvalidComObjectException>(() => meter.GetPeakValue(out _));
        Assert.Null(node.Meter);
        node.SetMuted(true);
        Assert.Null(node.KeepRunning());
    }
}
