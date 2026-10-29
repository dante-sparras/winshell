using System.Globalization;
using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Services.Audio;
using Winshell.Runtime;
using Xunit.Abstractions;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Reads the real Windows playback and recording devices. When a playback device exists, opens a silent
/// session in this process, changes only that session's volume and mute, and restores both.
/// </summary>
public sealed class AudioTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _directory;
    private readonly ManualEventLoop _loop = new();
    private readonly List<string> _messages = [];
    private readonly QmlEngine _engine;
    private readonly QObject _root;

    public AudioTests(ITestOutputHelper output)
    {
        _output = output;
        _directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "shell.qml"), """
            import QtQuick
            import Winshell.Audio
            Item {
              property var audio: Audio
              property bool ready: Audio.ready
              property var playback: Audio.defaultPlaybackDevice
              property var recording: Audio.defaultRecordingDevice
              property int playbackFlag: AudioNodeType.Playback
              property int sessionFlag: AudioNodeType.Session
              property int inactive: AudioLinkState.Inactive
              property int active: AudioLinkState.Active
              AudioPeakMonitor {
                id: meter
                node: Audio.defaultPlaybackDevice
              }
              property real peak: meter.peak
              property var peaks: meter.peaks
            }
            """);
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        ServiceModules.Register(registry);
        var log = new Log { Sink = (_, category, message) => _messages.Add($"{category}: {message}") };
        _engine = new QmlEngine(registry, _loop, log, _directory);
        _root = _engine.CreateRoot(Path.Combine(_directory, "shell.qml"));
        _loop.Process();
    }

    [Fact]
    public async Task Devices_sessions_mute_and_meters_match_core_audio()
    {
        Assert.True((bool)_root.ReadProperty("ready")!);
        Assert.Equal(0b10000, _root.ReadProperty("playbackFlag"));
        Assert.Equal(0b100, _root.ReadProperty("sessionFlag"));
        Assert.Equal(0, _root.ReadProperty("inactive"));
        Assert.Equal(1, _root.ReadProperty("active"));

        var audio = (AudioService)_root.ReadProperty("audio")!;
        var playback = _root.ReadProperty("playback") as AudioNode;
        var recording = _root.ReadProperty("recording") as AudioNode;

        if (playback is null)
        {
            _output.WriteLine("no playback device");
            Assert.DoesNotContain(audio.Nodes.Values.OfType<AudioNode>(), n => n.IsPlayback && !n.IsSession);
            return;
        }

        Assert.False(playback.IsSession);
        Assert.True(playback.IsPlayback);
        _output.WriteLine($"playback: {playback.Description} volume {playback.Audio.Volume:0.###} muted {playback.Audio.Muted} channels {playback.Audio.Channels.Count}");
        Assert.Equal(playback.Name, playback.Properties["endpointId"] as string);
        Assert.Equal("playback", playback.Properties["flow"] as string);
        Assert.InRange(playback.Audio.Volume, 0, 1);
        Assert.Equal(playback.Audio.Channels.Count, playback.Audio.Volumes.Count);
        AssertEndpoint(playback.Name, EDataFlow.Render);

        var deviceVolume = playback.Audio.Volume;
        var deviceMuted = playback.Audio.Muted;

        if (recording is not null)
        {
            _output.WriteLine($"recording: {recording.Description} volume {recording.Audio.Volume:0.###} muted {recording.Audio.Muted}");
            Assert.False(recording.IsPlayback);
            Assert.False(recording.IsSession);
            Assert.Equal("recording", recording.Properties["flow"] as string);
            Assert.InRange(recording.Audio.Volume, 0, 1);
            AssertEndpoint(recording.Name, EDataFlow.Capture);
            audio.WriteProperty("defaultPlaybackDevice", recording);
            _loop.Process();
            Assert.Contains(_messages, m => m.Contains("not a playback device", StringComparison.Ordinal));
            Assert.Same(playback, _root.ReadProperty("playback"));
        }

        _loop.Advance(TimeSpan.FromMilliseconds(80));
        var peaks = ValueConverter.ToList(_root.ReadProperty("peaks"));
        _output.WriteLine($"peak {_root.ReadProperty("peak")} across {peaks.Count} channels");
        Assert.InRange((double)_root.ReadProperty("peak")!, 0, 1);
        Assert.All(peaks, peak => Assert.InRange((double)peak!, 0, 1));
        if (playback.Node.Meter is { } meter)
        {
            Assert.Equal(0, meter.GetPeakValue(out var level));
            Assert.InRange(level, 0, 1);
            Assert.Equal(0, meter.GetMeteringChannelCount(out var channels));
            if (channels > 0)
                Assert.NotEmpty(peaks);
        }

        var client = StartSilence(playback.Name);
        try
        {
            var session = await WaitForSession(audio);
            Assert.NotNull(session);
            Assert.True(session.IsSession);
            Assert.True(session.IsPlayback);
            Assert.Equal("playback", session.Properties["flow"] as string);
            Assert.Equal(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), session.Properties["processId"] as string);

            var link = audio.Links.Values.OfType<AudioLink>().First(l => ReferenceEquals(l.Source, session));
            Assert.Equal(1, link.State);

            var volume = session.Audio.Volume;
            var muted = session.Audio.Muted;
            _output.WriteLine($"session: {session.Description} volume {volume:0.###} muted {muted}");
            var target = volume < 0.5 ? 0.5 : 0.25;
            try
            {
                session.Audio.Volume = target;
                session.Audio.Muted = !muted;
                _loop.Process();
                Assert.Equal(target, session.Audio.Volume, precision: 2);
                Assert.Equal(!muted, session.Audio.Muted);
            }
            finally
            {
                session.Audio.Volume = volume;
                session.Audio.Muted = muted;
                _loop.Process();
            }

            Assert.Equal(deviceVolume, playback.Audio.Volume);
            Assert.Equal(deviceMuted, playback.Audio.Muted);
        }
        finally
        {
            client.Stop();
            Marshal.ReleaseComObject(client);
        }
    }

    private static void AssertEndpoint(string deviceId, EDataFlow flow)
    {
        var enumerator = CoreAudio.Create<IMMDeviceEnumerator>(CoreAudio.MMDeviceEnumeratorClsid);
        Assert.Equal(0, enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out var device));
        device.GetId(out var id);
        Assert.Equal(id, deviceId);
    }

    private async Task<AudioNode?> WaitForSession(AudioService audio)
    {
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < 50; i++)
        {
            _loop.Process();
            // Another test in this process can leave an inactive session with the same pid. Use the one that is playing.
            var session = audio.Nodes.Values.OfType<AudioNode>().FirstOrDefault(n =>
                n.IsSession && n.IsPlayback
                && n.Properties.TryGetValue("processId", out var id) && (string?)id == pid
                && audio.Links.Values.OfType<AudioLink>().FirstOrDefault(l => ReferenceEquals(l.Source, n)) is { State: 1 });
            if (session is not null)
                return session;
            await Task.Delay(100);
        }
        return null;
    }

    /// <summary>A shared playback stream of silence, so this process owns one audio session.</summary>
    private static IAudioClient StartSilence(string deviceId)
    {
        var enumerator = CoreAudio.Create<IMMDeviceEnumerator>(CoreAudio.MMDeviceEnumeratorClsid);
        Assert.Equal(0, enumerator.GetDevice(deviceId, out var device));
        var client = CoreAudio.Activate<IAudioClient>(device, CoreAudio.AudioClientIid);
        Assert.Equal(0, client.GetMixFormat(out var format));
        try
        {
            var started = client.Initialize(0, 0, 1_000_000, 0, format, 0);
            Assert.True(started == 0, $"The playback device did not open a shared stream (0x{started:X8}).");
            var running = client.Start();
            Assert.True(running == 0, $"The silent stream did not start (0x{running:X8}).");
            return client;
        }
        finally
        {
            Marshal.FreeCoTaskMem(format);
        }
    }

    [Fact]
    public void A_device_node_releases_its_com_objects_once()
    {
        var enumerator = CoreAudio.Create<IMMDeviceEnumerator>(CoreAudio.MMDeviceEnumeratorClsid);
        if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out var device) != 0)
        {
            _output.WriteLine("no playback device");
            return;
        }
        device.GetId(out var id);
        var node = new EndpointNode(AudioGraph.Get(_loop, new Log()), device, id, render: true);
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

    [Fact]
    public async Task A_session_node_releases_its_com_objects_once()
    {
        var playback = _root.ReadProperty("playback") as AudioNode;
        if (playback is null)
        {
            _output.WriteLine("no playback device");
            return;
        }
        var client = StartSilence(playback.Name);
        try
        {
            var audio = (AudioService)_root.ReadProperty("audio")!;
            var node = (SessionNode)(await WaitForSession(audio))!.Node;
            var graph = AudioGraph.Get(_loop, new Log());
            var meter = node.Meter;
            Assert.NotNull(meter);

            graph.RemoveSession(node);
            node.Release();

            Assert.Throws<InvalidComObjectException>(() => meter.GetPeakValue(out _));
            Assert.Null(node.Meter);
            node.SetMuted(true);
        }
        finally
        {
            client.Stop();
            Marshal.ReleaseComObject(client);
        }
    }

    [Fact]
    public async Task A_recording_meter_does_not_list_this_process_as_a_recording_session()
    {
        // The graph is one per process and posts to the loop it was made with, so this test makes its own.
        var instance = typeof(AudioGraph).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        instance.SetValue(null, null);
        try
        {
            var graph = AudioGraph.Get(_loop, new Log());
            if (graph.DefaultRecording is not { } recording)
            {
                _output.WriteLine("no recording device");
                return;
            }
            using var keepRunning = recording.KeepRunning();
            for (var i = 0; i < 10; i++)
            {
                _loop.Process();
                Assert.DoesNotContain(recording.Sessions, s => s.Properties["processId"] == Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                await Task.Delay(50);
            }
        }
        finally
        {
            instance.SetValue(null, null);
        }
    }

    public void Dispose()
    {
        _engine.Dispose();
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }
}
