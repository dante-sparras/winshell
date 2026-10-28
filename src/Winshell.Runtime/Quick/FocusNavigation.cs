using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Quick;

/// <summary>
/// Where the keyboard moves focus, as Windows does: Tab and Shift+Tab through the tab stops in item order, and the
/// arrow keys within a group. A group is one tab stop: radio buttons or tabs that exclude each other, the items of a
/// menu, the rows of a combo box's list, or the delegates of a list view.
/// </summary>
public static class FocusNavigation
{
    private const int KeyLeft = 0x01000012, KeyUp = 0x01000013, KeyRight = 0x01000014, KeyDown = 0x01000015;

    /// <summary>Whether Tab can move focus to the item.</summary>
    public static bool IsTabStop(Item item) =>
        !item.IsDestroyed && item.ActiveFocusOnTab && item.Visible && item.Enabled && item.Opacity > 0;

    /// <summary>The item Tab moves within: the topmost modal popup, else the popup focus is in, else the window.</summary>
    public static Item Scope(Item root, Item? focus)
    {
        if (root.Host is not { } host)
            return root;
        if (PopupOverlay.OpenIn(host).LastOrDefault(p => p.Modal) is { } modal)
            return modal.PopupItem;
        for (var item = focus; item is not null; item = item.ParentItem)
        {
            if (item is PopupItem { ParentItem: PopupOverlay } popup)
                return popup;
        }
        return root;
    }

    /// <summary>The tab stops under an item in tab order, with each group as the one member Tab enters it at.</summary>
    public static List<Item> TabStops(Item scope)
    {
        var stops = new List<Item>();
        using (DependencyTracker.Suspend())
            Collect(scope, stops, []);
        return stops;
    }

    private static void Collect(Item item, List<Item> stops, HashSet<object> groups)
    {
        foreach (var child in item.PaintOrder)
        {
            if (!child.IsVisibleFlag || child.Opacity <= 0)
                continue;
            if (IsTabStop(child))
            {
                if (GroupOf(child) is not { } group)
                    stops.Add(child);
                else if (groups.Add(group.Key) && group.Entry is { } entry)
                    stops.Add(entry);
            }
            Collect(child, stops, groups);
        }
    }

    /// <summary>The tab stop after (or before) the focused item, wrapping around, or null when there is none.</summary>
    public static Item? Next(Item root, Item? focus, bool forward)
    {
        var stops = TabStops(Scope(root, focus));
        if (stops.Count == 0)
            return null;
        using var _ = DependencyTracker.Suspend();
        var key = focus is null ? null : GroupOf(focus)?.Key;
        var at = focus is null ? -1 : stops.FindIndex(s => s == focus || (key is not null && GroupOf(s)?.Key == key));
        if (at < 0)
            return forward ? stops[0] : stops[^1];
        return stops[(at + (forward ? 1 : stops.Count - 1)) % stops.Count];
    }

    /// <summary>
    /// The member of the focused item's group an arrow key moves to, and what moving there selects; null when the
    /// item is in no group, the key is not along the group, or the group ends there.
    /// </summary>
    public static (Item Target, Action<Item>? Select)? Arrow(Item focus, int key)
    {
        using var _ = DependencyTracker.Suspend();
        if (GroupOf(focus) is not { } group)
            return null;
        var step = key switch
        {
            KeyUp when group.Vertical => -1,
            KeyDown when group.Vertical => 1,
            KeyLeft when group.Horizontal => -1,
            KeyRight when group.Horizontal => 1,
            _ => 0,
        };
        var at = group.Members.IndexOf(focus);
        if (step == 0 || at < 0)
            return null;
        var next = at + step;
        if (next < 0 || next >= group.Members.Count)
        {
            if (!group.Wraps)
                return null;
            next = (next + group.Members.Count) % group.Members.Count;
        }
        return (group.Members[next], group.Select);
    }

    private sealed record Group(object Key, List<Item> Members, Item? Entry, bool Vertical, bool Horizontal, bool Wraps, Action<Item>? Select);

    private static Group? GroupOf(Item item)
    {
        switch (item)
        {
            case AbstractButton { Group: { Exclusive: true } buttons }:
                return Exclusive(buttons, buttons.ButtonList);
            case AbstractButton { Checkable: true, AutoExclusive: true, Group: null } when item.ParentItem is { } parent:
                return Exclusive(parent, parent.ChildItems.OfType<AbstractButton>().Where(b => b is { Checkable: true, AutoExclusive: true, Group: null }));
            case ComboBox.Row row when item.ParentItem is { } column:
                {
                    var rows = column.ChildItems.Where(IsTabStop).ToList();
                    var current = rows.OfType<ComboBox.Row>().FirstOrDefault(r => r.IsCurrent);
                    return new Group(column, rows, current ?? rows.FirstOrDefault(), true, false, false, null);
                }
        }
        if (item.ParentItem is MenuColumn menu)
        {
            var items = menu.ChildItems.Where(IsTabStop).ToList();
            return new Group(menu, items, items.FirstOrDefault(), true, false, true, null);
        }
        if (item.ParentItem is { ParentItem: ListView list } content && content == list.ContentItem)
        {
            var delegates = Enumerable.Range(0, list.Count).Select(list.ItemAtIndex).OfType<Item>().Where(IsTabStop).ToList();
            var current = list.ItemAtIndex(list.CurrentIndex) is { } c && delegates.Contains(c) ? c : delegates.FirstOrDefault();
            var vertical = list.Orientation == (int)ListView.OrientationValue.Vertical;
            return new Group(list, delegates, current, vertical, !vertical, list.KeyNavigationWraps, member =>
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list.ItemAtIndex(i) == member)
                    {
                        list.CurrentIndex = i;
                        list.PositionViewAtIndex(i, (int)ListView.PositionMode.Contain);
                        return;
                    }
                }
            });
        }
        return null;
    }

    private static Group Exclusive(object key, IEnumerable<AbstractButton> buttons)
    {
        var members = buttons.Where(IsTabStop).Cast<Item>().ToList();
        var entry = members.OfType<AbstractButton>().FirstOrDefault(b => b.Checked) ?? members.FirstOrDefault();
        // Selection follows focus in a radio group or a tab bar.
        return new Group(key, members, entry, true, true, true, member =>
        {
            if (member is AbstractButton { Checked: false } button)
                button.Click();
        });
    }
}
