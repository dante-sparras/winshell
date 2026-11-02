using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Winshell.Audio;

/// <summary>A playback or recording device, or an app's audio session on one.</summary>
public sealed class AudioNode : ObservableObject
{
    private readonly AudioGraphNode _node;
    private string _description;

    internal AudioNode(AudioService service, AudioGraphNode node, Log log)
    {
        Service = service;
        _node = node;
        _description = node.Description;
        Audio = new AudioVolume(node, log);
        node.PropertiesChanged += OnPropertiesChanged;
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
    public bool IsSession => Type.HasFlag(AudioNodeType.Session);

    public AudioNodeType Type => (AudioNodeType)_node.Type;

    /// <summary>
    /// More about the node. A device has <c>endpointId</c>, <c>friendlyName</c>, <c>interfaceName</c>, <c>flow</c>,
    /// <c>adapter</c>, <c>icon</c> and <c>formFactor</c>. A session has <c>sessionId</c>, <c>friendlyName</c>,
    /// <c>flow</c>, <c>appName</c>, <c>icon</c>, <c>processId</c> and <c>processName</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties => _node.Properties;

    /// <summary>The node's volume and mute. Every Windows node carries audio.</summary>
    public AudioVolume Audio { get; }

    /// <summary>Windows nodes are always fully known, so always ready.</summary>
    public bool Ready => true;

    internal void Remove()
    {
        if (IsRemoved)
            return;
        IsRemoved = true;
        _node.PropertiesChanged -= OnPropertiesChanged;
        Audio.Detach();
        Removed?.Invoke();
    }

    private void OnPropertiesChanged()
    {
        Changed(nameof(Properties));
        Set(ref _description, _node.Description, nameof(Description));
    }
}

/// <summary>A node's volume, per channel, and its mute.</summary>
public sealed class AudioVolume : ObservableObject
{
    private readonly AudioGraphNode _node;
    private readonly Log _log;
    private IReadOnlyList<float> _volumes;
    private bool _muted;

    internal AudioVolume(AudioGraphNode node, Log log)
    {
        _node = node;
        _log = log;
        _volumes = node.Volumes;
        _muted = node.Muted;
        Channels = [.. node.Channels.Select(c => (AudioChannel)c)];
        node.AudioChanged += OnAudioChanged;
    }

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

    internal void Detach() => _node.AudioChanged -= OnAudioChanged;

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

    private void OnAudioChanged()
    {
        var old = _volumes;
        _volumes = _node.Volumes;
        Changed(!old.SequenceEqual(_volumes), nameof(Volumes));
        Changed(Average(old) != Average(_volumes), nameof(Volume));
        Set(ref _muted, _node.Muted, nameof(Muted));
    }
}

/// <summary>The connection between an audio session and its device.</summary>
public sealed class AudioLink : ObservableObject
{
    private readonly AudioGraphLink _link;

    internal AudioLink(AudioGraphLink link, AudioNode source, AudioNode target)
    {
        _link = link;
        Source = source;
        Target = target;
        link.StateChanged += OnStateChanged;
    }

    internal AudioGraphLink Link => _link;

    /// <summary>A number that tells links apart while the shell runs.</summary>
    public int Id => _link.Id;

    /// <summary>Where the sound comes from: a playback session, or a recording device.</summary>
    public AudioNode Source { get; }

    /// <summary>Where the sound goes: a playback device, or a recording session.</summary>
    public AudioNode Target { get; }

    /// <summary>Active while the app plays or records, Inactive while its session is idle.</summary>
    public AudioLinkState State => _link.Active ? AudioLinkState.Active : AudioLinkState.Inactive;

    internal void Detach() => _link.StateChanged -= OnStateChanged;

    private void OnStateChanged() => Changed(nameof(State));
}

/// <summary>The connections between one source and one target. Windows has one per session.</summary>
public sealed class AudioLinkGroup : ObservableObject
{
    private readonly AudioGraphLink _link;

    internal AudioLinkGroup(AudioGraphLink link, AudioNode source, AudioNode target)
    {
        _link = link;
        Source = source;
        Target = target;
        link.StateChanged += OnStateChanged;
    }

    internal AudioGraphLink Link => _link;

    /// <summary>Where the sound comes from: a playback session, or a recording device.</summary>
    public AudioNode Source { get; }

    /// <summary>Where the sound goes: a playback device, or a recording session.</summary>
    public AudioNode Target { get; }

    /// <summary>Active while the app plays or records, Inactive while its session is idle.</summary>
    public AudioLinkState State => _link.Active ? AudioLinkState.Active : AudioLinkState.Inactive;

    internal void Detach() => _link.StateChanged -= OnStateChanged;

    private void OnStateChanged() => Changed(nameof(State));
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
        _graph.LinkAdded += OnLinkChanged;
        _graph.LinkRemoved += OnLinkChanged;
        _graph.DefaultsChanged += OnDefaultsChanged;
    }

    /// <summary>Every node: the active devices and the audio sessions on them.</summary>
    public ObservableCollection<AudioNode> Nodes { get; } = [];

    /// <summary>One link per audio session.</summary>
    public ObservableCollection<AudioLink> Links { get; } = [];

    /// <summary>One group for each entry of <see cref="Links"/>.</summary>
    public ObservableCollection<AudioLinkGroup> LinkGroups { get; } = [];

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

    private AudioNode? Find(AudioGraphNode? node) => Nodes.FirstOrDefault(n => n.Node == node);

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

    private void OnLinkChanged(AudioGraphLink _) => Refresh();

    private void Refresh()
    {
        Nodes.Sync(_graph.Nodes, n => n.Node, n => n, n => new AudioNode(this, n, _log), static (_, _) => { }, n => n.Remove());
        Links.Sync(_graph.Links, l => l.Link, l => l, l => new AudioLink(l, Find(l.Source)!, Find(l.Target)!), static (_, _) => { }, l => l.Detach());
        LinkGroups.Sync(_graph.Links, g => g.Link, l => l, l => new AudioLinkGroup(l, Find(l.Source)!, Find(l.Target)!), static (_, _) => { }, g => g.Detach());
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
        _graph.LinkAdded -= OnLinkChanged;
        _graph.LinkRemoved -= OnLinkChanged;
        _graph.DefaultsChanged -= OnDefaultsChanged;
        foreach (var link in Links)
            link.Detach();
        foreach (var group in LinkGroups)
            group.Detach();
        foreach (var node in Nodes)
            node.Remove();
    }
}
