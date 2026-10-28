using System.Globalization;
using System.Runtime.InteropServices;
using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Services.Audio;

/// <summary>
/// Quickshell's <c>Pipewire</c> singleton, backed by Windows audio: every playback and recording device is a sink
/// or source node, and every app's audio session is a stream node linked to its device.
/// </summary>
public sealed class Pipewire : QObject
{
    private readonly AudioGraph _graph;
    private readonly Dictionary<AudioNode, PwNode> _nodes = [];
    private readonly Dictionary<AudioLink, (PwLink Link, PwLinkGroup Group)> _links = [];

    public Pipewire(QmlEngine engine)
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
    public PwNode? DefaultAudioSink
    {
        get { Track(); return Find(_graph.DefaultSink); }
    }

    [Qml]
    public PwNode? DefaultAudioSource
    {
        get { Track(); return Find(_graph.DefaultSource); }
    }

    /// <summary>
    /// Windows has no separate preference: this reads as the current default device, and setting it makes the node
    /// the Windows default device.
    /// </summary>
    [Qml]
    public PwNode? PreferredDefaultAudioSink
    {
        get { Track(); return Find(_graph.DefaultSink); }
        set => SetDefault(value, sink: true);
    }

    [Qml]
    public PwNode? PreferredDefaultAudioSource
    {
        get { Track(); return Find(_graph.DefaultSource); }
        set => SetDefault(value, sink: false);
    }

    [Qml]
    public bool Ready
    {
        get { Track(); return _graph.Ready; }
    }

    /// <summary>The link groups changed, for <see cref="PwNodeLinkTracker"/>.</summary>
    internal event Action? LinkGroupsChanged;

    internal IEnumerable<PwLinkGroup> AllLinkGroups => _links.Values.Select(l => l.Group);

    internal PwNode? Find(AudioNode? node) => node is not null && _nodes.TryGetValue(node, out var wrapper) ? wrapper : null;

    private void SetDefault(PwNode? node, bool sink)
    {
        if (node?.Node is not EndpointNode endpoint || endpoint.IsSink != sink)
        {
            if (node is not null)
                Engine?.Log.Warn("pipewire", $"Cannot make {node.Description} the default {(sink ? "sink" : "source")}: it is not a {(sink ? "sink" : "source")} device");
            return;
        }
        _graph.SetDefault(endpoint);
    }

    private void OnNodeAdded(AudioNode node)
    {
        var wrapper = new PwNode(this, node) { Engine = Engine };
        wrapper.SetOwner(this);
        _nodes[node] = wrapper;
        Nodes.Add(wrapper);
    }

    private void OnNodeRemoved(AudioNode node)
    {
        if (!_nodes.Remove(node, out var wrapper))
            return;
        Nodes.Remove(wrapper);
        wrapper.Destroy();
    }

    private void OnLinkAdded(AudioLink link)
    {
        if (Find(link.Source) is not { } source || Find(link.Target) is not { } target)
            return;
        var pwLink = new PwLink(link, source, target) { Engine = Engine };
        var group = new PwLinkGroup(link, source, target) { Engine = Engine };
        pwLink.SetOwner(this);
        group.SetOwner(this);
        _links[link] = (pwLink, group);
        Links.Add(pwLink);
        LinkGroups.Add(group);
        LinkGroupsChanged?.Invoke();
    }

    private void OnLinkRemoved(AudioLink link)
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
        var module = registry.Module("Quickshell.Services.Pipewire");
        module.Singleton(e => new Pipewire(e), "Pipewire")
            .Uncreatable<PwNode>("PwNode", "PwNodes cannot be created directly")
            .Uncreatable<PwNodeAudio>("PwNodeAudio", "PwNodeAudio cannot be created directly")
            .Uncreatable<PwLink>("PwLink", "PwLinks cannot be created directly")
            .Uncreatable<PwLinkGroup>("PwLinkGroup", "PwLinkGroups cannot be created directly")
            .Type<PwObjectTracker>("PwObjectTracker")
            .Type<PwNodeLinkTracker>("PwNodeLinkTracker")
            .Type<PwNodePeakMonitor>("PwNodePeakMonitor")
            .Add(StandardModules.EnumType("PwNodeType", [
                new("Untracked", 0), new("Audio", 0b1), new("Video", 0b10), new("Stream", 0b100), new("Source", 0b1000),
                new("Sink", 0b10000), new("AudioSink", 0b10001), new("AudioSource", 0b1001), new("AudioDuplex", 0b11001),
                new("AudioOutStream", 0b10101), new("AudioInStream", 0b1101), new("VideoSource", 0b1010), new("VideoSink", 0b10010)], module.Name))
            .Add(StandardModules.EnumType("PwLinkState", [
                new("Error", -2), new("Unlinked", -1), new("Init", 0), new("Negotiating", 1), new("Allocating", 2),
                new("Paused", 3), new("Active", 4)], module.Name))
            .Add(StandardModules.EnumType("PwAudioChannel", PwAudioChannels.Values, module.Name));
    }
}

/// <summary>Quickshell's <c>PwNode</c>: a device or an app stream.</summary>
public sealed class PwNode : QObject
{
    private readonly AudioNode _node;

    internal PwNode(Pipewire service, AudioNode node)
    {
        Service = service;
        _node = node;
        Audio = new PwNodeAudio(node) { Engine = service.Engine };
        Audio.SetOwner(this);
        node.PropertiesChanged += OnPropertiesChanged;
    }

    internal Pipewire Service { get; }

    internal AudioNode Node => _node;

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
    public bool IsStream => (_node.Type & PwNodeTypes.Stream) != 0;

    /// <summary>The <c>PwNodeType</c> flags.</summary>
    [Qml]
    public int Type => _node.Type;

    [Qml]
    public IReadOnlyDictionary<string, object?> Properties
    {
        get { Track(); return _node.Properties.ToDictionary(kv => kv.Key, kv => (object?)kv.Value); }
    }

    /// <summary>Every Windows node carries audio.</summary>
    [Qml]
    public PwNodeAudio Audio { get; }

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

/// <summary>Quickshell's <c>PwNodeAudio</c>: a node's volume, per channel, and mute.</summary>
public sealed class PwNodeAudio : QObject
{
    private readonly AudioNode _node;

    internal PwNodeAudio(AudioNode node)
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
    /// is what Quickshell's volume means too. Windows cannot go above 1.0.
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

    /// <summary>The <c>PwAudioChannel</c> of each entry of <see cref="Volumes"/>.</summary>
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
            Engine?.Log.Warn("pipewire", $"Cannot change the volume of {_node.Description}: {e.Message}");
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

/// <summary>Quickshell's <c>PwLink</c>: a stream's connection to its device.</summary>
public sealed class PwLink : QObject
{
    private readonly AudioLink _link;

    internal PwLink(AudioLink link, PwNode source, PwNode target)
    {
        _link = link;
        Source = source;
        Target = target;
        link.StateChanged += OnStateChanged;
    }

    [Qml]
    public int Id => _link.Id;

    [Qml]
    public PwNode Source { get; }

    [Qml]
    public PwNode Target { get; }

    /// <summary><c>PwLinkState.Active</c> while the app plays or records, <c>Paused</c> while its stream is idle.</summary>
    [Qml]
    public int State
    {
        get { Track(); return _link.Active ? 4 : 3; }
    }

    private void OnStateChanged() => NotifyChanged("state");

    protected override void OnDestroy() => _link.StateChanged -= OnStateChanged;
}

/// <summary>Quickshell's <c>PwLinkGroup</c>: the connections between one source and one target.</summary>
public sealed class PwLinkGroup : QObject
{
    private readonly AudioLink _link;

    internal PwLinkGroup(AudioLink link, PwNode source, PwNode target)
    {
        _link = link;
        Source = source;
        Target = target;
        link.StateChanged += OnStateChanged;
    }

    [Qml]
    public PwNode Source { get; }

    [Qml]
    public PwNode Target { get; }

    [Qml]
    public int State
    {
        get { Track(); return _link.Active ? 4 : 3; }
    }

    private void OnStateChanged() => NotifyChanged("state");

    protected override void OnDestroy() => _link.StateChanged -= OnStateChanged;
}

/// <summary>
/// Quickshell's <c>PwObjectTracker</c>. Pipewire objects must be bound before all their properties are valid;
/// Windows objects always are, so this only keeps the list.
/// </summary>
public sealed class PwObjectTracker : QObject
{
    private IReadOnlyList<object?> _objects = [];

    [Qml]
    public IReadOnlyList<object?> Objects
    {
        get { Track(); return _objects; }
        set => SetField(ref _objects, value ?? []);
    }
}

/// <summary>Quickshell's <c>PwNodeLinkTracker</c>: the link groups connected to one node.</summary>
public sealed class PwNodeLinkTracker : QObject
{
    private PwNode? _node;
    private Pipewire? _service;
    private IDisposable? _nodeDestroyed;
    private IReadOnlyList<PwLinkGroup> _linkGroups = [];

    [Qml]
    public PwNode? Node
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
    public IReadOnlyList<PwLinkGroup> LinkGroups
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
/// Quickshell's <c>PwNodePeakMonitor</c>: the peak level of a node's channels, read from the Windows peak meter
/// about 30 times a second while enabled. Windows meters a device after its volume is applied, unlike Pipewire.
/// </summary>
public sealed class PwNodePeakMonitor : QObject
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(33);

    private PwNode? _node;
    private bool _enabled = true;
    private double _peak;
    private IReadOnlyList<object?> _peaks = [];
    private IReadOnlyList<object?> _channels = [];
    private IDisposable? _timer;
    private IDisposable? _keepRunning;
    private IAudioMeterInformation? _meter;

    [Qml]
    public PwNode? Node
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

/// <summary>Pipewire's channel positions, with the values of <c>spa_audio_channel</c>.</summary>
internal static class PwAudioChannels
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
