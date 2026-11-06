namespace Winshell.Audio;

/// <summary>A node of the graph: a device or an app's session, with its volume.</summary>
internal abstract class AudioGraphNode(int id)
{
    private float[] _volumes = [];

    public int Id { get; } = id;

    public string Name { get; protected set; } = "";

    public string Description { get; protected set; } = "";

    public string InterfaceName { get; protected set; } = "";

    public AudioNodeType Type { get; protected set; }

    /// <summary>True for a playback device and for an app session that plays to one.</summary>
    public bool IsPlayback => Type is AudioNodeType.PlaybackDevice or AudioNodeType.PlaybackSession;

    /// <summary>True for an app's audio session, false for a device.</summary>
    public bool IsSession => Type is AudioNodeType.PlaybackSession or AudioNodeType.RecordingSession;

    /// <summary>The device a session is on. Null for a device.</summary>
    public virtual AudioGraphNode? Device => null;

    /// <summary>True while a session plays or records, false while it is open but idle. A device stays false.</summary>
    public bool Active { get; private set; }

    public Dictionary<string, string> Properties { get; protected set; } = [];

    public AudioChannel[] Channels { get; protected set; } = [];

    public IReadOnlyList<float> Volumes => _volumes;

    public bool Muted { get; private set; }

    /// <summary>Volume, mute or channels changed.</summary>
    public event Action? AudioChanged;

    public event Action? PropertiesChanged;

    public event Action? ActiveChanged;

    public abstract void SetVolumes(IReadOnlyList<float> volumes);

    public abstract void SetMuted(bool muted);

    /// <summary>The peak meter of the node, or null when it has none.</summary>
    public abstract IAudioMeterInformation? Meter { get; }

    /// <summary>Keeps the node's audio running while peaks are read, for devices that only meter while in use.</summary>
    public virtual IDisposable? KeepRunning()
    {
        return null;
    }

    protected void ApplyAudio(float[] volumes, bool muted)
    {
        if (volumes.AsSpan().SequenceEqual(_volumes) && muted == Muted)
            return;
        _volumes = volumes;
        Muted = muted;
        AudioChanged?.Invoke();
    }

    protected void ApplyProperties()
    {
        PropertiesChanged?.Invoke();
    }

    protected void ApplyActive(bool active)
    {
        if (active == Active)
            return;
        Active = active;
        ActiveChanged?.Invoke();
    }
}
