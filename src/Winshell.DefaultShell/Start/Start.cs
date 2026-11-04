using System.Collections.Specialized;
using Winshell.Apps;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The apps pinned to Start, shared by every taskbar. Pins live in <c>launcher.toml</c>. An empty pins array is a
/// choice to pin nothing. A missing file, or a file with no pins array, is not: once apps are known, Start writes
/// its defaults. A file that does not parse shows those defaults and is left unchanged.
/// </summary>
public sealed class Start : ObservableObject, IDisposable
{
    private readonly StartPins _pins;
    private readonly IEventLoop _loop;
    private readonly Func<string, IReadOnlyList<StartApp>> _search;
    private readonly List<string> _ids = [];
    private AppsService? _appsService;
    private IReadOnlyList<StartApp> _apps = [];
    private IReadOnlyList<StartApp> _recent = [];
    private bool _appsRead;
    private int _reading;
    private bool _choice;
    private bool _disposed;

    public Start(string path, IEventLoop loop, Log log, Func<string, IReadOnlyList<StartApp>> search)
    {
        _loop = loop;
        _search = search;
        _pins = new StartPins(path, loop, log);
        _pins.PropertyChanged += OnFile;
        Read();
    }

    public IReadOnlyList<StartApp> Pinned { get; private set => Set(ref field, value); } = [];

    public IReadOnlyList<StartApp> Recommended { get; private set => Set(ref field, value); } = [];

    public IReadOnlyList<StartApp> AllApps { get; private set => Set(ref field, value); } = [];

    /// <summary>Why <c>launcher.toml</c> could not be read, or empty when the last read parsed or the file is missing.</summary>
    public string Error => _pins.Error;

    /// <summary>Recommended apps under the pins. Off shows the pins alone.</summary>
    public bool ShowRecommended
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Publish();
        }
    } = true;

    /// <summary>Pins, the lists, or the file changed.</summary>
    public event EventHandler? Updated;

    /// <summary>Follows the app index. The scan that fills it does not run on the caller.</summary>
    public void Use(AppsService apps)
    {
        if (_appsService is not null)
            return;
        _appsService = apps;
        apps.Apps.CollectionChanged += OnApps;
        apps.Recent.CollectionChanged += OnApps;
        ReadApps();
    }

    public void SetApps(IReadOnlyList<StartApp> apps, IReadOnlyList<StartApp> recent)
    {
        _apps = apps;
        _recent = recent;
        if (!_choice)
            TrySeed();
        Publish();
    }

    public void TogglePin(string id) => ApplyChoice(StartList.TogglePin(_ids, id));

    /// <summary>Moves a pin past the one <paramref name="offset"/> rows away. A pin Start cannot show keeps its place.</summary>
    public bool MoveShown(string id, int offset)
    {
        var shown = new string[Pinned.Count];
        for (var i = 0; i < Pinned.Count; i++)
            shown[i] = Pinned[i].Id;
        var next = SettingsOrder.Move(_ids, shown, id, offset);
        if (next is null)
            return false;
        ApplyChoice(next);
        return true;
    }

    /// <summary>Unpins an app. An id Start cannot show stays in the file.</summary>
    public void UnpinShown(string id)
    {
        if (!IsPinned(id))
            return;
        ApplyChoice(SettingsOrder.Remove(_ids, id));
    }

    // A choice. Written only when the file parses, so a broken launcher.toml is left as it is.
    private void ApplyChoice(IReadOnlyList<string> ids)
    {
        _choice = true;
        _ids.Clear();
        _ids.AddRange(ids);
        if (_pins.Error.Length == 0)
            AssignPins();
        Publish();
    }

    public bool IsPinned(string id) => StartList.IsPinned(_ids, id);

    public IReadOnlyList<StartApp> Search(string query) => _search(query);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_appsService is not null)
        {
            _appsService.Apps.CollectionChanged -= OnApps;
            _appsService.Recent.CollectionChanged -= OnApps;
        }
        _pins.PropertyChanged -= OnFile;
        _pins.Dispose();
    }

    // The lists change one app at a time. Read once they are done, or the first app of a scan becomes the default pins.
    private void OnApps(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_appsRead)
            return;
        _appsRead = true;
        _loop.Post(() =>
        {
            _appsRead = false;
            if (!_disposed)
                ReadApps();
        });
    }

    private void ReadApps()
    {
        if (_appsService is not { } apps)
            return;
        SetApps(apps.Apps.Select(Map).ToList(), apps.Recent.Select(Map).ToList());
    }

    internal static StartApp Map(AppEntry app) => new(app.Id, app.Name, app.AppId, app.Icon, app.Target, app.Packaged, app.Hidden);

    private void OnFile(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_reading > 0 || _disposed)
            return;
        if (e.PropertyName is nameof(StartPins.Pins) or nameof(StartPins.Error) or nameof(StartPins.Loaded))
            Read();
    }

    /// <summary>
    /// Takes the file as it is. A pins array, including <c>[]</c>, is a choice. Anything else shows the defaults once
    /// apps are known, and writes them only when the file itself is fine.
    /// </summary>
    private void Read()
    {
        if (_disposed)
            return;
        _reading++;
        try
        {
            if (_pins.Error.Length > 0)
            {
                // Show the defaults, and do not write them over a file that does not parse. The next good read replaces them.
                _choice = false;
                TrySeed();
            }
            else if (_pins.Pins is { } list)
            {
                _choice = true;
                _ids.Clear();
                foreach (var item in list)
                {
                    if (item is string id)
                        _ids.Add(id);
                }
            }
            else
                TrySeed();
        }
        finally
        {
            _reading--;
        }
        Publish();
    }

    private void TrySeed()
    {
        if (_choice || StartList.Visible(_apps).Count == 0)
            return;
        _ids.Clear();
        _ids.AddRange(StartList.DefaultPins(_apps));
        _choice = true;
        if (_pins.Error.Length == 0)
            AssignPins();
    }

    private void AssignPins()
    {
        _reading++;
        try
        {
            _pins.Pins = _ids.Select(static id => (object?)id).ToArray();
        }
        finally
        {
            _reading--;
        }
    }

    private void Publish()
    {
        if (_disposed)
            return;
        Pinned = StartList.Pinned(_apps, _ids);
        Recommended = ShowRecommended ? StartList.Recommended(_recent, _ids) : [];
        AllApps = StartList.Visible(_apps);
        Updated?.Invoke(this, EventArgs.Empty);
    }
}
