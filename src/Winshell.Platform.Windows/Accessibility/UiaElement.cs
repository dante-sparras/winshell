using System.Runtime.InteropServices;
using Winshell.Runtime.Quick;
using Role = Winshell.Runtime.Quick.AccessibleAttached.RoleValue;

namespace Winshell.Platform.Windows.Accessibility;

/// <summary>
/// One node of a window's accessibility tree as a UI Automation provider. UI Automation calls in on its own threads;
/// every call runs on the window's thread, where the items live, through <see cref="WindowAutomation.Run"/>.
/// </summary>
[ComVisible(true)]
internal class UiaElement : IRawElementProviderSimple, IRawElementProviderFragment,
    IInvokeProvider, IToggleProvider, IRangeValueProvider, IValueProvider, ISelectionProvider, ISelectionItemProvider,
    IExpandCollapseProvider, IScrollProvider, ITextProvider
{
    private WindowAutomation? _automation;
    private AccessibleNode? _node;

    public UiaElement(WindowAutomation automation, AccessibleNode node)
    {
        _automation = automation;
        _node = node;
    }

    public AccessibleNode Node => _node ?? throw new InvalidOperationException("The element is disconnected");

    internal WindowAutomation Automation => _automation ?? throw new InvalidOperationException("The element is disconnected");

    /// <summary>Whether the element still stands for a node in the tree.</summary>
    internal bool IsAvailable => _node is { IsAlive: true };

    /// <summary>
    /// Lets go of the window and its node once the element is disconnected. UI Automation can keep a provider it was
    /// given after that, and it must not keep the shell's items, or a whole generation, alive with it.
    /// </summary>
    internal void Detach()
    {
        _automation = null;
        _node = null;
    }

    /// <summary>Runs a call on the window's thread, unless the node has left the tree.</summary>
    protected int Call(Action action) => _automation is { } automation ? automation.Run(() =>
    {
        if (!IsAvailable)
            return Uia.ElementNotAvailable;
        action();
        return Uia.S_OK;
    }) : Uia.ElementNotAvailable;

    protected int Get<T>(Func<T> read, out T value)
    {
        T result = default!;
        var hr = Call(() => result = read());
        value = result;
        return hr;
    }

    private int Act(Func<int> check, Action action)
    {
        var hr = 0;
        var call = Call(() => hr = check());
        if (call != Uia.S_OK || hr != Uia.S_OK)
            return call != Uia.S_OK ? call : hr;
        // Actions that can open popups or run QML handlers run after the call returns, as UI Automation asks.
        Automation.Post(Node, action);
        return Uia.S_OK;
    }

    private int Enabled() => Node.IsEnabled ? Uia.S_OK : Uia.ElementNotEnabled;

    // IRawElementProviderSimple

    int IRawElementProviderSimple.get_ProviderOptions(out int options)
    {
        options = Uia.ServerSideProvider | Uia.UseComThreading;
        return Uia.S_OK;
    }

    int IRawElementProviderSimple.GetPatternProvider(int patternId, out object? pattern) => Get(() => Supports(patternId) ? (object)this : null, out pattern);

    private bool Supports(int pattern) => pattern switch
    {
        Uia.InvokePattern => Node.CanInvoke,
        Uia.TogglePattern => Node.Toggle is not null,
        Uia.RangeValuePattern => Node.Range is not null,
        Uia.ValuePattern => Node.Value is not null,
        Uia.SelectionItemPattern => Node.IsSelected is not null,
        Uia.SelectionPattern => Node.IsSelectionContainer,
        Uia.ExpandCollapsePattern => Node.IsExpanded is not null,
        Uia.ScrollPattern => Node.Scroll is not null,
        Uia.TextPattern => Node.TextInput is not null,
        _ => false,
    };

    int IRawElementProviderSimple.GetPropertyValue(int propertyId, out object? value) => Get(() => Property(propertyId), out value);

    private object? Property(int id) => id switch
    {
        // The window's HWND says whether it is enabled and has the keyboard.
        Uia.IsEnabledProperty or Uia.IsKeyboardFocusableProperty or Uia.HasKeyboardFocusProperty when Node.IsRoot => null,
        Uia.ControlTypeProperty => ControlType(Node.Role),
        Uia.LocalizedControlTypeProperty when Node.Role == Role.Switch => "toggle switch",
        Uia.NameProperty => Node.Name is { Length: > 0 } name ? name : Node.IsRoot ? null : "",
        Uia.HelpTextProperty => Node.Description is { Length: > 0 } help ? help : null,
        Uia.AutomationIdProperty when Node.Item.ObjectName is { Length: > 0 } objectName => objectName,
        Uia.ClassNameProperty when !Node.IsRoot => Node.Item.GetType().Name,
        Uia.FrameworkIdProperty => "Winshell",
        Uia.IsEnabledProperty => Node.IsEnabled,
        Uia.IsKeyboardFocusableProperty => Node.IsKeyboardFocusable,
        Uia.HasKeyboardFocusProperty => Automation.HasFocus && Node.HasKeyboardFocus,
        Uia.IsPasswordProperty => Node.IsPassword,
        Uia.IsOffscreenProperty when !Node.IsRoot => Node.IsOffscreen,
        Uia.IsControlElementProperty or Uia.IsContentElementProperty when !Node.IsRoot => true,
        _ => null,
    };

    /// <summary>The UI Automation control type of a role. A switch is a button with the Toggle pattern, as WinUI's is.</summary>
    internal static int ControlType(Role role) => role switch
    {
        Role.Button or Role.Switch => Uia.Button,
        Role.CheckBox => Uia.CheckBox,
        Role.RadioButton => Uia.RadioButton,
        Role.ComboBox => Uia.ComboBox,
        Role.EditableText => Uia.Edit,
        Role.StaticText or Role.Heading or Role.Paragraph => Uia.Text,
        Role.Link => Uia.Hyperlink,
        Role.Graphic => Uia.Image,
        Role.List => Uia.List,
        Role.ListItem => Uia.ListItem,
        Role.PopupMenu => Uia.Menu,
        Role.MenuBar => Uia.MenuBar,
        Role.MenuItem => Uia.MenuItem,
        Role.ProgressBar => Uia.ProgressBar,
        Role.ScrollBar => Uia.ScrollBar,
        Role.Slider or Role.Dial => Uia.Slider,
        Role.SpinBox => Uia.Spinner,
        Role.StatusBar => Uia.StatusBar,
        Role.PageTabList => Uia.Tab,
        Role.PageTab => Uia.TabItem,
        Role.ToolBar => Uia.ToolBar,
        Role.ToolTip => Uia.ToolTip,
        Role.Tree => Uia.Tree,
        Role.TreeItem => Uia.TreeItem,
        Role.Grouping or Role.Section => Uia.Group,
        Role.Document => Uia.Document,
        Role.Window or Role.Dialog => Uia.Window,
        Role.Table => Uia.Table,
        Role.Separator => Uia.Separator,
        Role.Pane or Role.Client or Role.LayeredPane or Role.Notification or Role.Application => Uia.Pane,
        _ => Uia.Custom,
    };

    int IRawElementProviderSimple.get_HostRawElementProvider(out IRawElementProviderSimple? host)
    {
        host = null;
        if (_node is { IsRoot: true } && _automation is { Handle: not 0 } automation)
            return Uia.UiaHostProviderFromHwnd(automation.Handle, out host);
        return Uia.S_OK;
    }

    // IRawElementProviderFragment

    int IRawElementProviderFragment.Navigate(int direction, out IRawElementProviderFragment? element) => Get(() =>
    {
        var target = direction switch
        {
            0 => Node.Parent,
            1 => Sibling(1),
            2 => Sibling(-1),
            3 => Node.Children.FirstOrDefault(),
            4 => Node.Children.LastOrDefault(),
            _ => null,
        };
        return target is null ? null : (IRawElementProviderFragment)Automation.For(target);
    }, out element);

    private AccessibleNode? Sibling(int delta)
    {
        if (Node.Parent is not { } parent)
            return null;
        var at = parent.ChildIndex(Node) + delta;
        return at >= 0 && at < parent.Children.Count ? parent.Children[at] : null;
    }

    int IRawElementProviderFragment.GetRuntimeId(out int[]? runtimeId)
    {
        // Read the node once. Node throws after Detach, and this call does not go through the window thread.
        var node = _node;
        if (node is not { IsAlive: true })
        {
            runtimeId = null;
            return Uia.ElementNotAvailable;
        }
        // The root's id comes from its window.
        runtimeId = node.IsRoot ? null : [Uia.AppendRuntimeId, node.Id];
        return Uia.S_OK;
    }

    public int[] RuntimeId => [Uia.AppendRuntimeId, Node.Id];

    int IRawElementProviderFragment.get_BoundingRectangle(out UiaRect rect) =>
        Get(() => Node.IsRoot ? default : Automation.ToScreen(Node.Bounds), out rect);

    int IRawElementProviderFragment.GetEmbeddedFragmentRoots(out object[]? roots)
    {
        roots = null;
        return Uia.S_OK;
    }

    // UI Automation focuses the window before it calls this, so only the item's focus moves here.
    // The item takes focus, and the window takes the keyboard if Windows lets it; if it does not, the client hears so.
    // An outermost call does that before it returns, so the client hears the result. A call that arrived inside a
    // frame or inside another call is posted: focusing there would run QML from the middle of layout.
    int IRawElementProviderFragment.SetFocus()
    {
        var taken = true;
        var hr = Call(() =>
        {
            if (!Automation.DeferMutations)
            {
                Node.Focus();
                taken = Node.HasKeyboardFocus && Automation.TryTakeKeyboard();
                return;
            }
            var node = Node;
            Automation.Post(node, () =>
            {
                node.Focus();
                if (node.HasKeyboardFocus)
                    Automation.TryTakeKeyboard();
            });
        });
        return hr != Uia.S_OK ? hr : taken ? Uia.S_OK : Uia.InvalidOperation;
    }

    int IRawElementProviderFragment.get_FragmentRoot(out IRawElementProviderFragmentRoot? root) =>
        Get(() => (IRawElementProviderFragmentRoot)Automation.Root, out root);

    // Invoke

    int IInvokeProvider.Invoke() => Act(Enabled, () =>
    {
        Node.Invoke();
        Automation.Raise(Node, Uia.InvokedEvent);
    });

    // Toggle

    int IToggleProvider.Toggle() => Act(Enabled, Node.DoToggle);

    int IToggleProvider.get_ToggleState(out int state) => Get(() => (int)(Node.Toggle ?? ToggleState.Off), out state);

    // RangeValue

    int IRangeValueProvider.SetValue(double value) => Act(() =>
    {
        if (Node.Range is not { } range || range.IsReadOnly || !Node.IsEnabled)
            return Uia.ElementNotEnabled;
        if (value < range.Minimum || value > range.Maximum || double.IsNaN(value))
            return Uia.E_INVALIDARG;
        return Uia.S_OK;
    }, () => Node.SetRangeValue(value));

    int IRangeValueProvider.get_Value(out double value) => Get(() => Node.Range?.Value ?? 0, out value);

    int IRangeValueProvider.get_IsReadOnly(out int readOnly) => Get(() => Node.Range is not { IsReadOnly: false } || !Node.IsEnabled ? 1 : 0, out readOnly);

    int IRangeValueProvider.get_Maximum(out double maximum) => Get(() => Node.Range?.Maximum ?? 0, out maximum);

    int IRangeValueProvider.get_Minimum(out double minimum) => Get(() => Node.Range?.Minimum ?? 0, out minimum);

    int IRangeValueProvider.get_LargeChange(out double change) => Get(() => Node.Range?.LargeChange ?? 0, out change);

    int IRangeValueProvider.get_SmallChange(out double change) => Get(() => Node.Range?.SmallChange ?? 0, out change);

    // Value

    int IValueProvider.SetValue(string value) => Act(
        () => Node.IsValueReadOnly || !Node.IsEnabled ? Uia.ElementNotEnabled : Uia.S_OK,
        () => Node.SetValue(value ?? ""));

    int IValueProvider.get_Value(out string value) => Get(() => Node.Value ?? "", out value);

    int IValueProvider.get_IsReadOnly(out int readOnly) => Get(() => Node.IsValueReadOnly ? 1 : 0, out readOnly);

    // Selection

    int ISelectionProvider.GetSelection(out object[]? selection) => Get(() => Node.Selection.Select(n => (object)Automation.For(n)).ToArray(), out selection);

    int ISelectionProvider.get_CanSelectMultiple(out int value)
    {
        value = 0;
        return Uia.S_OK;
    }

    int ISelectionProvider.get_IsSelectionRequired(out int value) => Get(() => Node.IsSelectionRequired ? 1 : 0, out value);

    // SelectionItem

    int ISelectionItemProvider.Select() => Act(Enabled, Node.Select);

    int ISelectionItemProvider.AddToSelection() => Act(() =>
        Enabled() is var enabled and not Uia.S_OK ? enabled
        : Node.IsSelected == true || Node.SelectionContainer?.Selection.Any() != true ? Uia.S_OK
        : Uia.InvalidOperation, Node.Select);

    int ISelectionItemProvider.RemoveFromSelection()
    {
        var hr = 0;
        var call = Call(() => hr = Node.IsSelected == true ? Uia.InvalidOperation : Uia.S_OK);
        return call != Uia.S_OK ? call : hr;
    }

    int ISelectionItemProvider.get_IsSelected(out int value) => Get(() => Node.IsSelected == true ? 1 : 0, out value);

    int ISelectionItemProvider.get_SelectionContainer(out IRawElementProviderSimple? container) =>
        Get(() => Node.SelectionContainer is { } c ? (IRawElementProviderSimple)Automation.For(c) : null, out container);

    // ExpandCollapse

    int IExpandCollapseProvider.Expand() => Act(Enabled, () => Node.SetExpanded(true));

    int IExpandCollapseProvider.Collapse() => Act(Enabled, () => Node.SetExpanded(false));

    int IExpandCollapseProvider.get_ExpandCollapseState(out int state) => Get(() => Node.IsExpanded == true ? 1 : 0, out state);

    // Scroll

    int IScrollProvider.Scroll(int horizontalAmount, int verticalAmount) => Act(() => Uia.S_OK, () =>
    {
        // LargeDecrement, SmallDecrement, NoAmount, LargeIncrement, SmallIncrement.
        static int Steps(int amount) => amount switch { 0 or 1 => -1, 3 or 4 => 1, _ => 0 };
        Node.ScrollBy(Steps(horizontalAmount), 0, horizontalAmount is 0 or 3);
        Node.ScrollBy(0, Steps(verticalAmount), verticalAmount is 0 or 3);
    });

    int IScrollProvider.SetScrollPercent(double horizontalPercent, double verticalPercent) =>
        Act(() => Uia.S_OK, () => Node.SetScrollPercent(horizontalPercent, verticalPercent));

    int IScrollProvider.get_HorizontalScrollPercent(out double value) => Get(() => Node.Scroll?.HorizontalPercent ?? -1, out value);

    int IScrollProvider.get_VerticalScrollPercent(out double value) => Get(() => Node.Scroll?.VerticalPercent ?? -1, out value);

    int IScrollProvider.get_HorizontalViewSize(out double value) => Get(() => Node.Scroll?.HorizontalViewSize ?? 100, out value);

    int IScrollProvider.get_VerticalViewSize(out double value) => Get(() => Node.Scroll?.VerticalViewSize ?? 100, out value);

    int IScrollProvider.get_HorizontallyScrollable(out int value) => Get(() => Node.Scroll?.HorizontallyScrollable == true ? 1 : 0, out value);

    int IScrollProvider.get_VerticallyScrollable(out int value) => Get(() => Node.Scroll?.VerticallyScrollable == true ? 1 : 0, out value);

    // Text

    int ITextProvider.GetSelection(out object[]? ranges) => Get(() =>
    {
        var input = Node.TextInput!;
        return new object[] { new UiaTextRange(this, input.SelectionStart == input.SelectionEnd ? input.CursorPosition : input.SelectionStart, input.SelectionEnd == input.SelectionStart ? input.CursorPosition : input.SelectionEnd) };
    }, out ranges);

    int ITextProvider.GetVisibleRanges(out object[]? ranges) => Get(() => new object[] { new UiaTextRange(this, 0, Node.TextInput!.DisplayText.Length) }, out ranges);

    int ITextProvider.RangeFromChild(IRawElementProviderSimple child, out ITextRangeProvider? range)
    {
        range = null;
        return Uia.E_INVALIDARG;
    }

    int ITextProvider.RangeFromPoint(UiaPoint point, out ITextRangeProvider? range) => Get(() =>
    {
        var local = Automation.ToItem(Node.Item, point.X, point.Y);
        var at = Node.TextInput!.PositionAt(local.X, local.Y);
        return (ITextRangeProvider)new UiaTextRange(this, at, at);
    }, out range);

    int ITextProvider.get_DocumentRange(out ITextRangeProvider? range) => Get(() => (ITextRangeProvider)new UiaTextRange(this, 0, Node.TextInput!.DisplayText.Length), out range);

    int ITextProvider.get_SupportedTextSelection(out int value)
    {
        value = 1;
        return Uia.S_OK;
    }
}

/// <summary>The window itself: the root of its UI Automation fragments, hosted by its HWND.</summary>
[ComVisible(true)]
internal sealed class UiaRoot(WindowAutomation Automation, AccessibleNode Node) : UiaElement(Automation, Node), IRawElementProviderFragmentRoot
{
    int IRawElementProviderFragmentRoot.ElementProviderFromPoint(double x, double y, out IRawElementProviderFragment? element) =>
        Get(() => (IRawElementProviderFragment)Automation.ElementAt(x, y), out element);

    int IRawElementProviderFragmentRoot.GetFocus(out IRawElementProviderFragment? element) =>
        Get(() => Automation.FocusedElement is { } focused && focused != this ? (IRawElementProviderFragment)focused : null, out element);
}
