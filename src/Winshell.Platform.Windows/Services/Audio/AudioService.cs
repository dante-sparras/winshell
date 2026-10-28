using System.Globalization;
using System.Runtime.InteropServices;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Audio;

/// <summary>
/// The <c>Audio</c> singleton, backed by Windows audio: every playback and recording device is a sink
/// or source node, and every app's audio session is a stream node linked to its device.
/// </summary>
public sealed class AudioService : QObject
{
    private readonly AudioGraph _graph;
    private readonly Dictionary<AudioGraphNode, AudioNode> _nodes = [];
    private readonly Dictionary<AudioGraphLink, (AudioLink Link, AudioLinkGroup Group)> _links = [];

    public AudioService(QmlEngine engine)
    {
        Engine = engine;
        _graph = AudioGraph.Get(engine.Loop, engine.Log);
        Nodes = new ObjectModel { Engine = engine };
        Links = new ObjectModel { Engine = engine };
        LinkGroups = new ObjectModel { Engine = engine };
        Nodes.SetOwner(this);
        Links.SetOwner(this);
        LinkGroups.SetOwner(this);
        foreach (var node in _graph.Nodes)
            OnNodeAdded(node);
        foreach (var link in _graph.Links)
            OnLinkAdded(link);
        _graph.NodeAdded += OnNodeAdded;
        _graph.NodeRemoved += OnNodeRemoved;
        _graph.LinkAdded += OnLinkAdded;
        _graph.LinkRemoved += OnLinkRemoved;
        _graph.DefaultsChanged += OnDefaultsChanged;
    }

    /// <summary>Every node: devices and app streams.</summary>
    [Qml]
    public ObjectModel Nodes { get; }

    /// <summary>Every link. Windows has one connection per stream, so there is one link per link group.</summary>
    [Qml]
    public ObjectModel Links { get; }

    [Qml]
    public ObjectModel LinkGroups { get; }

    [Qml]
    public AudioNode? DefaultAudioSink
    {
        get { Track(); return Find(_graph.DefaultSink); }
    }

    [Qml]
    public AudioNode? DefaultAudioSource
    {
        get { Track(); return Find(_graph.DefaultSource); }
    }

    /// <summary>
    /// Windows has no separate preference: this reads as the current default device, and setting it makes the node
    /// the Windows default device.
    /// </summary>
    [Qml]
    public AudioNode? PreferredDefaultAudioSink
    {
        get { Track(); return Find(_graph.DefaultSink); }
        set => SetDefault(value, sink: true);
    }

    [Qml]
    public AudioNode? PreferredDefaultAudioSource
    {
        get { Track(); return Find(_graph.DefaultSource); }
        set => SetDefault(value, sink: false);
    }

    [Qml]
    public bool Ready
    {
        get { Track(); return _graph.Ready; }
    }

    /// <summary>The link groups changed, for <see cref="AudioLinkTracker"/>.</summary>
    internal event Action? LinkGroupsChanged;

    internal IEnumerable<AudioLinkGroup> AllLinkGroups => _links.Values.Select(l => l.Group);

    internal AudioNode? Find(AudioGraphNode? node) => node is not null && _nodes.TryGetValue(node, out var wrapper) ? wrapper : null;

    private void SetDefault(AudioNode? node, bool sink)
    {
        if (node?.Node is not EndpointNode endpoint || endpoint.IsSink != sink)
        {
            if (node is not null)
                Engine?.Log.Warn("audio", $"Cannot make {node.Description} the default {(sink ? "sink" : "source")}: it is not a {(sink ? "sink" : "source")} device");
            return;
        }
        _graph.SetDefault(endpoint);
    }

    private void OnNodeAdded(AudioGraphNode node)
    {
        var wrapper = new AudioNode(this, node) { Engine = Engine };
        wrapper.SetOwner(this);
        _nodes[node] = wrapper;
        Nodes.Add(wrapper);
    }

    private void OnNodeRemoved(AudioGraphNode node)
    {
        if (!_nodes.Remove(node, out var wrapper))
            return;
        Nodes.Remove(wrapper);
        wrapper.Destroy();
    }

    private void OnLinkAdded(AudioGraphLink link)
    {
        if (Find(link.Source) is not { } source || Find(link.Target) is not { } target)
            return;
        var pwLink = new AudioLink(link, source, target) { Engine = Engine };
        var group = new AudioLinkGroup(link, source, target) { Engine = Engine };
        pwLink.SetOwner(this);
        group.SetOwner(this);
        _links[link] = (pwLink, group);
        Links.Add(pwLink);
        LinkGroups.Add(group);
        LinkGroupsChanged?.Invoke();
    }

    private void OnLinkRemoved(AudioGraphLink link)
    {
        if (!_links.Remove(link, out var pair))
            return;
        Links.Remove(pair.Link);
        LinkGroups.Remove(pair.Group);
        LinkGroupsChanged?.Invoke();
        pair.Link.Destroy();
        pair.Group.Destroy();
    }

    private void OnDefaultsChanged()
    {
        NotifyChanged("defaultAudioSink");
        NotifyChanged("defaultAudioSource");
        NotifyChanged("preferredDefaultAudioSink");
        NotifyChanged("preferredDefaultAudioSource");
    }

    protected override void OnDestroy()
    {
        _graph.NodeAdded -= OnNodeAdded;
        _graph.NodeRemoved -= OnNodeRemoved;
        _graph.LinkAdded -= OnLinkAdded;
        _graph.LinkRemoved -= OnLinkRemoved;
        _graph.DefaultsChanged -= OnDefaultsChanged;
    }

    internal static void Register(ModuleRegistry registry)
    {
        var module = registry.Module("Winshell.Audio");
        module.Singleton(e => new AudioService(e), "Audio")
            .Uncreatable<AudioNode>("AudioNode", "AudioNodes cannot be created directly")
            .Uncreatable<AudioVolume>("AudioVolume", "AudioVolume cannot be created directly")
            .Uncreatable<AudioLink>("AudioLink", "AudioLinks cannot be created directly")
            .Uncreatable<AudioLinkGroup>("AudioLinkGroup", "AudioLinkGroups cannot be created directly")
            .Type<AudioObjectTracker>("AudioObjectTracker")
            .Type<AudioLinkTracker>("AudioLinkTracker")
            .Type<AudioPeakMonitor>("AudioPeakMonitor")
            .Add(StandardModules.EnumType("AudioNodeType", [
                new("Untracked", 0), new("Audio", 0b1), new("Video", 0b10), new("Stream", 0b100), new("Source", 0b1000),
                new("Sink", 0b10000), new("AudioSink", 0b10001), new("AudioSource", 0b1001), new("AudioDuplex", 0b11001),
                new("AudioOutStream", 0b10101), new("AudioInStream", 0b1101), new("VideoSource", 0b1010), new("VideoSink", 0b10010)], module.Name))
            .Add(StandardModules.EnumType("AudioLinkState", [
                new("Error", -2), new("Unlinked", -1), new("Init", 0), new("Negotiating", 1), new("Allocating", 2),
                new("Paused", 3), new("Active", 4)], module.Name))
            .Add(StandardModules.EnumType("AudioChannel", AudioChannels.Values, module.Name));
    }
}

/// <summary>The <c>AudioNode</c>: a device or an app stream.</summary>
public sealed class AudioNode : QObject
{
    private readonly AudioGraphNode _node;

    internal AudioNode(AudioService service, AudioGraphNode node)
    {
        Service = service;
        _node = node;
        Audio = new AudioVolume(node) { Engine = service.Engine };
        Audio.SetOwner(this);
        node.PropertiesChanged += OnPropertiesChanged;
    }

    internal AudioService Service { get; }

    internal AudioGraphNode Node => _node;

    [Qml]
    public int Id => _node.Id;

    [Qml]
    public string Name => _node.Name;

    [Qml]
    public string Description
    {
        get { Track(); return _node.Description; }
    }

    [Qml]
    public string Nickname => _node.Nickname;

    [Qml]
    public bool IsSink => _node.IsSink;

    [Qml]
    public bool IsStream => (_node.Type & AudioNodeTypes.Stream) != 0;

    /// <summary>The <c>AudioNodeType</c> flags.</summary>
    [Qml]
    public int Type => _node.Type;

    [Qml]
    public IReadOnlyDictionary<string, object?> Properties
    {
        get { Track(); return _node.Properties.ToDictionary(kv => kv.Key, kv => (object?)kv.Value); }
    }

    /// <summary>Every Windows node carries audio.</summary>
    [Qml]
    public AudioVolume Audio { get; }

    /// <summary>Windows nodes are always fully known, so always ready.</summary>
    [Qml]
    public bool Ready => true;

    private void OnPropertiesChanged()
    {
        NotifyChanged("properties");
        NotifyChanged("description");
    }

    protected override void OnDestroy() => _node.PropertiesChanged -= OnPropertiesChanged;
}

/// <summary>The <c>AudioVolume</c>: a node's volume, per channel, and mute.</summary>
public sealed class AudioVolume : QObject
{
    private readonly AudioGraphNode _node;

    internal AudioVolume(AudioGraphNode node)
    {
        _node = node;
        node.AudioChanged += OnAudioChanged;
    }

    [Qml]
    public bool Muted
    {
        get { Track(); return _node.Muted; }
        set => Apply(() => _node.SetMuted(value));
    }

    /// <summary>
    /// The average of the channels. Windows' volume is the slider position the Windows volume flyout shows, which
    /// is what <c>volume</c> means here too. Windows cannot go above 1.0.
    /// </summary>
    [Qml]
    public double Volume
    {
        get
        {
            TrackQml("volumes");
            return _node.Volumes.Count == 0 ? 0 : _node.Volumes.Average();
        }
        set
        {
            var old = _node.Volumes.Count == 0 ? 0 : _node.Volumes.Average();
            var factor = old == 0 ? 0 : value / old;
            Apply(() => _node.SetVolumes(_node.Volumes.Select(v => (float)(factor == 0 ? value : v * factor)).ToArray()));
        }
    }

    [Qml]
    public IReadOnlyList<object?> Volumes
    {
        get { Track(); return _node.Volumes.Select(v => (object?)(double)v).ToList(); }
        set
        {
            var volumes = (value ?? []).Select(v => (float)Math.Max(0, Convert.ToDouble(v, CultureInfo.InvariantCulture))).ToArray();
            Apply(() => _node.SetVolumes(volumes));
        }
    }

    /// <summary>The <c>AudioChannel</c> of each entry of <see cref="Volumes"/>.</summary>
    [Qml]
    public IReadOnlyList<object?> Channels
    {
        get { TrackQml("volumes"); return _node.Channels.Select(c => (object?)c).ToList(); }
    }

    private void Apply(Action change)
    {
        try
        {
            change();
        }
        catch (COMException e)
        {
            Engine?.Log.Warn("audio", $"Cannot change the volume of {_node.Description}: {e.Message}");
        }
    }

    private void OnAudioChanged()
    {
        NotifyChanged("volumes");
        NotifyChanged("volume");
        NotifyChanged("muted");
        NotifyChanged("channels");
    }

    protected override void OnDestroy() => _node.AudioChanged -= OnAudioChanged;
}

/// <summary>The <c>AudioLink</c>: a stream's connection to its device.</summary>
public sealed class AudioLink : QObject
{
    private readonly AudioGraphLink _link;

    internal AudioLink(AudioGraphLink link, AudioNode source, AudioNode target)
    {
        _link = link;
        Source = source;
        Target = target;
        link.StateChanged += OnStateChanged;
    }

    [Qml]
    public int Id => _link.Id;

    [Qml]
    public AudioNode Source { get; }

    [Qml]
    public AudioNode Target { get; }

    /// <summary><c>AudioLinkState.Active</c> while the app plays or records, <c>Paused</c> while its stream is idle.</summary>
    [Qml]
    public int State
    {
        get { Track(); return _link.Active ? 4 : 3; }
    }

    private void OnStateChanged() => NotifyChanged("state");

    protected override void OnDestroy() => _link.StateChanged -= OnStateChanged;
}

/// <summary>The <c>AudioLinkGroup</c>: the connections between one source and one target.</summary>
public sealed class AudioLinkGroup : QObject
{
    private readonly AudioGraphLink _link;

    internal AudioLinkGroup(AudioGraphLink link, AudioNode source, AudioNode target)
    {
        _link = link;
        Source = source;
        Target = target;
        link.StateChanged += OnStateChanged;
    }

    [Qml]
    public AudioNode Source { get; }

    [Qml]
    public AudioNode Target { get; }

    [Qml]
    public int State
    {
        get { Track(); return _link.Active ? 4 : 3; }
    }

    private void OnStateChanged() => NotifyChanged("state");

    protected override void OnDestroy() => _link.StateChanged -= OnStateChanged;
}

/// <summary>
/// The <c>AudioObjectTracker</c>. Audio objects must be bound before all their properties are valid;
/// Windows objects always are, so this only keeps the list.
/// </summary>
public sealed class AudioObjectTracker : QObject
{
    private IReadOnlyList<object?> _objects = [];

    [Qml]
    public IReadOnlyList<object?> Objects
    {
        get { Track(); return _objects; }
        set => SetField(ref _objects, value ?? []);
    }
}

/// <summary>The <c>AudioLinkTracker</c>: the link groups connected to one node.</summary>
public sealed class AudioLinkTracker : QObject
{
    private AudioNode? _node;
    private AudioService? _service;
    private IDisposable? _nodeDestroyed;
    private IReadOnlyList<AudioLinkGroup> _linkGroups = [];

    [Qml]
    public AudioNode? Node
    {
        get { Track(); return _node; }
        set
        {
            if (_node == value)
                return;
            Detach();
            _node = value;
            if (value is not null)
            {
                _service = value.Service;
                _service.LinkGroupsChanged += Update;
                _nodeDestroyed = value.Connect("destroyed", _ => Node = null);
            }
            NotifyChanged("node");
            Update();
        }
    }

    /// <summary>
    /// For a sink, the groups that play into it; for a source, the groups that record from it. Windows has no
    /// monitor streams, so none are left out.
    /// </summary>
    [Qml]
    public IReadOnlyList<AudioLinkGroup> LinkGroups
    {
        get { Track(); return _linkGroups; }
    }

    private void Update()
    {
        var node = _node;
        var groups = node is null || _service is null
            ? []
            : _service.AllLinkGroups.Where(g => node.IsSink ? g.Target == node : g.Source == node).ToList();
        if (groups.SequenceEqual(_linkGroups))
            return;
        _linkGroups = groups;
        NotifyChanged("linkGroups");
    }

    private void Detach()
    {
        if (_service is not null)
            _service.LinkGroupsChanged -= Update;
        _service = null;
        _nodeDestroyed?.Dispose();
        _nodeDestroyed = null;
    }

    protected override void OnDestroy() => Detach();
}

/// <summary>
/// The <c>AudioPeakMonitor</c>: the peak level of a node's channels, read from the Windows peak meter
/// about 30 times a second while enabled. Windows meters a device after its volume is applied, unlike PipeWire.
/// </summary>
public sealed class AudioPeakMonitor : QObject
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(33);

    private AudioNode? _node;
    private bool _enabled = true;
    private double _peak;
    private IReadOnlyList<object?> _peaks = [];
    private IReadOnlyList<object?> _channels = [];
    private IDisposable? _timer;
    private IDisposable? _keepRunning;
    private IAudioMeterInformation? _meter;

    [Qml]
    public AudioNode? Node
    {
        get { Track(); return _node; }
        set
        {
            if (SetField(ref _node, value))
                Restart();
        }
    }

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set
        {
            if (SetField(ref _enabled, value))
                Restart();
        }
    }

    [Qml]
    public double Peak
    {
        get { Track(); return _peak; }
    }

    [Qml]
    public IReadOnlyList<object?> Peaks
    {
        get { Track(); return _peaks; }
    }

    [Qml]
    public IReadOnlyList<object?> Channels
    {
        get { Track(); return _channels; }
    }

    public override void ComponentComplete() => Restart();

    private void Restart()
    {
        Stop();
        if (!IsComplete || IsDestroyed || !_enabled || _node is null || _node.IsDestroyed || Engine is null)
        {
            Clear();
            return;
        }
        _meter = _node.Node.Meter;
        if (_meter is null)
        {
            Clear();
            return;
        }
        _keepRunning = _node.Node.KeepRunning();
        Tick();
    }

    private void Tick()
    {
        _timer = null;
        if (_meter is null || Engine is null || IsDestroyed || _node is null || _node.IsDestroyed)
            return;
        try
        {
            _meter.GetMeteringChannelCount(out var count);
            var values = new float[count];
            if (count > 0 && _meter.GetChannelsPeakValues(count, values) == 0)
                Apply(values);
        }
        catch (COMException)
        {
            // The device went away; the node is removed shortly.
        }
        _timer = Engine.Loop.Schedule(Interval, Tick);
    }

    private void Apply(float[] values)
    {
        var peaks = values.Select(v => (object?)(double)v).ToList();
        if (!peaks.SequenceEqual(_peaks))
        {
            _peaks = peaks;
            NotifyChanged("peaks");
        }
        SetField(ref _peak, values.Length == 0 ? 0 : values.Max(), nameof(Peak));
        var layout = _node!.Node.Channels.Length == values.Length ? _node.Node.Channels : ChannelLayout.FromFormat(null, values.Length);
        if (_channels.Count != layout.Length || !_channels.Select(c => (int)c!).SequenceEqual(layout))
        {
            _channels = layout.Select(c => (object?)c).ToList();
            NotifyChanged("channels");
        }
    }

    private void Clear()
    {
        if (_peaks.Count > 0)
        {
            _peaks = [];
            NotifyChanged("peaks");
        }
        if (_channels.Count > 0)
        {
            _channels = [];
            NotifyChanged("channels");
        }
        SetField(ref _peak, 0, nameof(Peak));
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _keepRunning?.Dispose();
        _keepRunning = null;
        _meter = null;
    }

    protected override void OnDestroy() => Stop();
}

/// <summary>PipeWire's channel positions, with the values of <c>spa_audio_channel</c>.</summary>
internal static class AudioChannels
{
    public static readonly KeyValuePair<string, int>[] Values =
    [
        new("Unknown", 0), new("NA", 1), new("Mono", 2), new("FrontLeft", 3), new("FrontRight", 4), new("FrontCenter", 5),
        new("LowFrequencyEffects", 6), new("SideLeft", 7), new("SideRight", 8), new("FrontLeftCenter", 9),
        new("FrontRightCenter", 10), new("RearCenter", 11), new("RearLeft", 12), new("RearRight", 13), new("TopCenter", 14),
        new("TopFrontLeft", 15), new("TopFrontCenter", 16), new("TopFrontRight", 17), new("TopRearLeft", 18),
        new("TopRearCenter", 19), new("TopRearRight", 20), new("RearLeftCenter", 21), new("RearRightCenter", 22),
        new("FrontLeftWide", 23), new("FrontRightWide", 24), new("LowFrequencyEffects2", 25), new("FrontLeftHigh", 26),
        new("FrontCenterHigh", 27), new("FrontRightHigh", 28), new("TopFrontLeftCenter", 29), new("TopFrontRightCenter", 30),
        new("TopSideLeft", 31), new("TopSideRight", 32), new("LowFrequencyEffectsLeft", 33), new("LowFrequencyEffectsRight", 34),
        new("BottomCenter", 35), new("BottomLeftCenter", 36), new("BottomRightCenter", 37), new("AuxRangeStart", 0x1000),
        new("AuxRangeEnd", 0x1FFF), new("CustomRangeStart", 0x10000),
    ];
}
