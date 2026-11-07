using System.Runtime.InteropServices;
using Windows.Win32.Media.Audio;
using Winshell.Native;

namespace Winshell.Audio;

/// <summary>
/// The Windows playback and recording devices, and each app's audio session on a device. It lives on the UI thread;
/// Windows' callbacks are posted to it.
/// </summary>
internal sealed class AudioGraph : IAudioGraph, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly IMMDeviceEnumerator? _enumerator;
    private readonly DeviceNotifications? _notifications;
    private readonly List<AudioGraphNode> _nodes = [];
    private readonly Dictionary<string, EndpointNode> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private int _syncPending;
    private int _nextId = 1;
    private bool _disposed;

    /// <summary>The event context Winshell's own volume changes carry.</summary>
    public static readonly Guid Context = Guid.NewGuid();

    /// <summary>Reads the devices and starts hearing about changes to them. Call on the UI thread.</summary>
    public AudioGraph(IEventLoop loop, Log log)
    {
        _loop = loop;
        _log = log;
        try
        {
            _enumerator = CoreAudio.CreateEnumerator();
            _notifications = new DeviceNotifications(this);
            _enumerator.RegisterEndpointNotificationCallback(_notifications);
            Sync();
            ReadDefaults();
            Ready = true;
        }
        catch (COMException e)
        {
            _log.Warn("audio", $"Windows audio is not available: {e.Message}");
        }
    }

    public IReadOnlyList<AudioGraphNode> Nodes => _nodes;

    public bool Ready { get; }

    public EndpointNode? DefaultPlayback { get; private set; }

    public EndpointNode? DefaultRecording { get; private set; }

    AudioGraphNode? IAudioGraph.DefaultPlayback => DefaultPlayback;

    AudioGraphNode? IAudioGraph.DefaultRecording => DefaultRecording;

    public Log Log => _log;

    public event Action<AudioGraphNode>? NodeAdded;

    public event Action<AudioGraphNode>? NodeRemoved;

    public event Action? DefaultsChanged;

    internal int NextId()
    {
        return _nextId++;
    }

    internal void Post(Action action)
    {
        _loop.Post(() =>
        {
            if (_disposed)
                return;
            try
            {
                action();
            }
            catch (Exception e) when (e is COMException or InvalidCastException or InvalidOperationException)
            {
                _log.Warn("audio", e.Message);
            }
        });
    }

    /// <summary>Makes a device the Windows default for apps (the console and multimedia roles).</summary>
    public void SetDefault(EndpointNode node)
    {
        try
        {
            CoreAudio.SetDefaultEndpoint(node.DeviceId);
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            _log.Warn("audio", $"Cannot set the default audio device to \"{node.Description}\": {e.Message}");
        }
    }

    void IAudioGraph.SetDefault(AudioGraphNode node)
    {
        SetDefault((EndpointNode)node);
    }

    /// <summary>Stops hearing from Windows and releases every device and session. Call on the UI thread.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            if (_notifications is not null)
                _enumerator?.UnregisterEndpointNotificationCallback(_notifications);
        }
        catch (COMException)
        {
            // Windows audio failed before the callback was registered.
        }

        foreach (var endpoint in _endpoints.Values)
        {
            foreach (var session in endpoint.Sessions)
                session.Release();
            endpoint.Release();
        }

        CoreAudio.Release(_enumerator);
    }

    /// <summary>Asks for the device list to be read again, once, on the UI thread.</summary>
    internal void RequestSync()
    {
        if (Interlocked.Exchange(ref _syncPending, 1) != 0)
            return;
        Post(() =>
        {
            Interlocked.Exchange(ref _syncPending, 0);
            Sync();
            ReadDefaults();
        });
    }

    private void Sync()
    {
        if (_enumerator is null)
            return;
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var flow in new[] { EDataFlow.eRender, EDataFlow.eCapture })
        {
            _enumerator.EnumAudioEndpoints(flow, DEVICE_STATE.DEVICE_STATE_ACTIVE, out var devices);
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
                    var node = new EndpointNode(this, device, id, flow == EDataFlow.eRender);
                    _endpoints[id] = node;
                    AddNode(node);
                    node.StartSessions();
                }
                catch (COMException e)
                {
                    _log.Warn("audio", $"Cannot open audio device {id}: {e.Message}");
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
        var playback = DefaultOf(EDataFlow.eRender);
        var recording = DefaultOf(EDataFlow.eCapture);
        if (playback == DefaultPlayback && recording == DefaultRecording)
            return;
        DefaultPlayback = playback;
        DefaultRecording = recording;
        foreach (var endpoint in _endpoints.Values)
        foreach (var session in endpoint.Sessions.Where(s => s.IsSystem))
            SetShown(session, endpoint == DefaultPlayback);
        DefaultsChanged?.Invoke();
    }

    private EndpointNode? DefaultOf(EDataFlow flow)
    {
        if (_enumerator is null || _enumerator.GetDefaultAudioEndpoint(flow, ERole.eConsole, out var device) != 0)
            return null;
        device.GetId(out var id);
        return _endpoints.GetValueOrDefault(id);
    }

    /// <summary>
    /// Shows a session the endpoint started tracking. Windows keeps a System Sounds session on every playback
    /// device, though only the default one plays them, so only that one is shown, as one stream.
    /// </summary>
    internal void AddSession(SessionNode session)
    {
        SetShown(session, !session.IsSystem || session.Endpoint == DefaultPlayback);
    }

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
            AddNode(session);
        else
            RemoveNode(session);
    }

    private void AddNode(AudioGraphNode node)
    {
        _nodes.Add(node);
        NodeAdded?.Invoke(node);
    }

    private void RemoveNode(AudioGraphNode node)
    {
        _nodes.Remove(node);
        NodeRemoved?.Invoke(node);
    }

    /// <summary>Hears about devices being added, removed, enabled or made the default.</summary>
    private sealed class DeviceNotifications(AudioGraph graph) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, uint newState)
        {
            graph.RequestSync();
        }

        public void OnDeviceAdded(string deviceId)
        {
            graph.RequestSync();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            graph.RequestSync();
        }

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (role == ERole.eConsole)
                graph.RequestSync();
        }

        public void OnPropertyValueChanged(string deviceId, PROPERTYKEY key)
        {
        }
    }
}
