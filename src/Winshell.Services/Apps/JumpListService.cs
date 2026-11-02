namespace Winshell.Apps;

/// <summary>
/// Reads the recent and frequent documents Windows will give for an app. It does not open them, pin them, or
/// change the list.
/// </summary>
public sealed class JumpListService
{
    private readonly IJumpListReader _reader;
    private readonly Dictionary<string, AppJumpList> _byId = new(StringComparer.OrdinalIgnoreCase);

    internal JumpListService(IJumpListReader? reader = null) => _reader = reader ?? WindowsJumpListReader.Instance;

    /// <summary>
    /// The recent and frequent documents for an AppUserModelID. A blank id is not read. The same object is returned
    /// for an id, with its properties updated from the latest read.
    /// </summary>
    public AppJumpList ForApp(string appId)
    {
        if (!_byId.TryGetValue(appId, out var list))
            _byId[appId] = list = new AppJumpList();
        if (string.IsNullOrWhiteSpace(appId))
        {
            list.Apply(false, []);
        }
        else
        {
            var read = _reader.Read(appId);
            list.Apply(read.Available, read.Names);
        }
        return list;
    }
}

/// <summary>One read of an app's jump list.</summary>
public sealed class AppJumpList : ObservableObject
{
    private bool _available;
    private IReadOnlyList<string> _destinations = [];

    internal AppJumpList()
    {
    }

    /// <summary>False when the recent and frequent lists cannot be read.</summary>
    public bool Available => _available;

    /// <summary>Document names, recent first. Empty when the list cannot be read, or when it was read and has none.</summary>
    public IReadOnlyList<string> Destinations => _destinations;

    internal void Apply(bool available, IReadOnlyList<string> destinations)
    {
        Set(ref _available, available, nameof(Available));
        if (!_destinations.SequenceEqual(destinations, StringComparer.Ordinal))
            Set(ref _destinations, destinations.ToArray(), nameof(Destinations));
    }
}
