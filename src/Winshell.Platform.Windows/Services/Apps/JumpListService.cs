using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Apps;

/// <summary>
/// The <c>JumpLists</c> singleton in <c>Winshell.Apps</c>. <c>forApp</c> reads the recent and frequent documents
/// Windows will give for an app. It does not open them, pin them, or change the list.
/// </summary>
public sealed class JumpListService : QObject
{
    private readonly IJumpListReader _reader;
    private readonly Dictionary<string, AppJumpList> _byId = new(StringComparer.OrdinalIgnoreCase);

    public JumpListService(QmlEngine engine)
        : this(engine, WindowsJumpListReader.Instance)
    {
    }

    internal JumpListService(QmlEngine engine, IJumpListReader reader)
    {
        Engine = engine;
        _reader = reader;
    }

    /// <summary>
    /// The recent and frequent documents for an AppUserModelID. A blank id is not read. The same object is returned
    /// for an id, with its properties updated from the latest read.
    /// </summary>
    [Qml]
    public AppJumpList ForApp(string appId)
    {
        TrackQml("jumpLists");
        appId ??= "";
        if (!_byId.TryGetValue(appId, out var list))
        {
            list = new AppJumpList { Engine = Engine };
            _byId[appId] = list;
        }
        else
        {
            list.Engine ??= Engine;
        }
        if (string.IsNullOrWhiteSpace(appId))
            list.Apply(false, []);
        else
        {
            var read = _reader.Read(appId);
            list.Apply(read.Available, read.Names);
        }
        return list;
    }
}

/// <summary>One read of an app's jump list. QML gets it from <c>JumpLists.forApp</c> and cannot create one.</summary>
public sealed class AppJumpList : QObject
{
    private bool _available;
    private IReadOnlyList<string> _destinations = [];

    /// <summary>False when the recent and frequent lists cannot be read.</summary>
    [Qml] public bool Available { get { Track(); return _available; } }

    /// <summary>Document names, recent first. Empty when the list cannot be read, or when it was read and has none.</summary>
    [Qml] public IReadOnlyList<string> Destinations { get { Track(); return _destinations; } }

    internal void Apply(bool available, IReadOnlyList<string> destinations)
    {
        SetField(ref _available, available, nameof(Available));
        if (_destinations.Count == destinations.Count && _destinations.SequenceEqual(destinations, StringComparer.Ordinal))
            return;
        SetField(ref _destinations, destinations.ToArray(), nameof(Destinations));
    }
}
