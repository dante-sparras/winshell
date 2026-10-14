using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Winshell.Kit;

/// <summary>
/// The Kit's selection: the control the arrow keys are on, for every panel built from Kit controls (ADR 0045).
/// <see cref="PanelSelection"/> decides what a key does from the controls the panel reports. The Kit finds the control's element,
/// gives it the keyboard, and says where the ring goes. A plugin writes no code for it.
/// </summary>
public sealed partial class Kit
{
    private PanelSelection _selection = new();

    // What had the keyboard at the last key. A box or a pressed control that has it since then is where the next move starts from.
    private UIElement? _focusSeen;

    // True from a rebuild until the next key. The rows the keyboard was in are gone, and Windows has put it somewhere of its own choosing.
    private bool _kept;

    /// <summary>
    /// Presses an arrow key or Return in the panel drawn under <paramref name="root"/>. Up and Down move the selection between
    /// the controls that have a command. Left and Right change a slider, step a stepper, or move along a row of chips.
    /// Return answers the command a click on the selected control sends, for the host to send.
    /// </summary>
    /// <param name="root">The panel's content.</param>
    /// <param name="key">The key.</param>
    /// <param name="held">True when the key was already down.</param>
    /// <param name="focused">What has the keyboard. A box being typed in, or a control the pointer pressed, is where the selection moves from.</param>
    /// <param name="typing">True when the key came from the keyboard, so a box that has it keeps Left, Right, and Return for its text.</param>
    /// <returns>Whether the key was used, and the command to send.</returns>
    public PanelStroke Key(UIElement root, PanelKey key, bool held = false, UIElement? focused = null, bool typing = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        var (lines, stops) = Trace(root);
        var fresh = focused is not null && !ReferenceEquals(focused, _focusSeen);
        var box = focused is TextBox or PasswordBox;
        if (typing && box && key is not (PanelKey.Up or PanelKey.Down))
        {
            // The box edits with these keys. They are the selection's only when it has moved on to a control that could not take the keyboard.
            var on = _selection.At(lines);
            if (fresh || on < 0 || on >= stops.Count || Holds(stops[on], focused!))
            {
                return default;
            }
        }

        if (fresh)
        {
            _focusSeen = focused;

            // A box that took the keyboard as the panel opened, or a control given it by a press of the pointer.
            // One Windows gave the keyboard, as the window opened or when the panel was built again, is not where the person is.
            if (box || (!_kept && focused!.FocusState == FocusState.Pointer))
            {
                var under = stops.FindLastIndex(stop => Holds(stop, focused!));
                if (under >= 0)
                {
                    _selection.Select(lines, under);
                }
            }

            _kept = false;
        }

        var before = _selection.At(lines);
        var stroke = _selection.Press(key, lines, held);
        var after = _selection.At(lines);
        if (stroke.Used && after != before && after >= 0 && after < stops.Count)
        {
            Reach(stops[after]);
        }

        return stroke;
    }

    /// <summary>The command of the control the arrow keys are on, as <c>winoma desktop</c> prints it. Null when they are on none.</summary>
    /// <param name="root">The panel's content.</param>
    /// <returns>The command, or null.</returns>
    public string? Selected(UIElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // A panel with nothing selected is not read for it. This is asked on every winoma desktop.
        return _selection.Active ? _selection.Selected(Trace(root).Lines)?.Command : null;
    }

    /// <summary>Draws <paramref name="ring"/> around the selected control of the panel under <paramref name="root"/>, or hides it when none is selected.</summary>
    /// <param name="root">The panel's content.</param>
    /// <param name="ring">The host's ring, laid over the panel's rows.</param>
    public void Mark(UIElement root, SelectionRing ring)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(ring);

        // The host asks on every refresh of the panel. A panel with nothing selected is not read for it.
        if (!_selection.Active)
        {
            ring.Hide();
            return;
        }

        var (lines, stops) = Trace(root);
        var at = _selection.At(lines);
        ring.Show(at >= 0 && at < stops.Count ? stops[at] : null);
    }

    /// <summary>Selects nothing, as when the pointer presses in the panel. The next arrow key starts from what the pointer pressed.</summary>
    public void Deselect()
    {
        _selection.Clear();
        _focusSeen = null;
    }

    /// <summary>Takes over the selection of <paramref name="from"/>, the Kit this panel was drawn with before it was built again.</summary>
    /// <param name="from">The Kit of the same panel's last build.</param>
    public void Keep(Kit from)
    {
        ArgumentNullException.ThrowIfNull(from);
        _selection = from._selection;
        _focusSeen = null;
        _kept = true;
    }

    /// <summary>The panel's lines, and the element of each line that has a command, in the order <see cref="PanelSelection"/> counts them.</summary>
    private (List<PanelLine> Lines, List<UIElement> Stops) Trace(UIElement root)
    {
        var lines = new List<PanelLine>();
        var stops = new List<UIElement>();
        Lines(root, lines, stops);
        return (lines, stops);
    }

    /// <summary>
    /// Gives the selected control the keyboard and scrolls it into view. A row that holds its control inside it, such as a field's box,
    /// passes the keyboard on to that. One that cannot take the keyboard at all, such as a slider's track, hands it to the panel around it,
    /// so Space and what is typed no longer reach the control selected before. The focus is asked for as the pointer's,
    /// so Windows draws no focus rectangle beside the ring.
    /// </summary>
    private void Reach(UIElement element)
    {
        // A panel that was opened and sent a key in one summon has not put its rows on screen yet. The ring finds the control once it has.
        if (element is FrameworkElement { IsLoaded: false })
        {
            return;
        }

        element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        var target = element.IsTabStop ? element : FocusManager.FindFirstFocusableElement(element) as UIElement ?? Around(element);
        if (target is null || !target.Focus(FocusState.Pointer))
        {
            return;
        }

        _focusSeen = element.XamlRoot is { } xaml ? FocusManager.GetFocusedElement(xaml) as UIElement ?? target : target;
    }

    /// <summary>The nearest element around <paramref name="element"/> that takes the keyboard: the panel's own frame.</summary>
    private static UIElement? Around(UIElement element)
    {
        for (var at = VisualTreeHelper.GetParent(element); at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is UIElement { IsTabStop: true } above)
            {
                return above;
            }
        }

        return null;
    }

    /// <summary>True when <paramref name="inner"/> is <paramref name="outer"/> or is drawn inside it.</summary>
    private static bool Holds(UIElement outer, UIElement inner)
    {
        for (DependencyObject? at = inner; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, outer))
            {
                return true;
            }
        }

        return false;
    }
}
