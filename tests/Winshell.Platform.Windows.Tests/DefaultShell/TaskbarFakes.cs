using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

// Stand-ins for the window list, the app index and the notification area, for the taskbar's tests.

public sealed class FakeWindowList : QObject
{
    private FakeAppWindow? _active;

    public FakeWindowList() => Windows = FakeModel.Of(this);

    [Qml] public ObjectModel Windows { get; }
    [Qml] public FakeAppWindow? ActiveWindow { get { Track(); return _active; } }

    /// <summary>Puts a window in front, as Windows does when the user switches to it.</summary>
    public void Activate(FakeAppWindow? window)
    {
        SetField(ref _active, window, nameof(ActiveWindow));
        foreach (var w in Windows.Values.OfType<FakeAppWindow>())
            w.Activated = w == window;
    }
}

public sealed class FakeAppWindow(string title, string appId, string executable = "") : QObject
{
    private string _title = title;
    private bool _activated;
    private bool _attention;
    private bool _minimized;

    public List<string> Calls { get; } = [];

    [Qml] public string Title { get { Track(); return _title; } internal set => SetField(ref _title, value); }
    [Qml] public string AppId => appId;
    [Qml] public string Executable => executable;
    [Qml] public string Icon => executable;
    [Qml] public bool Activated { get { Track(); return _activated; } internal set => SetField(ref _activated, value); }
    [Qml] public bool Attention { get { Track(); return _attention; } internal set => SetField(ref _attention, value); }
    [Qml] public IReadOnlyList<ShellScreen> Screens { get; internal set; } = [];

    [Qml]
    public bool Minimized
    {
        get { Track(); return _minimized; }
        set
        {
            Calls.Add(value ? "minimize" : "restore");
            SetField(ref _minimized, value);
        }
    }

    [Qml] public void Activate() => Calls.Add("activate");

    [Qml] public void Close() => Calls.Add("close");
}

public sealed class FakeApps : QObject
{
    private readonly List<FakeAppEntry> _entries = [];

    public FakeApps()
    {
        Apps = FakeModel.Of(this);
        Recent = FakeModel.Of(this);
        Frequent = FakeModel.Of(this);
    }

    [Qml] public ObjectModel Apps { get; }
    [Qml] public ObjectModel Recent { get; }
    [Qml] public ObjectModel Frequent { get; }

    /// <summary>Apps whose name starts with the query. A blank query matches nothing, as in <c>Apps.search</c>.</summary>
    [Qml]
    public IReadOnlyList<FakeAppEntry> Search(string query)
    {
        TrackQml("apps");
        return string.IsNullOrWhiteSpace(query) ? [] : _entries.Where(e => e.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public FakeAppEntry Add(string id, string name, string appId, string? icon = null)
    {
        var entry = new FakeAppEntry(id, name, appId, icon);
        _entries.Add(entry);
        Apps.Add(entry);
        NotifyChanged("apps");
        return entry;
    }

    /// <summary>By id, then by the app id a window carries, then by name, as <c>Apps.find</c> looks.</summary>
    [Qml]
    public FakeAppEntry? Find(string name)
    {
        TrackQml("apps");
        return _entries.FirstOrDefault(e => string.Equals(e.Id, name, StringComparison.OrdinalIgnoreCase))
            ?? _entries.FirstOrDefault(e => string.Equals(e.AppId, name, StringComparison.OrdinalIgnoreCase))
            ?? _entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class FakeAppEntry(string id, string name, string appId, string? icon = null) : QObject
{
    public int Launches { get; private set; }

    [Qml] public string Id => id;
    [Qml] public string Name => name;
    [Qml] public string AppId => appId;
    [Qml] public string Icon => icon ?? id;

    [Qml] public void Launch() => Launches++;
}

/// <summary>
/// Recent documents are absent. A test that wants one sets <c>recentFiles</c> on the view,
/// so a harness never reads this PC's Recent folder.
/// </summary>
public sealed class FakeRecentDocuments : QObject
{
    [Qml]
    public IReadOnlyList<Dictionary<string, object?>> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        return [];
    }
}

public sealed class FakeSystemTray : QObject
{
    public FakeSystemTray() => Items = FakeModel.Of(this);

    [Qml] public ObjectModel Items { get; }
}

public sealed class FakeTrayItem(string appName, string tooltip, bool overflow) : QObject
{
    public List<string> Calls { get; } = [];

    [Qml] public string AppName => appName;
    [Qml] public string Tooltip => tooltip;
    [Qml] public bool Overflow => overflow;
    [Qml] public string Icon => "";

    [Qml] public void Click() => Calls.Add("click");

    [Qml] public void DoubleClick() => Calls.Add("doubleClick");

    [Qml] public void MiddleClick() => Calls.Add("middleClick");

    [Qml] public void Display(ShellWindow? parentWindow, double relativeX, double relativeY) => Calls.Add("display");
}
