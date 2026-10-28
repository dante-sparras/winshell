using System.Runtime.InteropServices;
using Winshell.Runtime.Quick;
using Role = Winshell.Runtime.Quick.AccessibleAttached.RoleValue;

namespace Winshell.Platform.Windows.Accessibility;

/// <summary>
/// One node of a window's accessibility tree as a UI Automation provider. UI Automation calls in on its own threads;
/// every call runs on the window's thread, where the items live, through <see cref="WindowAutomation.Run"/>.
/// </summary>
[ComVisible(true)]
internal class UiaElement(WindowAutomation automation, AccessibleNode node) : IRawElementProviderSimple, IRawElementProviderFragment,
    IInvokeProvider, IToggleProvider, IRangeValueProvider, IValueProvider, ISelectionProvider, ISelectionItemProvider,
    IExpandCollapseProvider, IScrollProvider, ITextProvider
{
    public AccessibleNode Node => node;

    internal WindowAutomation Automation => automation;

    /// <summary>Runs a call on the window's thread, unless the node has left the tree.</summary>
    protected int Call(Action action) => automation.Run(() =>
    {
        if (!node.IsAlive)
            return Uia.ElementNotAvailable;
        action();
        return Uia.S_OK;
    });

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
        automation.Post(node, action);
        return Uia.S_OK;
    }

    private int Enabled() => node.IsEnabled ? Uia.S_OK : Uia.ElementNotEnabled;

    // IRawElementProviderSimple

    int IRawElementProviderSimple.get_ProviderOptions(out int options)
    {
        options = Uia.ServerSideProvider | Uia.UseComThreading;
        return Uia.S_OK;
    }

    int IRawElementProviderSimple.GetPatternProvider(int patternId, out object? pattern) => Get(() => Supports(patternId) ? (object)this : null, out pattern);

    private bool Supports(int pattern) => pattern switch
    {
        Uia.InvokePattern => node.CanInvoke,
        Uia.TogglePattern => node.Toggle is not null,
        Uia.RangeValuePattern => node.Range is not null,
        Uia.ValuePattern => node.Value is not null,
        Uia.SelectionItemPattern => node.IsSelected is not null,
        Uia.SelectionPattern => node.IsSelectionContainer,
        Uia.ExpandCollapsePattern => node.IsExpanded is not null,
        Uia.ScrollPattern => node.Scroll is not null,
        Uia.TextPattern => node.TextInput is not null,
        _ => false,
    };

    int IRawElementProviderSimple.GetPropertyValue(int propertyId, out object? value) => Get(() => Property(propertyId), out value);

    private object? Property(int id) => id switch
    {
        // The window's HWND says whether it is enabled and has the keyboard.
        Uia.IsEnabledProperty or Uia.IsKeyboardFocusableProperty or Uia.HasKeyboardFocusProperty when node.IsRoot => null,
        Uia.ControlTypeProperty => ControlType(node.Role),
        Uia.LocalizedControlTypeProperty when node.Role == Role.Switch => "toggle switch",
        Uia.NameProperty => node.Name is { Length: > 0 } name ? name : node.IsRoot ? null : "",
        Uia.HelpTextProperty => node.Description is { Length: > 0 } help ? help : null,
        Uia.AutomationIdProperty when node.Item.ObjectName is { Length: > 0 } objectName => objectName,
        Uia.ClassNameProperty when !node.IsRoot => node.Item.GetType().Name,
        Uia.FrameworkIdProperty => "Winshell",
        Uia.IsEnabledProperty => node.IsEnabled,
        Uia.IsKeyboardFocusableProperty => node.IsKeyboardFocusable,
        Uia.HasKeyboardFocusProperty => automation.HasFocus && node.HasKeyboardFocus,
        Uia.IsPasswordProperty => node.IsPassword,
        Uia.IsOffscreenProperty when !node.IsRoot => node.IsOffscreen,
        Uia.IsControlElementProperty or Uia.IsContentElementProperty when !node.IsRoot => true,
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
        if (node.IsRoot && automation.Handle != 0)
            return Uia.UiaHostProviderFromHwnd(automation.Handle, out host);
        return Uia.S_OK;
    }

    // IRawElementProviderFragment

    int IRawElementProviderFragment.Navigate(int direction, out IRawElementProviderFragment? element) => Get(() =>
    {
        var target = direction switch
        {
            0 => node.Parent,
            1 => Sibling(1),
            2 => Sibling(-1),
            3 => node.Children.FirstOrDefault(),
            4 => node.Children.LastOrDefault(),
            _ => null,
        };
        return target is null ? null : (IRawElementProviderFragment)automation.For(target);
    }, out element);

    private AccessibleNode? Sibling(int delta)
    {
        if (node.Parent is not { } parent)
            return null;
        var at = parent.ChildIndex(node) + delta;
        return at >= 0 && at < parent.Children.Count ? parent.Children[at] : null;
    }

    int IRawElementProviderFragment.GetRuntimeId(out int[]? runtimeId)
    {
        // The root's id comes from its window.
        runtimeId = node.IsRoot ? null : RuntimeId;
        return node.IsAlive ? Uia.S_OK : Uia.ElementNotAvailable;
    }

    public int[] RuntimeId => [Uia.AppendRuntimeId, node.Id];

    int IRawElementProviderFragment.get_BoundingRectangle(out UiaRect rect) =>
        Get(() => node.IsRoot ? default : automation.ToScreen(node.Bounds), out rect);

    int IRawElementProviderFragment.GetEmbeddedFragmentRoots(out object[]? roots)
    {
        roots = null;
        return Uia.S_OK;
    }

    // UI Automation focuses the window before it calls this, so only the item's focus moves here.
    int IRawElementProviderFragment.SetFocus() => Call(node.Focus);

    int IRawElementProviderFragment.get_FragmentRoot(out IRawElementProviderFragmentRoot? root)
    {
        root = automation.Root;
        return Uia.S_OK;
    }

    // Invoke

    int IInvokeProvider.Invoke() => Act(Enabled, () =>
    {
        node.Invoke();
        automation.Raise(node, Uia.InvokedEvent);
    });

    // Toggle

    int IToggleProvider.Toggle() => Act(Enabled, node.DoToggle);

    int IToggleProvider.get_ToggleState(out int state) => Get(() => (int)(node.Toggle ?? ToggleState.Off), out state);

    // RangeValue

    int IRangeValueProvider.SetValue(double value)
    {
        var hr = 0;
        var call = Call(() =>
        {
            if (node.Range is not { } range || range.IsReadOnly || !node.IsEnabled)
                hr = Uia.ElementNotEnabled;
            else if (value < range.Minimum || value > range.Maximum || double.IsNaN(value))
                hr = Uia.E_INVALIDARG;
            else
                node.SetRangeValue(value);
        });
        return call != Uia.S_OK ? call : hr;
    }

    int IRangeValueProvider.get_Value(out double value) => Get(() => node.Range?.Value ?? 0, out value);

    int IRangeValueProvider.get_IsReadOnly(out int readOnly) => Get(() => node.Range is not { IsReadOnly: false } || !node.IsEnabled ? 1 : 0, out readOnly);

    int IRangeValueProvider.get_Maximum(out double maximum) => Get(() => node.Range?.Maximum ?? 0, out maximum);

    int IRangeValueProvider.get_Minimum(out double minimum) => Get(() => node.Range?.Minimum ?? 0, out minimum);

    int IRangeValueProvider.get_LargeChange(out double change) => Get(() => node.Range?.LargeChange ?? 0, out change);

    int IRangeValueProvider.get_SmallChange(out double change) => Get(() => node.Range?.SmallChange ?? 0, out change);

    // Value

    int IValueProvider.SetValue(string value)
    {
        var hr = 0;
        var call = Call(() =>
        {
            if (node.IsValueReadOnly || !node.IsEnabled)
                hr = Uia.ElementNotEnabled;
            else
                node.SetValue(value ?? "");
        });
        return call != Uia.S_OK ? call : hr;
    }

    int IValueProvider.get_Value(out string value) => Get(() => node.Value ?? "", out value);

    int IValueProvider.get_IsReadOnly(out int readOnly) => Get(() => node.IsValueReadOnly ? 1 : 0, out readOnly);

    // Selection

    int ISelectionProvider.GetSelection(out object[]? selection) => Get(() => node.Selection.Select(n => (object)automation.For(n)).ToArray(), out selection);

    int ISelectionProvider.get_CanSelectMultiple(out int value)
    {
        value = 0;
        return Uia.S_OK;
    }

    int ISelectionProvider.get_IsSelectionRequired(out int value) => Get(() => node.IsSelectionRequired ? 1 : 0, out value);

    // SelectionItem

    int ISelectionItemProvider.Select() => Act(Enabled, node.Select);

    int ISelectionItemProvider.AddToSelection() => Act(() =>
        Enabled() is var enabled and not Uia.S_OK ? enabled
        : node.IsSelected == true || node.SelectionContainer?.Selection.Any() != true ? Uia.S_OK
        : Uia.InvalidOperation, node.Select);

    int ISelectionItemProvider.RemoveFromSelection()
    {
        var hr = 0;
        var call = Call(() => hr = node.IsSelected == true ? Uia.InvalidOperation : Uia.S_OK);
        return call != Uia.S_OK ? call : hr;
    }

    int ISelectionItemProvider.get_IsSelected(out int value) => Get(() => node.IsSelected == true ? 1 : 0, out value);

    int ISelectionItemProvider.get_SelectionContainer(out IRawElementProviderSimple? container) =>
        Get(() => node.SelectionContainer is { } c ? (IRawElementProviderSimple)automation.For(c) : null, out container);

    // ExpandCollapse

    int IExpandCollapseProvider.Expand() => Act(Enabled, () => node.SetExpanded(true));

    int IExpandCollapseProvider.Collapse() => Act(Enabled, () => node.SetExpanded(false));

    int IExpandCollapseProvider.get_ExpandCollapseState(out int state) => Get(() => node.IsExpanded == true ? 1 : 0, out state);

    // Scroll

    int IScrollProvider.Scroll(int horizontalAmount, int verticalAmount) => Call(() =>
    {
        // LargeDecrement, SmallDecrement, NoAmount, LargeIncrement, SmallIncrement.
        static int Steps(int amount) => amount switch { 0 or 1 => -1, 3 or 4 => 1, _ => 0 };
        node.ScrollBy(Steps(horizontalAmount), 0, horizontalAmount is 0 or 3);
        node.ScrollBy(0, Steps(verticalAmount), verticalAmount is 0 or 3);
    });

    int IScrollProvider.SetScrollPercent(double horizontalPercent, double verticalPercent) =>
        Call(() => node.SetScrollPercent(horizontalPercent, verticalPercent));

    int IScrollProvider.get_HorizontalScrollPercent(out double value) => Get(() => node.Scroll?.HorizontalPercent ?? -1, out value);

    int IScrollProvider.get_VerticalScrollPercent(out double value) => Get(() => node.Scroll?.VerticalPercent ?? -1, out value);

    int IScrollProvider.get_HorizontalViewSize(out double value) => Get(() => node.Scroll?.HorizontalViewSize ?? 100, out value);

    int IScrollProvider.get_VerticalViewSize(out double value) => Get(() => node.Scroll?.VerticalViewSize ?? 100, out value);

    int IScrollProvider.get_HorizontallyScrollable(out int value) => Get(() => node.Scroll?.HorizontallyScrollable == true ? 1 : 0, out value);

    int IScrollProvider.get_VerticallyScrollable(out int value) => Get(() => node.Scroll?.VerticallyScrollable == true ? 1 : 0, out value);

    // Text

    int ITextProvider.GetSelection(out object[]? ranges) => Get(() =>
    {
        var input = node.TextInput!;
        return new object[] { new UiaTextRange(this, input.SelectionStart == input.SelectionEnd ? input.CursorPosition : input.SelectionStart, input.SelectionEnd == input.SelectionStart ? input.CursorPosition : input.SelectionEnd) };
    }, out ranges);

    int ITextProvider.GetVisibleRanges(out object[]? ranges) => Get(() => new object[] { new UiaTextRange(this, 0, node.TextInput!.DisplayText.Length) }, out ranges);

    int ITextProvider.RangeFromChild(IRawElementProviderSimple child, out ITextRangeProvider? range)
    {
        range = null;
        return Uia.E_INVALIDARG;
    }

    int ITextProvider.RangeFromPoint(UiaPoint point, out ITextRangeProvider? range) => Get(() =>
    {
        var local = automation.ToItem(node.Item, point.X, point.Y);
        var at = node.TextInput!.PositionAt(local.X, local.Y);
        return (ITextRangeProvider)new UiaTextRange(this, at, at);
    }, out range);

    int ITextProvider.get_DocumentRange(out ITextRangeProvider? range) => Get(() => (ITextRangeProvider)new UiaTextRange(this, 0, node.TextInput!.DisplayText.Length), out range);

    int ITextProvider.get_SupportedTextSelection(out int value)
    {
        value = 1;
        return Uia.S_OK;
    }
}

/// <summary>The window itself: the root of its UI Automation fragments, hosted by its HWND.</summary>
[ComVisible(true)]
internal sealed class UiaRoot(WindowAutomation automation, AccessibleNode node) : UiaElement(automation, node), IRawElementProviderFragmentRoot
{
    int IRawElementProviderFragmentRoot.ElementProviderFromPoint(double x, double y, out IRawElementProviderFragment? element) =>
        Get(() => (IRawElementProviderFragment)Automation.ElementAt(x, y), out element);

    int IRawElementProviderFragmentRoot.GetFocus(out IRawElementProviderFragment? element) =>
        Get(() => Automation.FocusedElement is { } focused && focused != this ? (IRawElementProviderFragment)focused : null, out element);
}
