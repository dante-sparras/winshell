namespace Winshell.Audio;

/// <summary>The devices and sessions Windows has. A test gives <see cref="AudioService"/> its own.</summary>
internal interface IAudioGraph
{
    IReadOnlyList<AudioGraphNode> Nodes { get; }

    IReadOnlyList<AudioGraphLink> Links { get; }

    bool Ready { get; }

    AudioGraphNode? DefaultPlayback { get; }

    AudioGraphNode? DefaultRecording { get; }

    event Action<AudioGraphNode>? NodeAdded;

    event Action<AudioGraphNode>? NodeRemoved;

    event Action<AudioGraphLink>? LinkAdded;

    event Action<AudioGraphLink>? LinkRemoved;

    event Action? DefaultsChanged;

    void SetDefault(AudioGraphNode node);
}
