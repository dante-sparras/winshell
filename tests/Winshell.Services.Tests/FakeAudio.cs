using System.Runtime.InteropServices;
using Winshell.Audio;

namespace Winshell.Services.Tests;

/// <summary>A graph a test changes by hand, raising what the real one raises in the order it does.</summary>
internal sealed class FakeAudioGraph : IAudioGraph
{
    private readonly List<AudioGraphNode> _nodes = [];
    private readonly List<AudioGraphLink> _links = [];
    private int _nextLink = 1;

    public IReadOnlyList<AudioGraphNode> Nodes => _nodes;

    public IReadOnlyList<AudioGraphLink> Links => _links;

    public bool Ready => true;

    public AudioGraphNode? DefaultPlayback { get; private set; }

    public AudioGraphNode? DefaultRecording { get; private set; }

    public List<AudioGraphNode> DefaultsAsked { get; } = [];

    public event Action<AudioGraphNode>? NodeAdded;

    public event Action<AudioGraphNode>? NodeRemoved;

    public event Action<AudioGraphLink>? LinkAdded;

    public event Action<AudioGraphLink>? LinkRemoved;

    public event Action? DefaultsChanged;

    public void SetDefault(AudioGraphNode node) => DefaultsAsked.Add(node);

    public FakeAudioNode Add(FakeAudioNode node)
    {
        _nodes.Add(node);
        NodeAdded?.Invoke(node);
        return node;
    }

    public void Remove(FakeAudioNode node)
    {
        _nodes.Remove(node);
        NodeRemoved?.Invoke(node);
    }

    public AudioGraphLink Link(AudioGraphNode source, AudioGraphNode target)
    {
        var link = new AudioGraphLink(_nextLink++, source, target);
        _links.Add(link);
        LinkAdded?.Invoke(link);
        return link;
    }

    public void Unlink(AudioGraphLink link)
    {
        _links.Remove(link);
        LinkRemoved?.Invoke(link);
    }

    public void SetDefaults(AudioGraphNode? playback, AudioGraphNode? recording)
    {
        DefaultPlayback = playback;
        DefaultRecording = recording;
        DefaultsChanged?.Invoke();
    }
}

internal sealed class FakeAudioNode : AudioGraphNode
{
    private static int _nextId;

    public FakeAudioNode(AudioNodeType type, string description = "Node", int[]? channels = null)
        : base(Interlocked.Increment(ref _nextId))
    {
        Type = (int)type;
        Description = description;
        Name = description;
        Channels = channels ?? [3, 4];
        ApplyAudio(new float[Channels.Length], false);
    }

    public FakeMeter? FakeMeter { get; set; }

    public bool FailWrites { get; set; }

    public int Running { get; private set; }

    public override IAudioMeterInformation? Meter => FakeMeter;

    public void SetAudio(float[] volumes, bool muted) => ApplyAudio(volumes, muted);

    public void Rename(string description)
    {
        Description = description;
        ApplyProperties();
    }

    public override void SetVolumes(IReadOnlyList<float> volumes)
    {
        ThrowIfFailing();
        ApplyAudio([.. volumes], Muted);
    }

    public override void SetMuted(bool muted)
    {
        ThrowIfFailing();
        ApplyAudio([.. Volumes], muted);
    }

    public override IDisposable? KeepRunning()
    {
        Running++;
        return new DisposeAction(() => Running--);
    }

    private void ThrowIfFailing()
    {
        if (FailWrites)
            throw new COMException("the device is gone");
    }
}

internal sealed class FakeMeter(params float[] peaks) : IAudioMeterInformation
{
    public float[] Peaks { get; set; } = peaks;

    public bool Gone { get; set; }

    public int Reads { get; private set; }

    public int GetPeakValue(out float peak)
    {
        peak = Peaks.Max();
        return 0;
    }

    public int GetMeteringChannelCount(out uint count)
    {
        Reads++;
        if (Gone)
            throw new COMException("the device is gone");
        count = (uint)Peaks.Length;
        return 0;
    }

    public int GetChannelsPeakValues(uint count, float[] peaks)
    {
        Peaks.CopyTo(peaks, 0);
        return 0;
    }
}
