using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Winshell.Kit;

/// <summary>
/// The controls one open panel is built from (ADR 0045). A control takes getters for what can change; the host reads them
/// on every tick and writes only what moved. Every interactive control has a command name, so <c>winoma desktop</c> prints it
/// and <c>winoma summon</c> presses it. A plugin builds anything the Kit lacks from its tokens, and reports it with
/// <see cref="Control{TElement}(TElement, PanelControl)"/> and <see cref="Bind{T}"/>.
/// </summary>
public sealed partial class Kit
{
    private readonly Action<string, Action> _press;
    private readonly Action _rebuild;
    // Held strongly for the panel's life. A WinUI element's managed wrapper can be collected while the element stays on screen,
    // and the panel's children would then come back as new wrappers that a weak table no longer knows.
    private readonly Dictionary<UIElement, Entry> _controls = new(ReferenceEqualityComparer.Instance);
    private readonly List<KitRegion> _regions = [];
    private PanelBindings _scope;

    // The controls reported while one part is built: a region, a keyed list's item, a group of chips.
    // When the part leaves the screen its controls are forgotten with it, so the table does not keep every past row alive.
    private Made? _made;

    /// <summary>Creates a Kit for one panel or bar item. The host does this when the panel opens or the item is placed.</summary>
    /// <param name="press">Runs a control's handler for its command, then lets the host refresh the panel.</param>
    /// <param name="rebuild">Asks the host to build the panel again.</param>
    /// <param name="vertical">True for a bar item on a bar along the left or right edge.</param>
    /// <param name="monitor">For a bar item, the monitor its Bar is on, as <c>winoma desktop</c> numbers the monitors. Null reads as the first.</param>
    public Kit(Action<string, Action> press, Action rebuild, bool vertical = false, Func<int>? monitor = null)
    {
        ArgumentNullException.ThrowIfNull(press);
        ArgumentNullException.ThrowIfNull(rebuild);
        _press = press;
        _rebuild = rebuild;
        _monitor = monitor;
        Vertical = vertical;
        _scope = Bindings;
    }

    // The host's own. A plugin draws the same item on every Bar and is not told which one this is (ADR 0080).
    private readonly Func<int>? _monitor;

    private int Monitor => _monitor?.Invoke() ?? 0;

    /// <summary>True when this Kit draws a bar item on a bar along the left or right edge, so the item stacks.</summary>
    public bool Vertical { get; }

    /// <summary>Every getter in the panel. The host refreshes them on each tick.</summary>
    public PanelBindings Bindings { get; } = new();

    /// <summary>Builds the panel again after this turn, such as when a tab switch changes its shape.</summary>
    public void Rebuild() => _rebuild();

    /// <summary>
    /// Lets go of every control and getter. The host calls this when the panel leaves, so a plugin that still holds
    /// its last Kit does not keep the closed panel's rows and pictures alive with it.
    /// </summary>
    public void Release()
    {
        _controls.Clear();
        Bindings.Clear();

        // A plugin may keep a region, and the search line's box is caught in the type-ahead. Neither holds its rows after this.
        foreach (var region in _regions)
        {
            region.Forget();
        }

        _regions.Clear();
        _typeAhead = null;
        _made = null;
        _focusSeen = null;
    }

    /// <summary>The panel, its rows top to bottom.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>What <see cref="IKitPanel.BuildPanel"/> returns.</returns>
    public KitPanel Panel(params UIElement[] rows) => Panel(0, true, rows);

    /// <summary>The panel at <paramref name="width"/>, its rows top to bottom.</summary>
    /// <param name="width">Width in device-independent pixels. Zero uses the usual width.</param>
    /// <param name="scrolls">False for a fixed shape that never scrolls, such as a month grid.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>What <see cref="IKitPanel.BuildPanel"/> returns.</returns>
    public KitPanel Panel(int width, bool scrolls, params UIElement[] rows) => Panel(width, 0, scrolls, rows);

    /// <summary>The panel at a fixed size, for rows of a fixed shape such as the tray grid.</summary>
    /// <param name="width">Width in device-independent pixels. Zero uses the usual width.</param>
    /// <param name="height">Height in device-independent pixels. Zero fits the rows.</param>
    /// <param name="scrolls">False for a shape that never scrolls.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>What <see cref="IKitPanel.BuildPanel"/> returns.</returns>
    public KitPanel Panel(int width, int height, bool scrolls, params UIElement[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new KitPanel(Stack(rows), width, height, scrolls);
    }

    /// <summary>
    /// Reads <paramref name="read"/> on every refresh and calls <paramref name="write"/> when the value moved, starting now.
    /// Use it for a drawing the Kit does not have. Inside a keyed list item, the binding ends with the item.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="read">A cheap getter.</param>
    /// <param name="write">Puts the value on screen.</param>
    /// <param name="same">How two values compare. Null uses the type's equality.</param>
    public void Bind<T>(Func<T> read, Action<T> write, IEqualityComparer<T>? same = null) =>
        _scope.Add(new PanelBinding<T>(read, write, same));

    /// <summary>Reports <paramref name="element"/> to the CLI as <paramref name="control"/>. Its children are not walked.</summary>
    /// <typeparam name="TElement">The element's type.</typeparam>
    /// <param name="element">What the plugin drew.</param>
    /// <param name="control">How <c>winoma desktop</c> prints it and what pressing it does.</param>
    /// <returns>The same element.</returns>
    public TElement Control<TElement>(TElement element, PanelControl control)
        where TElement : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(control);
        _controls[element] = new Entry(control, Group: false);
        _made?.Elements.Add(element);
        return element;
    }

    /// <summary>
    /// Reports <paramref name="element"/> as a group, such as a month grid: one outline line whose rows are the controls inside it.
    /// </summary>
    /// <typeparam name="TElement">The element's type.</typeparam>
    /// <param name="element">What the plugin drew.</param>
    /// <param name="describe">The group's own line. Its rows are filled from the controls inside.</param>
    /// <returns>The same element.</returns>
    public TElement Group<TElement>(TElement element, Func<PanelLine> describe)
        where TElement : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        _controls[element] = new Entry(new PanelControl("", describe), Group: true);
        _made?.Elements.Add(element);
        return element;
    }

    /// <summary>
    /// Builds part of a panel in a scope of its own, so the plugin can replace it later and drop the getters it read.
    /// Use it for a drawing the plugin swaps out as a whole, such as one row of a page.
    /// </summary>
    /// <param name="build">Builds the part with this Kit.</param>
    /// <returns>The part and the way to drop it.</returns>
    public KitRegion Region(Func<UIElement> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var parent = _scope;
        var group = parent.Add(new PanelBindings());
        var outer = _made;
        var made = Open(outer);
        _scope = group;
        _made = made;
        try
        {
            KitRegion? region = null;
            region = new KitRegion(build(), () =>
            {
                parent.Remove(group);
                Close(made);
                _regions.Remove(region!);
            });
            _regions.Add(region);
            return region;
        }
        finally
        {
            _scope = parent;
            _made = outer;
        }
    }

    /// <summary>Starts a part's list of controls under <paramref name="parent"/>, so the parent's end forgets them too.</summary>
    private static Made Open(Made? parent)
    {
        var made = new Made(parent);
        parent?.Parts.Add(made);
        return made;
    }

    /// <summary>Forgets every control the part reported, and those of the parts inside it. The part can be filled again.</summary>
    private void Empty(Made made)
    {
        foreach (var element in made.Elements)
        {
            _controls.Remove(element);
        }

        made.Elements.Clear();
        foreach (var part in made.Parts)
        {
            part.Parent = null;
            Empty(part);
        }

        made.Parts.Clear();
    }

    /// <summary>The part left the screen: forgets its controls and takes it out of the part it was built in.</summary>
    private void Close(Made made)
    {
        Empty(made);
        made.Parent?.Parts.Remove(made);
        made.Parent = null;
    }

    /// <summary>A handler that runs through the host, so a fault is logged and the panel refreshes after it.</summary>
    /// <param name="command">The control's command name.</param>
    /// <param name="action">What the control does.</param>
    /// <returns>The guarded handler.</returns>
    public Action Pressed(string command, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () => _press(command, action);
    }

    /// <summary>The panel as <c>winoma desktop</c> prints it, top to bottom.</summary>
    /// <param name="root">The panel's content.</param>
    /// <returns>One line per control.</returns>
    public IReadOnlyList<PanelLine> Outline(UIElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var lines = new List<PanelLine>();
        Lines(root, lines);
        return lines;
    }

    /// <summary>The control <paramref name="command"/> presses, or null when the plugin's own handler takes it.</summary>
    /// <param name="root">The panel's content.</param>
    /// <param name="command">The command <c>winoma summon</c> sent.</param>
    /// <returns>The control, or null.</returns>
    public PanelControl? Find(UIElement root, string command)
    {
        ArgumentNullException.ThrowIfNull(root);
        var controls = new List<PanelControl>();
        Controls(root, controls);
        return PanelControl.Find(controls, command);
    }

    /// <summary>
    /// Shows one element per item from <paramref name="items"/>, matched by <paramref name="key"/>. A refresh slides in a new item,
    /// takes a gone one off at once, slides the rest into place, and builds again only an item whose value changed.
    /// </summary>
    /// <typeparam name="T">An item. A record compares by value, which is what decides that it changed.</typeparam>
    /// <param name="items">The items now, read on every refresh.</param>
    /// <param name="key">Who an item is from one refresh to the next, such as a network's name.</param>
    /// <param name="item">Builds an item's element with this Kit.</param>
    /// <param name="same">How two values of an item compare. Null uses the type's equality.</param>
    /// <returns>The list.</returns>
    public UIElement KeyedList<T>(Func<IReadOnlyList<T>> items, Func<T, string> key, Func<T, UIElement> item, IEqualityComparer<T>? same = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(item);
        var compare = same ?? EqualityComparer<T>.Default;
        // Items slide in, out, and into place, as the Windows lists do.
        var host = new Rows
        {
            ChildrenTransitions = [new AddDeleteThemeTransition(), new RepositionThemeTransition()],
        };
        var keys = new List<string>();
        var values = new List<T>();
        var groups = new List<PanelBindings>();
        var parts = new List<Made>();
        var list = _scope.Add(new PanelBindings());

        // Items are built on later refreshes too, when no part is being built. They belong to the part the list was made in.
        var owner = _made;

        UIElement Build(T value, out PanelBindings group, out Made part)
        {
            group = new PanelBindings();
            part = Open(owner);
            var outer = _scope;
            var outerMade = _made;
            _scope = group;
            _made = part;
            try
            {
                return item(value);
            }
            finally
            {
                _scope = outer;
                _made = outerMade;
            }
        }

        void Apply(IReadOnlyList<T> next)
        {
            var nextKeys = next.Select(key).ToList();
            var plan = KeyedPlan.Build(keys, values, nextKeys, next, compare.Equals);
            foreach (var index in plan.Removals)
            {
                // The panel fades a removed item out where it stood, and the items after it would slide up over it.
                // Taken off the screen first, it leaves at once and the rest slide into room that is already empty.
                host.Children[index].Visibility = Visibility.Collapsed;
                host.Children.RemoveAt(index);
                list.Remove(groups[index]);
                Close(parts[index]);
                groups.RemoveAt(index);
                parts.RemoveAt(index);
                keys.RemoveAt(index);
                values.RemoveAt(index);
            }

            foreach (var slot in plan.Slots)
            {
                var value = next[slot.Index];
                if (slot.From is not int from)
                {
                    var element = Build(value, out var group, out var part);
                    host.Children.Insert(slot.Index, element);
                    list.Add(group);
                    groups.Insert(slot.Index, group);
                    parts.Insert(slot.Index, part);
                    keys.Insert(slot.Index, nextKeys[slot.Index]);
                    values.Insert(slot.Index, value);
                    continue;
                }

                if (from != slot.Index)
                {
                    var moving = host.Children[from];
                    var movingGroup = groups[from];
                    var movingPart = parts[from];
                    host.Children.RemoveAt(from);
                    groups.RemoveAt(from);
                    parts.RemoveAt(from);
                    keys.RemoveAt(from);
                    values.RemoveAt(from);
                    host.Children.Insert(slot.Index, moving);
                    groups.Insert(slot.Index, movingGroup);
                    parts.Insert(slot.Index, movingPart);
                    keys.Insert(slot.Index, nextKeys[slot.Index]);
                    values.Insert(slot.Index, value);
                }

                if (slot.Kind == KeyedPlan.Step.Replace)
                {
                    list.Remove(groups[slot.Index]);
                    Close(parts[slot.Index]);
                    var element = Build(value, out var group, out var part);
                    host.Children[slot.Index] = element;
                    list.Add(group);
                    groups[slot.Index] = group;
                    parts[slot.Index] = part;
                }

                values[slot.Index] = value;
            }
        }

        _scope.Add(new PanelBinding<IReadOnlyList<T>>(items, Apply, new Sequence<T>(compare)));
        return host;
    }

    private static Rows Stack(IEnumerable<UIElement> rows)
    {
        var stack = new Rows();
        foreach (var row in rows)
        {
            stack.Children.Add(row);
        }

        return stack;
    }

    /// <summary>
    /// Adds the lines under <paramref name="element"/> to <paramref name="into"/>. With <paramref name="stops"/>, it also notes the element
    /// of every line that has a command, a line before the lines inside it, which is the order <see cref="PanelSelection"/> counts them in.
    /// </summary>
    private void Lines(UIElement element, List<PanelLine> into, List<UIElement>? stops = null)
    {
        // A collapsed control is not on screen, so the CLI does not see it either.
        if (element.Visibility == Visibility.Collapsed)
        {
            return;
        }

        if (_controls.TryGetValue(element, out var entry))
        {
            var line = entry.Control.Describe();
            if (entry.Group)
            {
                if (!string.IsNullOrEmpty(line.Command))
                {
                    stops?.Add(element);
                }

                var rows = new List<PanelLine>();
                foreach (var child in Children(element))
                {
                    Lines(child, rows, stops);
                }

                if (rows.Count > 0)
                {
                    line = line with { Rows = rows };
                }
                else
                {
                    Stops(line.Rows, element, stops);
                }
            }
            else
            {
                Stops([line], element, stops);
            }

            into.Add(line);
            return;
        }

        foreach (var child in Children(element))
        {
            Lines(child, into, stops);
        }
    }

    /// <summary>Notes <paramref name="element"/> once for each of <paramref name="rows"/> that has a command, and for the rows inside them: a control that prints rows of its own draws them all.</summary>
    private static void Stops(IReadOnlyList<PanelLine>? rows, UIElement element, List<UIElement>? stops)
    {
        if (rows is null || stops is null)
        {
            return;
        }

        foreach (var row in rows)
        {
            if (!string.IsNullOrEmpty(row.Command))
            {
                stops.Add(element);
            }

            Stops(row.Rows, element, stops);
        }
    }

    private void Controls(UIElement element, List<PanelControl> into)
    {
        if (element.Visibility == Visibility.Collapsed)
        {
            return;
        }

        if (_controls.TryGetValue(element, out var entry))
        {
            into.Add(entry.Control);
            if (!entry.Group)
            {
                return;
            }
        }

        foreach (var child in Children(element))
        {
            Controls(child, into);
        }
    }

    private static IEnumerable<UIElement> Children(UIElement element) => element switch
    {
        Panel panel => panel.Children,
        Border border when border.Child is not null => [border.Child],
        ContentControl content when content.Content is UIElement inner => [inner],
        Viewbox box when box.Child is not null => [box.Child],
        _ => [],
    };

    private sealed record Entry(PanelControl Control, bool Group);

    /// <summary>The controls one part of a panel reported, and the parts built inside it.</summary>
    private sealed class Made(Made? parent)
    {
        public Made? Parent { get; set; } = parent;

        public List<UIElement> Elements { get; } = [];

        public List<Made> Parts { get; } = [];
    }

    /// <summary>Two item lists are the same when they hold the same items in the same order.</summary>
    private sealed class Sequence<T>(IEqualityComparer<T> item) : IEqualityComparer<IReadOnlyList<T>>
    {
        public bool Equals(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null || left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!item.Equals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(IReadOnlyList<T> obj) => obj.Count;
    }
}
