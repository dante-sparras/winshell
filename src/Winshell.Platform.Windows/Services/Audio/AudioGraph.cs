using System.Diagnostics;
using System.Runtime.InteropServices;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Audio;

/// <summary>
/// The Windows audio devices and app sessions, seen as Pipewire's graph: each device is a sink or source node,
/// each app's audio session is a stream node linked to its device. One graph serves the whole process, so a
/// reload does not enumerate devices again. It lives on the UI thread; Windows' callbacks are posted to it.
/// </summary>
internal sealed class AudioGraph
{
    private static AudioGraph? _instance;

    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly IMMDeviceEnumerator? _enumerator;
    private readonly DeviceNotifications? _notifications;
    private readonly List<AudioNode> _nodes = [];
    private readonly List<AudioLink> _links = [];
    private readonly Dictionary<string, EndpointNode> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private bool _syncPending;
    private int _nextId = 1;

    /// <summary>The event context Winshell's own volume changes carry.</summary>
    public static readonly Guid Context = Guid.NewGuid();

    private AudioGraph(IEventLoop loop, Log log)
    {
        _loop = loop;
        _log = log;
        try
        {
            _enumerator = CoreAudio.Create<IMMDeviceEnumerator>(CoreAudio.MMDeviceEnumeratorClsid);
            _notifications = new DeviceNotifications(this);
            _enumerator.RegisterEndpointNotificationCallback(_notifications);
            Sync();
            ReadDefaults();
        }
        catch (COMException e)
        {
            _log.Warn("pipewire", $"Windows audio is not available: {e.Message}");
        }
        Ready = true;
    }

    /// <summary>The graph, made on first use. Call on the UI thread.</summary>
    public static AudioGraph Get(IEventLoop loop, Log log) => _instance ??= new AudioGraph(loop, log);

    public IReadOnlyList<AudioNode> Nodes => _nodes;

    public IReadOnlyList<AudioLink> Links => _links;

    public bool Ready { get; }

    public EndpointNode? DefaultSink { get; private set; }

    public EndpointNode? DefaultSource { get; private set; }

    public Log Log => _log;

    public event Action<AudioNode>? NodeAdded;

    public event Action<AudioNode>? NodeRemoved;

    public event Action<AudioLink>? LinkAdded;

    public event Action<AudioLink>? LinkRemoved;

    public event Action? DefaultsChanged;

    internal int NextId() => _nextId++;

    internal void Post(Action action) => _loop.Post(() =>
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is COMException or InvalidCastException or InvalidOperationException)
        {
            _log.Warn("pipewire", e.Message);
        }
    });

    /// <summary>Makes a device the Windows default for apps (the console and multimedia roles).</summary>
    public void SetDefault(EndpointNode node)
    {
        try
        {
            var policy = CoreAudio.Create<IPolicyConfig>(CoreAudio.PolicyConfigClsid);
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(node.DeviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(node.DeviceId, ERole.Multimedia));
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            _log.Warn("pipewire", $"Cannot set the default audio device to \"{node.Description}\": {e.Message}");
        }
    }

    // Devices

    /// <summary>Asks for the device list to be read again, once, on the UI thread.</summary>
    internal void RequestSync()
    {
        if (_syncPending)
            return;
        _syncPending = true;
        Post(() =>
        {
            _syncPending = false;
            Sync();
            ReadDefaults();
        });
    }

    private void Sync()
    {
        if (_enumerator is null)
            return;
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var flow in new[] { EDataFlow.Render, EDataFlow.Capture })
        {
            _enumerator.EnumAudioEndpoints(flow, CoreAudio.DeviceStateActive, out var devices);
            devices.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                devices.Item(i, out var device);
                device.GetId(out var id);
                present.Add(id);
                if (_endpoints.ContainsKey(id))
                    continue;
                try
                {
                    var node = new EndpointNode(this, device, id, flow == EDataFlow.Render);
                    _endpoints[id] = node;
                    AddNode(node);
                    node.StartSessions();
                }
                catch (COMException e)
                {
                    _log.Warn("pipewire", $"Cannot open audio device {id}: {e.Message}");
                }
            }
        }
        foreach (var (id, node) in _endpoints.ToArray())
        {
            if (present.Contains(id))
                continue;
            _endpoints.Remove(id);
            foreach (var session in node.Sessions.ToArray())
                RemoveSession(session);
            node.Release();
            RemoveNode(node);
        }
    }

    private void ReadDefaults()
    {
        var sink = DefaultOf(EDataFlow.Render);
        var source = DefaultOf(EDataFlow.Capture);
        if (sink == DefaultSink && source == DefaultSource)
            return;
        DefaultSink = sink;
        DefaultSource = source;
        foreach (var endpoint in _endpoints.Values)
        {
            foreach (var session in endpoint.Sessions.Where(s => s.IsSystem))
                SetShown(session, endpoint == DefaultSink);
        }
        DefaultsChanged?.Invoke();
    }

    private EndpointNode? DefaultOf(EDataFlow flow)
    {
        if (_enumerator is null || _enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out var device) != 0)
            return null;
        device.GetId(out var id);
        return _endpoints.GetValueOrDefault(id);
    }

    // Sessions

    /// <summary>
    /// Shows a session the endpoint started tracking. Windows keeps a System Sounds session on every playback
    /// device, though only the default one plays them, so only that one is shown, as one stream.
    /// </summary>
    internal void AddSession(SessionNode session) => SetShown(session, !session.IsSystem || session.Endpoint == DefaultSink);

    internal void RemoveSession(SessionNode session)
    {
        if (!session.Endpoint.Sessions.Remove(session))
            return;
        session.Release();
        SetShown(session, false);
    }

    private void SetShown(SessionNode session, bool shown)
    {
        if (shown == _nodes.Contains(session))
            return;
        if (shown)
        {
            AddNode(session);
            var link = session.IsSink
                ? new AudioLink(NextId(), session, session.Endpoint)
                : new AudioLink(NextId(), session.Endpoint, session);
            link.Active = session.Active;
            session.Link = link;
            _links.Add(link);
            LinkAdded?.Invoke(link);
        }
        else
        {
            if (session.Link is { } link)
            {
                _links.Remove(link);
                LinkRemoved?.Invoke(link);
                session.Link = null;
            }
            RemoveNode(session);
        }
    }

    private void AddNode(AudioNode node)
    {
        _nodes.Add(node);
        NodeAdded?.Invoke(node);
    }

    private void RemoveNode(AudioNode node)
    {
        _nodes.Remove(node);
        NodeRemoved?.Invoke(node);
    }

    /// <summary>Hears about devices being added, removed, enabled or made the default.</summary>
    private sealed class DeviceNotifications(AudioGraph graph) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, uint newState) => graph.RequestSync();

        public void OnDeviceAdded(string deviceId) => graph.RequestSync();

        public void OnDeviceRemoved(string deviceId) => graph.RequestSync();

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (role == ERole.Console)
                graph.RequestSync();
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
        }
    }
}

/// <summary>A node of the graph: a device or an app's session, with its volume.</summary>
internal abstract class AudioNode(int id)
{
    private float[] _volumes = [];

    public int Id { get; } = id;

    public string Name { get; protected set; } = "";

    public string Description { get; protected set; } = "";

    public string Nickname { get; protected set; } = "";

    /// <summary>The <c>PwNodeType</c> flags.</summary>
    public int Type { get; protected set; }

    public bool IsSink => (Type & PwNodeTypes.Sink) != 0;

    public Dictionary<string, string> Properties { get; protected set; } = [];

    public int[] Channels { get; protected set; } = [];

    public IReadOnlyList<float> Volumes => _volumes;

    public bool Muted { get; private set; }

    /// <summary>Volume, mute or channels changed.</summary>
    public event Action? AudioChanged;

    public event Action? PropertiesChanged;

    public abstract void SetVolumes(IReadOnlyList<float> volumes);

    public abstract void SetMuted(bool muted);

    /// <summary>The peak meter of the node, or null when it has none.</summary>
    public abstract IAudioMeterInformation? Meter { get; }

    /// <summary>Keeps the node's audio running while peaks are read, for devices that only meter while in use.</summary>
    public virtual IDisposable? KeepRunning() => null;

    protected void ApplyAudio(float[] volumes, bool muted)
    {
        if (volumes.AsSpan().SequenceEqual(_volumes) && muted == Muted)
            return;
        _volumes = volumes;
        Muted = muted;
        AudioChanged?.Invoke();
    }

    protected void ApplyProperties() => PropertiesChanged?.Invoke();
}

/// <summary>A connection from a stream to its device, or from a device to a recording stream.</summary>
internal sealed class AudioLink(int id, AudioNode source, AudioNode target)
{
    private bool _active;

    public int Id { get; } = id;

    public AudioNode Source { get; } = source;

    public AudioNode Target { get; } = target;

    /// <summary>True while audio flows (the session is active), false while the stream is open but idle.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
                return;
            _active = value;
            StateChanged?.Invoke();
        }
    }

    public event Action? StateChanged;
}

/// <summary>The <c>PwNodeType</c> flags, as Quickshell defines them.</summary>
internal static class PwNodeTypes
{
    public const int Audio = 0b1;
    public const int Stream = 0b100;
    public const int Source = 0b1000;
    public const int Sink = 0b10000;
}

/// <summary>An audio device: a sink when it plays, a source when it records.</summary>
internal sealed class EndpointNode : AudioNode
{
    private readonly AudioGraph _graph;
    private readonly IMMDevice _device;
    private readonly IAudioEndpointVolume _volume;
    private readonly VolumeNotifications _volumeNotifications;
    private IAudioSessionManager2? _sessionManager;
    private SessionNotifications? _sessionNotifications;
    private IAudioMeterInformation? _meter;
    private IAudioClient? _keepAlive;
    private int _keepAliveCount;
    private bool _refreshPending;

    public EndpointNode(AudioGraph graph, IMMDevice device, string deviceId, bool render)
        : base(graph.NextId())
    {
        _graph = graph;
        _device = device;
        DeviceId = deviceId;
        Type = PwNodeTypes.Audio | (render ? PwNodeTypes.Sink : PwNodeTypes.Source);
        device.OpenPropertyStore(0, out var store);
        Description = CoreAudio.ReadString(store, CoreAudio.DeviceFriendlyName) ?? deviceId;
        Nickname = CoreAudio.ReadString(store, CoreAudio.DeviceDescription) ?? "";
        // node.name is Pipewire's stable identifier for a device; the Windows endpoint id is that here.
        Name = deviceId;
        var formFactor = CoreAudio.ReadUInt(store, CoreAudio.EndpointFormFactor);
        Properties = new Dictionary<string, string>
        {
            ["node.name"] = Name,
            ["node.description"] = Description,
            ["node.nick"] = Nickname,
            ["media.class"] = render ? "Audio/Sink" : "Audio/Source",
            ["device.description"] = CoreAudio.ReadString(store, CoreAudio.InterfaceFriendlyName) ?? Description,
            ["device.icon-name"] = IconName(formFactor, render),
            ["device.form-factor"] = FormFactorName(formFactor),
            ["object.id"] = Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var format = CoreAudio.ReadBlob(store, CoreAudio.EngineDeviceFormat);

        _volume = CoreAudio.Activate<IAudioEndpointVolume>(device, CoreAudio.AudioEndpointVolumeIid);
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
            try
            {
                return _meter ??= CoreAudio.Activate<IAudioMeterInformation>(_device, CoreAudio.AudioMeterInformationIid);
            }
            catch (COMException)
            {
                return null;
            }
        }
    }

    public override void SetVolumes(IReadOnlyList<float> volumes)
    {
        if (volumes.Count == 0)
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
        var context = AudioGraph.Context;
        _volume.SetMute(muted, ref context);
        ReadVolume();
    }

    /// <summary>
    /// A capture device only meters while something records from it, as Pipewire's peak monitor records
    /// from it too. This opens a shared recording stream while peaks are wanted.
    /// </summary>
    public override IDisposable? KeepRunning()
    {
        if (IsSink)
            return null;
        if (_keepAliveCount++ == 0)
        {
            try
            {
                var client = CoreAudio.Activate<IAudioClient>(_device, CoreAudio.AudioClientIid);
                if (client.GetMixFormat(out var format) == 0)
                {
                    // Shared mode, a 100 ms buffer. Nothing reads it; overruns are harmless.
                    var ok = client.Initialize(0, 0, 1_000_000, 0, format, 0) == 0 && client.Start() == 0;
                    Marshal.FreeCoTaskMem(format);
                    if (ok)
                        _keepAlive = client;
                }
            }
            catch (COMException)
            {
            }
        }
        return new DisposeAction(() =>
        {
            if (--_keepAliveCount == 0 && _keepAlive is { } client)
            {
                client.Stop();
                _keepAlive = null;
            }
        });
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
        if (_refreshPending)
            return;
        _refreshPending = true;
        _graph.Post(() =>
        {
            _refreshPending = false;
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
            _sessionManager = CoreAudio.Activate<IAudioSessionManager2>(_device, CoreAudio.AudioSessionManager2Iid);
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
            _graph.Log.Warn("pipewire", $"Cannot list the audio sessions of \"{Description}\": {e.Message}");
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
        // A recording device's System Sounds session records nothing.
        if (state == AudioSessionState.Expired || (!IsSink && control.IsSystemSoundsSession() == 0))
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
        Released = true;
        try
        {
            _volume.UnregisterControlChangeNotify(_volumeNotifications);
            if (_sessionNotifications is not null)
                _sessionManager?.UnregisterSessionNotification(_sessionNotifications);
            _keepAlive?.Stop();
        }
        catch (COMException)
        {
            // The device is gone already.
        }
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
            node._graph.Post(() => node.AddSession(session));
            return 0;
        }
    }
}

/// <summary>
/// An app's audio session on one device: a playback stream on a speaker, a recording stream on a microphone.
/// Windows gives a session one volume, so every channel shows that volume, and setting the channels sets it to
/// their average.
/// </summary>
internal sealed class SessionNode : AudioNode
{
    private readonly AudioGraph _graph;
    private readonly IAudioSessionControl2 _control;
    private readonly ISimpleAudioVolume _volume;
    private readonly SessionEvents _events;
    private readonly uint _processId;
    private Process? _process;
    private bool _released;

    public SessionNode(AudioGraph graph, EndpointNode endpoint, IAudioSessionControl2 control, string instanceId, AudioSessionState state)
        : base(graph.NextId())
    {
        _graph = graph;
        _control = control;
        Endpoint = endpoint;
        InstanceId = instanceId;
        Active = state == AudioSessionState.Active;
        Type = PwNodeTypes.Audio | PwNodeTypes.Stream | (endpoint.IsSink ? PwNodeTypes.Sink : PwNodeTypes.Source);
        Channels = endpoint.Channels;
        control.GetProcessId(out _processId);
        IsSystem = control.IsSystemSoundsSession() == 0;
        _volume = (ISimpleAudioVolume)control;
        ReadProperties();
        ReadVolume();
        _events = new SessionEvents(this);
        control.RegisterAudioSessionNotification(_events);
    }

    public EndpointNode Endpoint { get; }

    public string InstanceId { get; }

    public bool Active { get; private set; }

    /// <summary>The session of Windows' own sounds, not of an app.</summary>
    public bool IsSystem { get; }

    public AudioLink? Link { get; set; }

    public override IAudioMeterInformation? Meter => _control as IAudioMeterInformation;

    public override void SetVolumes(IReadOnlyList<float> volumes)
    {
        if (volumes.Count == 0)
            return;
        var context = AudioGraph.Context;
        _volume.SetMasterVolume(Math.Clamp(volumes.Average(), 0, 1), ref context);
        ReadVolume();
    }

    public override void SetMuted(bool muted)
    {
        var context = AudioGraph.Context;
        _volume.SetMute(muted, ref context);
        ReadVolume();
    }

    /// <summary>Removes the session when its app exits, since Windows keeps it while anyone holds it.</summary>
    public void Watch()
    {
        if (_processId == 0)
            return;
        try
        {
            _process = Process.GetProcessById((int)_processId);
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => _graph.Post(() => _graph.RemoveSession(this));
            if (_process.HasExited)
                _graph.Post(() => _graph.RemoveSession(this));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process is gone, or cannot be watched; the session's own state still removes it.
            _process = null;
        }
    }

    public void Release()
    {
        if (_released)
            return;
        _released = true;
        _process?.Dispose();
        try
        {
            _control.UnregisterAudioSessionNotification(_events);
        }
        catch (COMException)
        {
        }
    }

    private void ReadVolume()
    {
        _volume.GetMasterVolume(out var level);
        _volume.GetMute(out var muted);
        ApplyAudio(Enumerable.Repeat(level, Math.Max(1, Channels.Length)).ToArray(), muted);
    }

    private void ReadProperties()
    {
        var system = IsSystem;
        _control.GetDisplayName(out var displayName);
        displayName = CoreAudio.LoadIndirectString(displayName ?? "");
        var path = _processId == 0 ? null : CoreAudio.ProcessImagePath(_processId);
        var binary = path is null ? "" : Path.GetFileName(path);
        var programName = path is null ? "" : Path.GetFileNameWithoutExtension(path);
        var appName = system ? "System Sounds"
            : displayName.Length > 0 ? displayName
            : FileDescription(path) is { Length: > 0 } description ? description
            : programName;
        Name = system ? "system-sounds" : programName.Length > 0 ? programName : $"session-{Id}";
        Description = appName;
        Properties = new Dictionary<string, string>
        {
            ["node.name"] = Name,
            ["node.description"] = Description,
            ["media.class"] = Endpoint.IsSink ? "Stream/Output/Audio" : "Stream/Input/Audio",
            ["application.name"] = appName,
            // The program's own icon: IconImage and Quickshell.iconPath accept a program's path.
            ["application.icon-name"] = system ? "audio-volume-high" : path ?? "",
            ["application.process.id"] = _processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["application.process.binary"] = binary,
            ["object.id"] = Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (path is null)
            Properties.Remove("application.icon-name");
    }

    private static string? FileDescription(string? path)
    {
        if (path is null)
            return null;
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private void OnState(AudioSessionState state)
    {
        if (_released)
            return;
        if (state == AudioSessionState.Expired)
        {
            _graph.RemoveSession(this);
            return;
        }
        Active = state == AudioSessionState.Active;
        if (Link is { } link)
            link.Active = Active;
    }

    private sealed class SessionEvents(SessionNode node) : IAudioSessionEvents
    {
        public int OnDisplayNameChanged(string name, ref Guid context)
        {
            node._graph.Post(() =>
            {
                if (node._released)
                    return;
                node.ReadProperties();
                node.ApplyProperties();
            });
            return 0;
        }

        public int OnIconPathChanged(string path, ref Guid context) => 0;

        public int OnSimpleVolumeChanged(float volume, bool mute, ref Guid context)
        {
            node._graph.Post(() =>
            {
                if (!node._released)
                    node.ReadVolume();
            });
            return 0;
        }

        public int OnChannelVolumeChanged(uint count, nint volumes, uint changedChannel, ref Guid context) => 0;

        public int OnGroupingParamChanged(ref Guid param, ref Guid context) => 0;

        public int OnStateChanged(AudioSessionState state)
        {
            node._graph.Post(() => node.OnState(state));
            return 0;
        }

        public int OnSessionDisconnected(int reason)
        {
            node._graph.Post(() => node._graph.RemoveSession(node));
            return 0;
        }
    }
}

/// <summary>Maps a Windows speaker layout onto Pipewire's channel positions.</summary>
internal static class ChannelLayout
{
    private const int Mono = 2;
    private const int AuxStart = 0x1000;

    // SPEAKER_FRONT_LEFT, SPEAKER_FRONT_RIGHT, ... in bit order, as PwAudioChannel values.
    private static readonly int[] MaskChannels =
    [
        3, // FrontLeft
        4, // FrontRight
        5, // FrontCenter
        6, // LowFrequencyEffects
        12, // RearLeft (back left)
        13, // RearRight (back right)
        9, // FrontLeftCenter
        10, // FrontRightCenter
        11, // RearCenter (back center)
        7, // SideLeft
        8, // SideRight
        14, // TopCenter
        15, // TopFrontLeft
        16, // TopFrontCenter
        17, // TopFrontRight
        18, // TopRearLeft
        19, // TopRearCenter
        20, // TopRearRight
    ];

    /// <summary>The channels of a device from its WAVEFORMATEXTENSIBLE, or a guess from the channel count.</summary>
    public static int[] FromFormat(byte[]? format, int count)
    {
        if (format is { Length: >= 24 } && BitConverter.ToUInt16(format, 0) == 0xFFFE)
        {
            var mask = BitConverter.ToUInt32(format, 20);
            var channels = new List<int>();
            for (var bit = 0; bit < MaskChannels.Length; bit++)
            {
                if ((mask & (1u << bit)) != 0)
                    channels.Add(MaskChannels[bit]);
            }
            if (channels.Count == count)
                return channels.ToArray();
        }
        return count switch
        {
            1 => [Mono],
            2 => [3, 4],
            _ => Enumerable.Range(0, count).Select(i => AuxStart + i).ToArray(),
        };
    }
}

/// <summary>Runs an action once when disposed.</summary>
internal sealed class DisposeAction(Action action) : IDisposable
{
    private Action? _action = action;

    public void Dispose()
    {
        _action?.Invoke();
        _action = null;
    }
}
