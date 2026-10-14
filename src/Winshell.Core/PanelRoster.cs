using Winshell.Core.Diagnostics;

namespace Winshell.Core;

/// <summary>
/// The panels a chord, Tab, or <c>winoma summon</c> can open, and the opening of one.
/// A panel chord and Tab reach a plugin whose manifest declares the <c>panel</c> kind. The tray draws a panel too,
/// but its item is not a panel widget, so its manifest leaves the kind out. Call on the UI thread.
/// </summary>
public sealed class PanelRoster
{
    private readonly PluginRuntime _runtime;
    private readonly PanelSession _session;
    private readonly HashSet<string> _panels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _items = new(StringComparer.Ordinal);
    private readonly HashSet<string> _centered = new(StringComparer.Ordinal);
    private readonly HashSet<string> _silent = new(StringComparer.Ordinal);
    private readonly HashSet<string> _menus = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _left = [];
    private IReadOnlyList<string> _center = [];
    private IReadOnlyList<string> _right = [];

    /// <summary>The plugin that draws the bar's own panel, which a right click on the bar itself opens.</summary>
    public const string Bar = "winoma.bar";

    /// <summary>Creates a roster with no bar yet.</summary>
    /// <param name="runtime">The running plugins, which hear the click that opens a panel.</param>
    /// <param name="session">The panel on screen.</param>
    public PanelRoster(PluginRuntime runtime, PanelSession session)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(session);
        _runtime = runtime;
        _session = session;
    }

    /// <summary>
    /// True while the Bar has the keyboard: a Panel it opened is up, whatever opened it. The Bar's own window never takes the keyboard,
    /// and an open Panel holds it for as long as it is up. The Menu opens at the middle of the screen from its own commands, so it is not the Bar's.
    /// </summary>
    public bool BarHasKeyboard => _session.OpenId is { } open && !_menus.Contains(open);

    /// <summary>
    /// True when a <c>panel:</c> chord may name <paramref name="id"/>: that plugin declares a panel,
    /// or a clone whose <see cref="PluginManifest.ClonedFrom"/> is <paramref name="id"/> does.
    /// </summary>
    /// <param name="plugins">The running manifests.</param>
    /// <param name="id">Plugin id.</param>
    /// <returns>False when no manifest in <paramref name="plugins"/> answers for that id with a panel.</returns>
    public static bool Declared(IReadOnlyList<PluginManifest> plugins, string id)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(id);
        foreach (var manifest in plugins)
        {
            var answers = manifest.Id.Equals(id, StringComparison.Ordinal)
                || manifest.ClonedFrom.Length > 0 && manifest.ClonedFrom.Equals(id, StringComparison.Ordinal);
            if (answers && manifest.Kinds.Contains(PluginKind.Panel))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True for a panel plugin with no bar item. Its panel opens at the bar's center, and only its chord or a summon reaches it.</summary>
    /// <param name="manifest">The plugin's manifest.</param>
    /// <returns>True when it declares a panel and no bar widget.</returns>
    public static bool AtCenter(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Kinds.Contains(PluginKind.Panel) && !manifest.Kinds.Contains(PluginKind.BarWidget);
    }

    /// <summary>
    /// True for a plugin that draws the Menu. Its panel opens at the middle of the screen, and it runs whether or not its item is on the bar.
    /// Its own commands open it; a <c>panel:</c> chord and Tab do not reach it.
    /// </summary>
    /// <param name="manifest">The plugin's manifest.</param>
    /// <returns>True when it declares the <c>menu</c> kind.</returns>
    public static bool AtMiddle(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Kinds.Contains(PluginKind.Menu);
    }

    /// <summary>Reads the bar that was just mounted: where each item sits, and which plugins declare a panel.</summary>
    /// <param name="layout">The resolved bar.</param>
    /// <param name="manifests">Discovered manifests.</param>
    public void Mount(ResolvedLayout layout, IEnumerable<PluginManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(manifests);
        _left = Ids(layout.Left);
        var center = Ids(layout.CenterBefore);
        if (layout.Anchor is not null)
        {
            center.Add(layout.Anchor.Id);
        }

        center.AddRange(Ids(layout.CenterAfter));
        _center = center;
        _right = Ids(layout.Right);
        _items.Clear();
        _items.UnionWith(_left);
        _items.UnionWith(_center);
        _items.UnionWith(_right);
        _centered.Clear();
        _centered.UnionWith(layout.Background.Where(manifest => AtCenter(manifest) || AtMiddle(manifest)).Select(manifest => manifest.Id));
        var known = manifests.ToList();
        _panels.Clear();
        _panels.UnionWith(known.Where(manifest => manifest.Kinds.Contains(PluginKind.Panel)).Select(manifest => manifest.Id));
        _menus.Clear();
        _menus.UnionWith(known.Where(AtMiddle).Select(manifest => manifest.Id));
        _silent.Clear();
    }

    /// <summary>
    /// Opens <paramref name="id"/>'s panel as a left click on its item would, or closes it when it is the one open.
    /// A clone that stands in for <paramref name="id"/> opens in its place. A hidden item, a plugin that is not running,
    /// and one that declares no panel do nothing.
    /// </summary>
    /// <param name="id">Plugin id, as a <c>panel:</c> chord names it.</param>
    public void Toggle(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_runtime.Route(id) is { } target && Reached(target))
        {
            Press(target);
        }
    }

    /// <summary>
    /// A right click on the bar itself, outside every item. It opens the bar's own panel, or the clone that stands in for it.
    /// The press of that click has already closed whatever panel was up, so a click that closed the bar's own panel leaves it closed.
    /// A press on another monitor's Bar than the open panel's only dismissed it, so that click opens nothing.
    /// Nothing happens when that plugin is switched off.
    /// </summary>
    /// <param name="press">What <see cref="PanelSession.BarPressed"/> answered for the press of this click.</param>
    /// <returns>True when the bar's panel is open after the click.</returns>
    public bool ClickBar(BarPress press)
    {
        if (press.Only || _runtime.Route(Bar) is not { } target || !Reached(target))
        {
            return false;
        }

        if (string.Equals(press.Closed, target, StringComparison.Ordinal))
        {
            // The close that press noted runs on the next turn. This click may arrive first.
            if (Open(target))
            {
                _session.Close();
            }

            return false;
        }

        Click(target);
        return Open(target);
    }

    /// <summary>Opens the numbered panel item on the right of the bar, or closes it when that panel is the one open.</summary>
    /// <param name="number">1-based index among the shown items that declare a panel. A number past the last does nothing.</param>
    public void ToggleRight(int number)
    {
        var shown = _right.Where(Reached).ToList();
        if (number >= 1 && number <= shown.Count)
        {
            Press(shown[number - 1]);
        }
    }

    /// <summary>
    /// Opens the panel <paramref name="step"/> places from the open one, among the shown panel items of its section, and wraps.
    /// Nothing happens when no panel is open, when the open one has no bar item, or when it is alone in its section.
    /// </summary>
    /// <param name="step">How many panel items to move. Negative walks back.</param>
    public void Cycle(int step)
    {
        if (_session.OpenId is not { } open || step == 0 || Section(open) is not { } section)
        {
            return;
        }

        var shown = section.Where(Reached).ToList();
        var index = shown.IndexOf(open);
        if (index < 0 || shown.Count < 2)
        {
            return;
        }

        var next = (index + step) % shown.Count;
        if (next < 0)
        {
            next += shown.Count;
        }

        if (next != index)
        {
            Click(shown[next]);
        }
    }

    /// <summary>
    /// Opens <paramref name="id"/>'s panel, or leaves it open when it already is, as <c>winoma summon</c> asks. It never closes one.
    /// Any running plugin with a bar item may be asked, so the tray's panel is reached, and so may a panel with no bar item.
    /// The id is exact: a clone is summoned by its own id.
    /// </summary>
    /// <param name="id">Plugin id.</param>
    /// <returns>Null when the panel is open, or the sentence saying why not.</returns>
    public string? Summon(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!_runtime.IsRunning(id) || !_items.Contains(id) && !_centered.Contains(id))
        {
            return id + " has no panel";
        }

        if (Hidden(id))
        {
            return id + " is hidden on the bar";
        }

        // A click on an open panel's own item closes it. Summon only opens.
        if (!Open(id))
        {
            _runtime.Click(id);
        }

        return Open(id) ? null : id + " did not open a panel";
    }

    /// <summary>True when a panel chord or Tab may open <paramref name="id"/>: it declares a panel and its item is not hidden.</summary>
    private bool Reached(string id) => _panels.Contains(id) && !Hidden(id);

    /// <summary>True for a bar item whose face is hidden. A panel with no bar item is never hidden.</summary>
    private bool Hidden(string id) => _items.Contains(id) && !_runtime.Shown(id);

    private bool Open(string id) => string.Equals(_session.OpenId, id, StringComparison.Ordinal);

    private void Press(string id)
    {
        if (Open(id))
        {
            _session.Close();
            return;
        }

        Click(id);
    }

    private void Click(string id)
    {
        _runtime.Click(id);
        if (!Open(id) && _silent.Add(id))
        {
            Log.Warning("Plugin", id + " declares a panel, but a click on it opened none");
        }
    }

    private IReadOnlyList<string>? Section(string id) =>
        _left.Contains(id, StringComparer.Ordinal) ? _left
        : _center.Contains(id, StringComparer.Ordinal) ? _center
        : _right.Contains(id, StringComparer.Ordinal) ? _right
        : null;

    private static List<string> Ids(IReadOnlyList<BarSlot> slots) => slots.Select(slot => slot.Id).ToList();
}
