using System.Runtime.InteropServices;
using Windows.Win32.Media.Audio;
using Winshell.Audio;
using Winshell.Native;

namespace Winshell.Services.Tests;

public sealed class AudioRulesTests
{
    [Fact]
    public void A_device_node_releases_its_com_objects_once()
    {
        var enumerator = CoreAudio.CreateEnumerator();
        if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var device) != 0)
            return;

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
}
