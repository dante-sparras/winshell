namespace Winshell.Audio;

/// <summary>
/// The devices and sessions Windows has. <see cref="AudioService"/> owns its graph, and a test gives it its own.
/// </summary>
internal interface IAudioGraph : IDisposable
{
    IReadOnlyList<AudioGraphNode> Nodes { get; }

    bool Ready { get; }

    AudioGraphNode? DefaultPlayback { get; }

    AudioGraphNode? DefaultRecording { get; }

    event Action<AudioGraphNode>? NodeAdded;

    event Action<AudioGraphNode>? NodeRemoved;

    event Action? DefaultsChanged;

    void SetDefault(AudioGraphNode node);
}
