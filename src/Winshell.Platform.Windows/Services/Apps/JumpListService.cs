using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Apps;

/// <summary>
/// What <see cref="IJumpListReader"/> returned for one app. <see cref="Available"/> is false when the lists cannot
/// be read. An empty <see cref="Names"/> with <see cref="Available"/> true was read and has no documents.
/// </summary>
internal readonly record struct JumpListRead(bool Available, IReadOnlyList<string> Names)
{
    /// <summary>How many names one category contributes. The menu shows at most twice this, recent first.</summary>
    public const int PerCategory = 10;

    public static JumpListRead Missing => new(false, []);

    /// <summary>
    /// Recent names first, then frequent names not already listed. A null category was not read. When both are
    /// null, the list cannot be read. A name that is only whitespace is left out.
    /// </summary>
    public static JumpListRead From(IReadOnlyList<string>? recent, IReadOnlyList<string>? frequent)
    {
        if (recent is null && frequent is null)
            return Missing;
        var names = new List<string>();
        Add(recent);
        Add(frequent);
        return new(true, names);

        void Add(IReadOnlyList<string>? source)
        {
            if (source is null)
                return;
            var taken = 0;
            foreach (var name in source)
            {
                if (taken >= PerCategory)
                    return;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var shown = name.Trim();
                if (names.Exists(have => have.Equals(shown, StringComparison.OrdinalIgnoreCase)))
                    continue;
                names.Add(shown);
                taken++;
            }
        }
    }
}

/// <summary>Reads recent and frequent destinations for an AppUserModelID. It does not launch or change them.</summary>
internal interface IJumpListReader
{
    JumpListRead Read(string appUserModelId);
}

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
