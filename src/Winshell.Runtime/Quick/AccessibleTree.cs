using System.Text.RegularExpressions;
using Winshell.Runtime.Quick.Controls;
using Role = Winshell.Runtime.Quick.AccessibleAttached.RoleValue;

namespace Winshell.Runtime.Quick;

public enum ToggleState
{
    Off,
    On,
    Indeterminate,
}

public sealed record RangeInfo(double Value, double Minimum, double Maximum, double SmallChange, double LargeChange, bool IsReadOnly);

/// <summary>How far a scrollable item is scrolled, in percent, as UI Automation's Scroll pattern reports it.</summary>
public sealed record ScrollInfo(double HorizontalPercent, double VerticalPercent, double HorizontalViewSize, double VerticalViewSize)
{
    public bool HorizontallyScrollable => HorizontalViewSize < 100;

    public bool VerticallyScrollable => VerticalViewSize < 100;
}

public enum AccessibleProperty
{
    Name,
    Description,
    IsEnabled,
    ToggleState,
    Value,
    RangeValue,
    IsSelected,
    IsExpanded,
    HorizontalScrollPercent,
    VerticalScrollPercent,
    TextSelection,
}

public enum AccessibleChangeKind
{
    /// <summary>The node's children changed.</summary>
    Children,

    /// <summary>The node joined the tree.</summary>
    Added,

    /// <summary>The node left the tree.</summary>
    Removed,

    Property,
}

public sealed record AccessibleChange(AccessibleChangeKind Kind, AccessibleNode Node, AccessibleProperty Property = default, object? Old = null, object? New = null);

/// <summary>
/// The items of one window as assistive technology sees them. Only items that mean something to a user become
/// nodes: controls, text, lists, popups, and items given a role through <c>Accessible</c>. The children of every
/// other item are hoisted into the nearest node above it. A control's own parts are never exposed.
/// </summary>
public sealed class AccessibleTree
{
    private readonly Dictionary<Item, AccessibleNode> _nodes = [];
    private int _nextId = 1;

    public AccessibleTree(Scene scene)
    {
        Scene = scene;
        Root = new AccessibleNode(this, scene.Root, 0) { Role = RootRole(scene) };
        _nodes[scene.Root] = Root;
    }

    public Scene Scene { get; }

    /// <summary>The window.</summary>
    public AccessibleNode Root { get; }

    /// <summary>The node that stands for an item: the item's own, or the nearest one above it.</summary>
    public AccessibleNode? Find(Item? item)
    {
        for (; item is not null; item = item.ParentItem)
        {
            if (_nodes.TryGetValue(item, out var node))
                return node;
        }
        return null;
    }

    /// <summary>The deepest node at a point in window coordinates.</summary>
    public AccessibleNode NodeAt(double x, double y)
    {
        using var _ = DependencyTracker.Suspend();
        var node = Root;
        while (true)
        {
            var hit = node.Children.LastOrDefault(c => !c.IsOffscreen && c.Bounds is var b && x >= b.X && y >= b.Y && x < b.Right && y < b.Bottom);
            if (hit is null)
                return node;
            node = hit;
        }
    }

    /// <summary>Rebuilds the tree from the items, and returns what changed since the last time.</summary>
    public IReadOnlyList<AccessibleChange> Refresh()
    {
        var changes = new List<AccessibleChange>();
        var seen = new HashSet<AccessibleNode> { Root };
        using (DependencyTracker.Suspend())
        {
            Root.Role = RootRole(Scene);
            Build(Root, seen, changes);
        }
        foreach (var (item, node) in _nodes.ToList())
        {
            if (seen.Contains(node))
                continue;
            _nodes.Remove(item);
            node.IsAlive = false;
            changes.Add(new AccessibleChange(AccessibleChangeKind.Removed, node));
        }
        return changes;
    }

    private void Build(AccessibleNode node, HashSet<AccessibleNode> seen, List<AccessibleChange> changes)
    {
        var children = new List<AccessibleNode>();
        if (!IsLeaf(node.Role))
            Collect(node.Item, node, children, changes);
        if (!children.SequenceEqual(node.ChildList))
        {
            node.ChildList.Clear();
            node.ChildList.AddRange(children);
            changes.Add(new AccessibleChange(AccessibleChangeKind.Children, node));
        }
        foreach (var child in children)
        {
            seen.Add(child);
            Build(child, seen, changes);
        }
        var state = node.Snapshot();
        if (node.LastState is { } last)
            Compare(node, last, state, changes);
        node.LastState = state;
    }

    private void Collect(Item item, AccessibleNode parent, List<AccessibleNode> into, List<AccessibleChange> changes)
    {
        foreach (var child in item.PaintOrder)
        {
            if (!child.IsVisibleFlag || child.Opacity <= 0)
                continue;
            var attached = child.FindAttached(typeof(AccessibleAttached)) as AccessibleAttached;
            var role = attached is { Role: not 0 } ? (Role)attached.Role : DefaultRole(child);
            var exposed = attached is not { Ignored: true } && (role != Role.NoRole || child.ActiveFocusOnTab || child is TextInput);
            if (!exposed)
            {
                Collect(child, parent, into, changes);
                continue;
            }
            if (!_nodes.TryGetValue(child, out var node))
            {
                _nodes[child] = node = new AccessibleNode(this, child, _nextId++);
                changes.Add(new AccessibleChange(AccessibleChangeKind.Added, node));
            }
            node.Role = role;
            node.Parent = parent;
            into.Add(node);
        }
    }

    private static void Compare(AccessibleNode node, AccessibleNode.State a, AccessibleNode.State b, List<AccessibleChange> changes)
    {
        void Check(AccessibleProperty property, object? old, object? now)
        {
            if (!Equals(old, now))
                changes.Add(new AccessibleChange(AccessibleChangeKind.Property, node, property, old, now));
        }
        Check(AccessibleProperty.Name, a.Name, b.Name);
        Check(AccessibleProperty.Description, a.Description, b.Description);
        Check(AccessibleProperty.IsEnabled, a.Enabled, b.Enabled);
        Check(AccessibleProperty.ToggleState, a.Toggle, b.Toggle);
        Check(AccessibleProperty.Value, a.Value, b.Value);
        Check(AccessibleProperty.RangeValue, a.Range, b.Range);
        Check(AccessibleProperty.IsSelected, a.Selected, b.Selected);
        Check(AccessibleProperty.IsExpanded, a.Expanded, b.Expanded);
        Check(AccessibleProperty.HorizontalScrollPercent, a.Scroll?.HorizontalPercent, b.Scroll?.HorizontalPercent);
        Check(AccessibleProperty.VerticalScrollPercent, a.Scroll?.VerticalPercent, b.Scroll?.VerticalPercent);
        Check(AccessibleProperty.TextSelection, a.TextSelection, b.TextSelection);
    }

    private static Role RootRole(Scene scene) => scene.WindowObject is Quickshell.FloatingWindow ? Role.Window : Role.Pane;

    /// <summary>What a built-in item is when QML does not say.</summary>
    internal static Role DefaultRole(Item item) => item switch
    {
        Switch => Role.Switch,
        CheckBox => Role.CheckBox,
        RadioButton => Role.RadioButton,
        TabButton => Role.PageTab,
        MenuItem => Role.MenuItem,
        ItemDelegate => Role.ListItem,
        AbstractButton => Role.Button,
        Slider or Dial => Role.Slider,
        SpinBox => Role.SpinBox,
        ProgressBar or BusyIndicator => Role.ProgressBar,
        ComboBox => Role.ComboBox,
        TextInput => Role.EditableText,
        Text text => text.Text_.Length > 0 ? Role.StaticText : Role.NoRole,
        ListView => Role.List,
        TabBar => Role.PageTabList,
        ToolBar => Role.ToolBar,
        GroupBox => Role.Grouping,
        ToolSeparator or MenuSeparator => Role.Separator,
        PopupItem { Popup: Menu } => Role.PopupMenu,
        PopupItem { Popup: ToolTip } => Role.ToolTip,
        PopupItem { Popup: ComboBoxPopup } => Role.List,
        PopupItem { Popup.Modal: true } => Role.Dialog,
        PopupItem => Role.Pane,
        // A popup's own scrolling is part of the popup.
        Flickable { ParentItem: not PopupItem } => Role.Pane,
        _ => Role.NoRole,
    };

    /// <summary>Whether a node of this role is one thing to the user, so the items inside it are not exposed.</summary>
    internal static bool IsLeaf(Role role) => role is Role.Button or Role.CheckBox or Role.RadioButton or Role.Switch
        or Role.PageTab or Role.MenuItem or Role.StaticText or Role.EditableText or Role.Slider or Role.Dial
        or Role.SpinBox or Role.ProgressBar or Role.ComboBox or Role.Separator or Role.Graphic or Role.Link
        or Role.ToolTip or Role.Heading;
}

/// <summary>One item as assistive technology sees it: what it is, what it says, and what can be done with it.</summary>
public sealed partial class AccessibleNode
{
    internal AccessibleNode(AccessibleTree tree, Item item, int id)
    {
        Tree = tree;
        Item = item;
        Id = id;
    }

    public AccessibleTree Tree { get; }

    public Item Item { get; }

    /// <summary>Unique within the window, and never reused.</summary>
    public int Id { get; }

    public AccessibleNode? Parent { get; internal set; }

    public IReadOnlyList<AccessibleNode> Children => ChildList;

    public int ChildIndex(AccessibleNode child) => ChildList.IndexOf(child);

    internal List<AccessibleNode> ChildList { get; } = [];

    /// <summary>False once the node left the tree.</summary>
    public bool IsAlive { get; internal set; } = true;

    public Role Role { get; internal set; }

    public bool IsRoot => this == Tree.Root;

    internal State? LastState { get; set; }

    private AccessibleAttached? Attached => (IsRoot ? Tree.Scene.WindowObject : Item)?.FindAttached(typeof(AccessibleAttached)) as AccessibleAttached;

    private string ToolTipText => Item.FindAttached(typeof(ToolTipAttached)) is ToolTipAttached tip ? tip.Text : "";

    public string Name
    {
        get
        {
            if (Attached is { Name.Length: > 0 } attached)
                return attached.Name;
            var name = Item switch
            {
                _ when IsRoot => Tree.Scene.WindowObject is Quickshell.FloatingWindow window ? window.Title : "",
                AbstractButton button => button.Text,
                TextField textField => textField.PlaceholderText,
                Text text => PlainText(text),
                GroupBox group => group.Title,
                PopupItem { Popup: ToolTip tip } => tip.Text,
                PopupItem { Popup: Menu menu } => menu.Title,
                _ => "",
            };
            // An icon button is named by its tool tip, as in WinUI; one built from items, by the text inside it.
            if (name.Length == 0 && Item is AbstractButton)
                name = ToolTipText;
            if (name.Length == 0 && Role is Role.Button or Role.CheckBox or Role.RadioButton or Role.Switch or Role.PageTab or Role.MenuItem or Role.ListItem or Role.Link)
                name = string.Join(" ", DescendantText(Item));
            return name;
        }
    }

    /// <summary>Help text: <c>Accessible.description</c>, or a tool tip that is not already the name.</summary>
    public string Description
    {
        get
        {
            if (Attached is { Description.Length: > 0 } attached)
                return attached.Description;
            var tip = ToolTipText;
            return tip.Length > 0 && tip != Name ? tip : "";
        }
    }

    public bool IsEnabled => Item.Enabled;

    public bool IsKeyboardFocusable => FocusNavigation.IsTabStop(Item) || (Item is TextInput && Item.Visible && Item.Enabled);

    public bool HasKeyboardFocus => Tree.Scene.FocusItem == Item;

    public bool IsPassword => Item is TextInput { EchoMode: not 0 };

    /// <summary>Where the item is, in window coordinates.</summary>
    public QRect Bounds
    {
        get
        {
            if (IsRoot)
                return new QRect(0, 0, Item.Width, Item.Height);
            var p = Item.MapToScene(0, 0);
            return new QRect(p.X, p.Y, Item.Width, Item.Height);
        }
    }

    /// <summary>Whether none of the item can be seen: it is scrolled or clipped away, or outside the window.</summary>
    public bool IsOffscreen
    {
        get
        {
            if (IsRoot)
                return false;
            var r = Bounds;
            for (var p = Item.ParentItem; p is not null; p = p.ParentItem)
            {
                if (p.Clip || p.ParentItem is null)
                {
                    var o = p.MapToScene(0, 0);
                    r = Intersect(r, new QRect(o.X, o.Y, p.Width, p.Height));
                }
            }
            return r.Width <= 0 || r.Height <= 0;
        }
    }

    private static QRect Intersect(QRect a, QRect b)
    {
        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        return new QRect(x, y, Math.Min(a.Right, b.Right) - x, Math.Min(a.Bottom, b.Bottom) - y);
    }

    public void Focus()
    {
        if (IsKeyboardFocusable)
            Item.ForceActiveFocus();
    }

    // Invoke

    public bool CanInvoke => Item switch
    {
        AbstractButton { Checkable: false } => Role is not (Role.RadioButton or Role.PageTab),
        AbstractButton => false,
        _ => Attached?.GetSignal("pressAction") is { Count: > 0 },
    };

    public void Invoke()
    {
        if (Item is AbstractButton button)
            button.Click();
        else
            Attached?.Emit("pressAction");
    }

    // Toggle

    public ToggleState? Toggle => Item switch
    {
        CheckBox { CheckState: 1 } => ToggleState.Indeterminate,
        AbstractButton { Checkable: true } button when Role is not (Role.RadioButton or Role.PageTab) =>
            button.Checked ? ToggleState.On : ToggleState.Off,
        _ when Attached is { Checkable: true } attached => attached.Checked ? ToggleState.On : ToggleState.Off,
        _ => null,
    };

    public void DoToggle()
    {
        if (Item is AbstractButton button)
            button.Click();
        else
            Attached?.Emit("toggleAction");
    }

    // RangeValue

    public RangeInfo? Range => Item switch
    {
        Slider s => Info(s.Value, s.From, s.To, s.StepSize, false),
        Dial d => Info(d.Value, d.From, d.To, d.StepSize, false),
        SpinBox s => Info(s.Value, s.From, s.To, s.StepSize, false),
        ProgressBar { Indeterminate: false } p => Info(p.Value, p.From, p.To, 0, true),
        _ => null,
    };

    private static RangeInfo Info(double value, double from, double to, double step, bool readOnly)
    {
        var small = step > 0 ? step : Math.Abs(to - from) / 100;
        return new RangeInfo(value, Math.Min(from, to), Math.Max(from, to), small, Math.Max(small, Math.Abs(to - from) / 10), readOnly);
    }

    public void SetRangeValue(double value)
    {
        switch (Item)
        {
            case Slider s:
                s.Value = value;
                s.Emit("moved");
                break;
            case Dial d:
                d.Value = value;
                d.Emit("moved");
                break;
            case SpinBox s:
                s.Value = (int)Math.Round(value);
                s.Emit("valueModified");
                break;
        }
    }

    // Value

    /// <summary>The text of a field or the shown entry of a combo box. A password field hides its text.</summary>
    public string? Value => Item switch
    {
        TextInput { EchoMode: not 0 } => "",
        TextInput input => input.Text_,
        ComboBox box => box.DisplayText,
        _ => null,
    };

    public bool IsValueReadOnly => Item is not TextInput { ReadOnly: false };

    public void SetValue(string value)
    {
        if (Item is TextInput { ReadOnly: false } input)
            input.Text_ = value;
    }

    // SelectionItem and Selection

    public bool? IsSelected => Item switch
    {
        RadioButton or TabButton => ((AbstractButton)Item).Checked,
        ComboBox.Row row => row.IsCurrent,
        _ when ListOf(Item) is { } list => list.ItemAtIndex(list.CurrentIndex) == Item,
        _ => null,
    };

    public void Select()
    {
        switch (Item)
        {
            case RadioButton or TabButton when !((AbstractButton)Item).Checked:
            case ComboBox.Row:
                ((AbstractButton)Item).Click();
                return;
        }
        if (ListOf(Item) is { } list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list.ItemAtIndex(i) == Item)
                    list.CurrentIndex = i;
            }
        }
    }

    private static ListView? ListOf(Item item) =>
        item.ParentItem is { ParentItem: ListView list } content && content == list.ContentItem ? list : null;

    /// <summary>Whether the node holds items one of which is selected: a tab bar or a list.</summary>
    public bool IsSelectionContainer => Role is Role.PageTabList or Role.List;

    public bool IsSelectionRequired => Role == Role.PageTabList;

    /// <summary>The node whose selection this item is part of.</summary>
    public AccessibleNode? SelectionContainer
    {
        get
        {
            for (var p = Parent; p is not null; p = p.Parent)
            {
                if (p.IsSelectionContainer)
                    return p;
            }
            return null;
        }
    }

    public IEnumerable<AccessibleNode> Selection => Descendants(this).Where(n => n.IsSelected == true && n.SelectionContainer == this);

    private static IEnumerable<AccessibleNode> Descendants(AccessibleNode node) =>
        node.Children.SelectMany(c => Descendants(c).Prepend(c));

    // ExpandCollapse

    public bool? IsExpanded => Item is ComboBox box ? box.IsPopupOpen : null;

    public void SetExpanded(bool expanded) => (Item as ComboBox)?.SetPopupOpen(expanded);

    // Scroll

    public ScrollInfo? Scroll
    {
        get
        {
            if (Item is not Flickable f || Role is not (Role.Pane or Role.List))
                return null;
            var rangeX = f.ContentWidth - f.Width;
            var rangeY = f.ContentHeight - f.Height;
            return new ScrollInfo(
                rangeX > 0 ? Math.Clamp(f.ContentX / rangeX * 100, 0, 100) : -1,
                rangeY > 0 ? Math.Clamp(f.ContentY / rangeY * 100, 0, 100) : -1,
                f.ContentWidth > f.Width ? f.Width / f.ContentWidth * 100 : 100,
                f.ContentHeight > f.Height ? f.Height / f.ContentHeight * 100 : 100);
        }
    }

    /// <summary>Scrolls to a percentage of each axis; a negative one leaves that axis where it is.</summary>
    public void SetScrollPercent(double horizontal, double vertical)
    {
        if (Item is not Flickable f)
            return;
        if (horizontal >= 0 && f.ContentWidth > f.Width)
            f.ContentX = Math.Clamp(horizontal, 0, 100) / 100 * (f.ContentWidth - f.Width);
        if (vertical >= 0 && f.ContentHeight > f.Height)
            f.ContentY = Math.Clamp(vertical, 0, 100) / 100 * (f.ContentHeight - f.Height);
    }

    /// <summary>Scrolls by a number of steps on each axis: a page when <paramref name="page"/> is set, else a line.</summary>
    public void ScrollBy(int horizontal, int vertical, bool page)
    {
        if (Item is not Flickable f)
            return;
        const double line = 48;
        if (horizontal != 0 && f.ContentWidth > f.Width)
            f.ContentX = Math.Clamp(f.ContentX + horizontal * (page ? f.Width : line), 0, f.ContentWidth - f.Width);
        if (vertical != 0 && f.ContentHeight > f.Height)
            f.ContentY = Math.Clamp(f.ContentY + vertical * (page ? f.Height : line), 0, f.ContentHeight - f.Height);
    }

    // Text

    /// <summary>The text field behind the Text pattern, or null.</summary>
    public TextInput? TextInput => Item as TextInput;

    internal State Snapshot()
    {
        var input = TextInput;
        return new State(Name, Description, IsEnabled, Toggle, Value, Range?.Value, IsSelected, IsExpanded, Scroll,
            input is null ? null : (input.SelectionStart, input.SelectionEnd, input.CursorPosition));
    }

    internal sealed record State(string Name, string Description, bool Enabled, ToggleState? Toggle, string? Value, double? Range,
        bool? Selected, bool? Expanded, ScrollInfo? Scroll, (int, int, int)? TextSelection);

    private static string PlainText(Text text)
    {
        var value = text.Text_;
        return text.TextFormat != (int)Text.TextFormatValue.PlainText && value.Contains('<') ? Markup().Replace(value, "") : value;
    }

    private static IEnumerable<string> DescendantText(Item item)
    {
        foreach (var child in item.PaintOrder)
        {
            if (!child.IsVisibleFlag)
                continue;
            if (child is Text { Text_.Length: > 0 } text)
                yield return PlainText(text);
            foreach (var inner in DescendantText(child))
                yield return inner;
        }
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Markup();

    public override string ToString() => $"{Role} \"{Name}\"";
}
