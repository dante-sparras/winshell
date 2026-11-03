using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Winshell.Audio;

/// <summary>A playback or recording device, or an app's audio session on one.</summary>
public sealed class AudioNode : ObservableObject
{
    private readonly AudioGraphNode _node;
    private readonly Log _log;
    private string _description;
    private IReadOnlyList<float> _volumes;
    private bool _muted;
    private bool _active;

    internal AudioNode(AudioService service, AudioGraphNode node, Log log)
    {
        Service = service;
        _node = node;
        _log = log;
        _description = node.Description;
        _volumes = node.Volumes;
        _muted = node.Muted;
        _active = node.Active;
        Channels = node.Channels;
        node.PropertiesChanged += OnPropertiesChanged;
        node.AudioChanged += OnAudioChanged;
        node.ActiveChanged += OnActiveChanged;
    }

    internal AudioService Service { get; }

    internal AudioGraphNode Node => _node;

    internal bool IsRemoved { get; private set; }

    /// <summary>The node left the graph, or its service was disposed.</summary>
    internal event Action? Removed;

    /// <summary>A number that tells nodes apart while the shell runs. Windows does not store it.</summary>
    public int Id => _node.Id;

    /// <summary>For a device, the Windows endpoint id. For a session, the program's file name without <c>.exe</c>, or <c>system-sounds</c>.</summary>
    public string Name => _node.Name;

    /// <summary>The name to show.</summary>
    public string Description => _description;

    /// <summary>For a device, the description Windows gives the endpoint, such as <c>Speakers</c>. Empty for a session.</summary>
    public string InterfaceName => _node.InterfaceName;

    /// <summary>True for a playback device and for a session that plays to one.</summary>
    public bool IsPlayback => _node.IsPlayback;

    /// <summary>True for an app's audio session, false for a device.</summary>
    public bool IsSession => _node.IsSession;

    /// <summary>The device a session plays to or records from. Null for a device.</summary>
    public AudioNode? Device => Service.Find(_node.Device);

    /// <summary>True while a session plays or records, false while it is open but idle. A device stays false.</summary>
    public bool Active => _active;

    /// <summary>
    /// More about the node. A device has <c>endpointId</c>, <c>friendlyName</c>, <c>interfaceName</c>, <c>flow</c>,
    /// <c>adapter</c>, <c>icon</c> and <c>formFactor</c>. A session has <c>sessionId</c>, <c>friendlyName</c>,
    /// <c>flow</c>, <c>appName</c>, <c>icon</c>, <c>processId</c> and <c>processName</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties => _node.Properties;

    public bool Muted
    {
        get => _muted;
        set => Apply(() => _node.SetMuted(value));
    }

    /// <summary>
    /// The average of the channels, from 0 to 1: the slider position the Windows volume flyout shows. Setting it keeps
    /// the balance between channels. Windows cannot go above 1.
    /// </summary>
    public double Volume
    {
        get => Average(_volumes);
        set
        {
            var old = Average(_volumes);
            var factor = old == 0 ? 0 : value / old;
            Apply(() => _node.SetVolumes([.. _node.Volumes.Select(v => (float)(factor == 0 ? value : v * factor))]));
        }
    }

    /// <summary>
    /// The volume of each channel, from 0 to 1. A list of a different length sets every channel to its first entry.
    /// A session has one volume in Windows, so every entry shows it, and setting this sets it to their average.
    /// </summary>
    public IReadOnlyList<double> Volumes
    {
        get => [.. _volumes.Select(v => (double)v)];
        set
        {
            var volumes = (value ?? []).Select(v => (float)Math.Max(0, v)).ToArray();
            Apply(() => _node.SetVolumes(volumes));
        }
    }

    /// <summary>The speaker position of each entry of <see cref="Volumes"/>.</summary>
    public IReadOnlyList<AudioChannel> Channels { get; }

    internal void Remove()
    {
        if (IsRemoved)
            return;
        IsRemoved = true;
        _node.PropertiesChanged -= OnPropertiesChanged;
        _node.AudioChanged -= OnAudioChanged;
        _node.ActiveChanged -= OnActiveChanged;
        Removed?.Invoke();
    }

    private static double Average(IReadOnlyList<float> volumes) => volumes.Count == 0 ? 0 : volumes.Average();

    private void Apply(Action change)
    {
        try
        {
            change();
        }
        catch (COMException e)
        {
            _log.Warn("audio", $"Cannot change the volume of {_node.Description}: {e.Message}");
        }
    }

    private void OnPropertiesChanged()
    {
        Changed(nameof(Properties));
        Set(ref _description, _node.Description, nameof(Description));
    }

    private void OnAudioChanged()
    {
        var old = _volumes;
        _volumes = _node.Volumes;
        Changed(!old.SequenceEqual(_volumes), nameof(Volumes));
        Changed(Average(old) != Average(_volumes), nameof(Volume));
        Set(ref _muted, _node.Muted, nameof(Muted));
    }

    private void OnActiveChanged() => Set(ref _active, _node.Active, nameof(Active));
}

/// <summary>Every playback device, recording device and audio session Windows has.</summary>
public sealed class AudioService : ObservableObject, IDisposable
{
    private readonly IAudioGraph _graph;
    private readonly Log _log;
    private AudioNode? _defaultPlayback;
    private AudioNode? _defaultRecording;

    internal AudioService(IEventLoop loop, Log log, IAudioGraph? graph = null)
    {
        _log = log;
        _graph = graph ?? AudioGraph.Get(loop, log);
        Refresh();
        OnDefaultsChanged();
        _graph.NodeAdded += OnNodeChanged;
        _graph.NodeRemoved += OnNodeChanged;
        _graph.DefaultsChanged += OnDefaultsChanged;
    }

    /// <summary>Every node: the active devices and the audio sessions on them.</summary>
    public ObservableCollection<AudioNode> Nodes { get; } = [];

    /// <summary>The default playback device. Setting it makes that device the Windows default.</summary>
    public AudioNode? DefaultPlaybackDevice
    {
        get => _defaultPlayback;
        set => SetDefault(value, playback: true);
    }

    /// <summary>The default recording device. Setting it makes that device the Windows default.</summary>
    public AudioNode? DefaultRecordingDevice
    {
        get => _defaultRecording;
        set => SetDefault(value, playback: false);
    }

    /// <summary>False when Windows audio cannot be opened, and then <see cref="Nodes"/> is empty.</summary>
    public bool Ready => _graph.Ready;

    internal AudioNode? Find(AudioGraphNode? node) => Nodes.FirstOrDefault(n => n.Node == node);

    private void SetDefault(AudioNode? node, bool playback)
    {
        if (node is null)
            return;
        var kind = playback ? "playback" : "recording";
        if (node.IsSession || node.IsPlayback != playback)
        {
            _log.Warn("audio", $"Cannot make {node.Description} the default {kind} device: it is not a {kind} device");
            return;
        }
        _graph.SetDefault(node.Node);
    }

    private void OnNodeChanged(AudioGraphNode _) => Refresh();

    private void Refresh()
    {
        Nodes.Sync(_graph.Nodes, n => n.Node, n => n, n => new AudioNode(this, n, _log), static (_, _) => { }, n => n.Remove());
    }

    private void OnDefaultsChanged()
    {
        Set(ref _defaultPlayback, Find(_graph.DefaultPlayback), nameof(DefaultPlaybackDevice));
        Set(ref _defaultRecording, Find(_graph.DefaultRecording), nameof(DefaultRecordingDevice));
    }

    public void Dispose()
    {
        _graph.NodeAdded -= OnNodeChanged;
        _graph.NodeRemoved -= OnNodeChanged;
        _graph.DefaultsChanged -= OnDefaultsChanged;
        foreach (var node in Nodes)
            node.Remove();
    }
}
