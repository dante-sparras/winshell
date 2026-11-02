namespace Winshell.Audio;

/// <summary>
/// Keeps a list of audio objects, for shells that come from Quickshell, where an object must be tracked before its
/// properties are valid. Windows objects always are, so this only holds the list.
/// </summary>
public sealed class AudioObjectTracker : ObservableObject
{
    private IReadOnlyList<object?> _objects = [];

    public IReadOnlyList<object?> Objects
    {
        get => _objects;
        set => Set(ref _objects, value ?? []);
    }
}
