using System.Reflection;
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
        var loop = new ManualEventLoop();
        var graph = AudioGraph.Get(loop, new Log());
        try
        {
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
        finally
        {
            ReleaseGraph(graph);
        }
    }

    private static void ReleaseGraph(AudioGraph graph)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var enumerator = (IMMDeviceEnumerator?)typeof(AudioGraph).GetField("_enumerator", flags)!.GetValue(graph);
        if (typeof(AudioGraph).GetField("_notifications", flags)!.GetValue(graph) is IMMNotificationClient notifications && enumerator is not null)
            enumerator.UnregisterEndpointNotificationCallback(notifications);
        foreach (var node in graph.Nodes)
        {
            if (node is EndpointNode endpoint)
                endpoint.Release();
            else if (node is SessionNode session)
                session.Release();
        }
        typeof(AudioGraph).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);
    }
}
