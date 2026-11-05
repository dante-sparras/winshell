using System.Runtime.InteropServices;
using Windows.Win32.Media.Audio;
using Winshell.Native;

namespace Winshell.Audio;

/// <summary>An audio endpoint: a playback device, or a recording device.</summary>
internal sealed class EndpointNode : AudioGraphNode
{
    private readonly AudioGraph _graph;
    private readonly IMMDevice _device;
    private readonly IAudioEndpointVolume _volume;
    private readonly VolumeNotifications _volumeNotifications;
    private IAudioSessionManager2? _sessionManager;
    private SessionNotifications? _sessionNotifications;
    private Native.IAudioMeterInformation? _meter;
    private IAudioClient? _keepAlive;
    private int _keepAliveCount;
    private int _refreshPending;

    public EndpointNode(AudioGraph graph, IMMDevice device, string deviceId, bool render)
        : base(graph.NextId())
    {
        _graph = graph;
        _device = device;
        DeviceId = deviceId;
        Type = render ? AudioNodeType.PlaybackDevice : AudioNodeType.RecordingDevice;
        device.OpenPropertyStore(0, out var store);
        Description = PropertyStore.ReadString(store, CoreAudio.DeviceFriendlyName) ?? deviceId;
        InterfaceName = PropertyStore.ReadString(store, CoreAudio.DeviceDescription) ?? "";
        Name = deviceId;
        var formFactor = PropertyStore.ReadUInt(store, CoreAudio.EndpointFormFactor);
        Properties = new Dictionary<string, string>
        {
            ["endpointId"] = Name,
            ["friendlyName"] = Description,
            ["interfaceName"] = InterfaceName,
            ["flow"] = render ? "playback" : "recording",
            ["adapter"] = PropertyStore.ReadString(store, CoreAudio.InterfaceFriendlyName) ?? Description,
            ["icon"] = IconName(formFactor, render),
            ["formFactor"] = FormFactorName(formFactor),
        };
        var format = PropertyStore.ReadBlob(store, CoreAudio.EngineDeviceFormat);

        _volume = CoreAudio.Activate<IAudioEndpointVolume>(device);
        _volume.GetChannelCount(out var channels);
        Channels = ChannelLayout.FromFormat(format, (int)channels);
        ReadVolume();
        _volumeNotifications = new VolumeNotifications(this);
        _volume.RegisterControlChangeNotify(_volumeNotifications);
    }

    public string DeviceId { get; }

    public List<SessionNode> Sessions { get; } = [];

    public override IAudioMeterInformation? Meter
    {
        get
        {
            if (Released)
                return null;
            try
            {
                _meter ??= CoreAudio.Activate<Native.IAudioMeterInformation>(_device);
            }
            catch (COMException)
            {
                return null;
            }
            return new WindowsAudioMeter(_meter);
        }
    }

    public override void SetVolumes(IReadOnlyList<float> volumes)
    {
        if (volumes.Count == 0 || Released)
            return;
        var context = AudioGraph.Context;
        if (volumes.All(v => v == volumes[0]) || volumes.Count != Channels.Length)
        {
            _volume.SetMasterVolumeLevelScalar(Math.Clamp(volumes[0], 0, 1), ref context);
        }
        else
        {
            for (var i = 0; i < volumes.Count; i++)
                _volume.SetChannelVolumeLevelScalar((uint)i, Math.Clamp(volumes[i], 0, 1), ref context);
        }
        ReadVolume();
    }

    public override void SetMuted(bool muted)
    {
        if (Released)
            return;
        var context = AudioGraph.Context;
        _volume.SetMute(muted, ref context);
        ReadVolume();
    }

    /// <summary>
    /// A recording device only meters while something records from it. This opens a shared recording
    /// stream while peaks are wanted. Windows shows its microphone privacy indicator while the stream is
    /// open; the stream's own session is not shown as a node.
    /// </summary>
    public override IDisposable? KeepRunning()
    {
        if (IsPlayback || Released)
            return null;
        if (_keepAliveCount++ == 0)
        {
            try
            {
                _keepAlive = CoreAudio.StartSharedStream(_device);
            }
            catch (COMException)
            {
            }
        }
        return new DisposeAction(() =>
        {
            if (--_keepAliveCount == 0)
                StopKeepAlive();
        });
    }

    private void StopKeepAlive()
    {
        if (_keepAlive is not { } client)
            return;
        _keepAlive = null;
        try
        {
            client.Stop();
        }
        catch (COMException)
        {
            // The device is gone already.
        }
        CoreAudio.Release(client);
    }

    private void ReadVolume()
    {
        var volumes = new float[Channels.Length];
        for (var i = 0; i < volumes.Length; i++)
            _volume.GetChannelVolumeLevelScalar((uint)i, out volumes[i]);
        _volume.GetMute(out var muted);
        ApplyAudio(volumes, muted);
    }

    private void RequestRefresh()
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0)
            return;
        _graph.Post(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            if (!Released)
                ReadVolume();
        });
    }

    private bool Released { get; set; }

    /// <summary>Starts tracking the apps that play to or record from this device.</summary>
    public void StartSessions()
    {
        try
        {
            _sessionManager = CoreAudio.Activate<IAudioSessionManager2>(_device);
            // Windows only reports new sessions once the session list has been read.
            _sessionManager.GetSessionEnumerator(out var sessions);
            _sessionNotifications = new SessionNotifications(this);
            _sessionManager.RegisterSessionNotification(_sessionNotifications);
            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var control);
                AddSession(control);
            }
        }
        catch (COMException e)
        {
            _graph.Log.Warn("audio", $"Cannot list the audio sessions of \"{Description}\": {e.Message}");
        }
    }

    private void AddSession(IAudioSessionControl2 control)
    {
        if (Released)
            return;
        control.GetSessionInstanceIdentifier(out var instance);
        if (Sessions.Any(s => s.InstanceId == instance))
            return;
        control.GetState(out var state);
        control.GetProcessId(out var processId);
        // A recording device's System Sounds session records nothing, and this process's own session there is
        // the stream KeepRunning opens to make the meter work.
        if (state == AudioSessionState.AudioSessionStateExpired
            || (!IsPlayback && (control.IsSystemSoundsSession() == 0 || processId == Environment.ProcessId)))
            return;
        SessionNode session;
        try
        {
            session = new SessionNode(_graph, this, control, instance, state);
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            // The session closed while it was being read, or has no volume control.
            return;
        }
        Sessions.Add(session);
        _graph.AddSession(session);
        session.Watch();
    }

    public void Release()
    {
        if (Released)
            return;
        Released = true;
        try
        {
            _volume.UnregisterControlChangeNotify(_volumeNotifications);
            if (_sessionNotifications is not null)
                _sessionManager?.UnregisterSessionNotification(_sessionNotifications);
        }
        catch (COMException)
        {
            // The device is gone already.
        }
        StopKeepAlive();
        CoreAudio.Release(_meter);
        CoreAudio.Release(_sessionManager);
        CoreAudio.Release(_volume);
        CoreAudio.Release(_device);
    }

    private static string IconName(uint? formFactor, bool render) => formFactor switch
    {
        1 => "audio-speakers",
        3 => "audio-headphones",
        5 or 6 => "audio-headset",
        4 => "audio-input-microphone",
        _ => render ? "audio-card" : "audio-input-microphone",
    };

    private static string FormFactorName(uint? formFactor) => formFactor switch
    {
        0 => "network",
        1 => "speaker",
        2 => "line",
        3 => "headphone",
        4 => "microphone",
        5 => "headset",
        6 => "handset",
        8 => "spdif",
        9 => "hdmi",
        _ => "unknown",
    };

    private sealed class VolumeNotifications(EndpointNode node) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint data)
        {
            node.RequestRefresh();
            return 0;
        }
    }

    private sealed class SessionNotifications(EndpointNode node) : IAudioSessionNotification
    {
        public int OnSessionCreated(IAudioSessionControl2 session)
        {
            // The callback's object belongs to the audio engine's thread. Marshal it before the
            // UI thread reads it, including after that thread is a single-threaded apartment.
            nint stream;
            try
            {
                stream = ComApartment.Capture(session);
            }
            catch (Exception e) when (e is COMException or InvalidCastException or InvalidOperationException)
            {
                node._graph.Log.Warn("audio", e.Message);
                return 0;
            }
            node._graph.Post(() => node.AddSession((IAudioSessionControl2)ComApartment.Restore(stream)));
            return 0;
        }
    }
}
