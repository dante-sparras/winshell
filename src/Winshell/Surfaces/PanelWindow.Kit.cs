using ShellKit = Winshell.Kit.Kit;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Winshell;

/// <summary>
/// A panel a plugin draws with the Kit (ADR 0045). The window keeps its place, slide, size, and keys; the plugin's view
/// fills it once, and each tick reads the getters its controls were given and changes only what moved.
/// </summary>
internal sealed partial class PanelWindow
{
    private IKitPanel? _kitView;
    private ShellKit? _kit;
    private KitPanel? _kitPanel;
    private Action<string, Action>? _kitPress;
    private Action<string, double?>? _kitSend;

    // The outline around the control the arrow keys are on. It lies over the rows, in the part of the panel that scrolls.
    private readonly SelectionRing _ring = new();

    /// <summary>The open Kit panel's controls as text for <c>winoma desktop</c>, or null when no Kit panel is open.</summary>
    internal IReadOnlyList<PanelLine>? KitOutline => _kit is not null && _kitPanel is not null ? _kit.Outline(_kitPanel.Content) : null;

    /// <summary>The command of the control the arrow keys are on in the open Kit panel, or null when they are on none.</summary>
    internal string? KitSelected => _kit is not null && _kitPanel is { OwnKeys: false } ? _kit.Selected(_kitPanel.Content) : null;

    /// <summary>
    /// Opens <paramref name="view"/>'s panel beside <paramref name="anchor"/>, or leaves it open when it already is.
    /// With <paramref name="middle"/>, it opens at the middle of the screen instead, as the Menu does.
    /// With <paramref name="at"/>, it opens beside that place on the bar in place of the anchor, as a right click on the bar itself asks.
    /// A control's press runs through <paramref name="press"/>, which guards it and refreshes the panel after it.
    /// Return on a selected row that only names its command sends it through <paramref name="send"/>, to the plugin, as a click on that row does.
    /// <paramref name="owner"/> is the Bar the panel is on, and <paramref name="bar"/> is that Bar's index: the panel is placed on its monitor,
    /// at the middle of it for the Menu, and layered just under it.
    /// </summary>
    public void ShowKit(string id, Window owner, int bar, BarEdge edge, FrameworkElement anchor, IKitPanel view, Action<string, Action> press, Action<string, double?> send, bool middle = false, Windows.Foundation.Rect? at = null) =>
        _session.Show(id, refreshing => DrawKit(refreshing, owner, edge, anchor, view, press, send, middle, at), bar);

    /// <summary>Fetches and reads every getter of the open Kit panel, and writes what moved. Call on each tick.</summary>
    public void RefreshKit()
    {
        if (_kitView is null || _kit is null)
        {
            return;
        }

        try
        {
            _kitView.UpdatePanel();
        }
        catch (Exception ex)
        {
            Log.Warning("Panel", "a panel could not fetch its values", ex);
        }

        _kit.Bindings.Refresh();
        MarkSelection();
    }

    /// <summary>
    /// Presses an arrow key or Return on the open Kit panel as the keyboard would, as <c>winoma summon &lt;id&gt; key --text &lt;key&gt;</c> asks:
    /// the plugin hears it first, and a key it leaves alone moves the Kit's selection.
    /// </summary>
    /// <returns>False when no Kit panel is open or neither used the key, so the command goes on to the plugin as text.</returns>
    public bool PressKey(PanelKey key)
    {
        if (_kit is null || _kitPanel is null)
        {
            return false;
        }

        var pressed = key switch
        {
            PanelKey.Up => VirtualKey.Up,
            PanelKey.Down => VirtualKey.Down,
            PanelKey.Left => VirtualKey.Left,
            PanelKey.Right => VirtualKey.Right,
            _ => VirtualKey.Enter,
        };
        if (OfferKey(new KitKey(pressed, false, false, false)))
        {
            return true;
        }

        // No box hears this key, so one that has the keyboard does not keep Left, Right, or Return to itself.
        var focused = Content?.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as UIElement : null;
        return SelectKey(key, held: false, focused, typing: false);
    }

    /// <summary>The selection's key for <paramref name="key"/>, or null for a key that is not an arrow or Return.</summary>
    private static PanelKey? Selects(VirtualKey key) => key switch
    {
        VirtualKey.Up => PanelKey.Up,
        VirtualKey.Down => PanelKey.Down,
        VirtualKey.Left => PanelKey.Left,
        VirtualKey.Right => PanelKey.Right,
        VirtualKey.Enter => PanelKey.Enter,
        _ => null,
    };

    /// <summary>
    /// Offers an arrow key or Return to the open Kit panel's selection, and sends the command the key answers with, as a click on
    /// the selected control would. A panel whose plugin moves a selection of its own has none here. True when the selection used the key.
    /// </summary>
    private bool SelectKey(PanelKey key, bool held, UIElement? focused, bool typing)
    {
        if (_kit is not { } kit || _kitPanel is not { OwnKeys: false } panel)
        {
            return false;
        }

        PanelStroke stroke;
        try
        {
            stroke = kit.Key(panel.Content, key, held, focused, typing);
        }
        catch (Exception ex)
        {
            // Reading the panel asks every control of the plugin's own to describe itself.
            Log.Error("Panel", "a panel could not take a key for its selection", ex);
            return false;
        }

        if (!stroke.Used)
        {
            return false;
        }

        if (stroke.Command is not { } command)
        {
            // The selection moved, or the key had nothing to send. The panel is read again, which draws the ring where the selection is.
            Press("key", () => { });
        }
        else if (!PressKit(command, stroke.Value, null))
        {
            // A row that names a command and leaves the press to its plugin, such as a network.
            var value = stroke.Value;
            Press(command, () => _kitSend?.Invoke(command, value));
        }

        return true;
    }

    /// <summary>Draws the ring around the selected control of the open Kit panel, or hides it.</summary>
    private void MarkSelection()
    {
        if (_kit is not { } kit || _kitPanel is not { OwnKeys: false } panel)
        {
            _ring.Hide();
            return;
        }

        try
        {
            kit.Mark(panel.Content, _ring);
        }
        catch (Exception ex)
        {
            _ring.Hide();
            Log.Warning("Panel", "a panel could not be read for its selection", ex);
        }
    }

    /// <summary>The pointer pressed in the panel. The arrow keys' selection goes, and the next one starts from what was pressed.</summary>
    private void Deselect()
    {
        _kit?.Deselect();
        _ring.Hide();
    }

    /// <summary>Presses the control <paramref name="command"/> names on the open Kit panel, as <c>winoma summon</c> asks.</summary>
    /// <returns>False when no Kit panel is open or no control has that command, so the plugin's own handler takes it.</returns>
    public bool PressKit(string command, double? value, string? text)
    {
        if (_kit is null || _kitPanel is null || _kit.Find(_kitPanel.Content, command) is not { } control)
        {
            return false;
        }

        control.Press(value, text);
        return true;
    }

    /// <summary>Offers a key to the open Kit panel's plugin before the panel acts on it. True when the plugin used it.</summary>
    private bool OfferKey(KitKey key)
    {
        if (_kitView is not { } view)
        {
            return false;
        }

        bool used;
        try
        {
            used = view.OnKey(key);
        }
        catch (Exception ex)
        {
            Log.Error("Panel", "a panel could not take a key", ex);
            return false;
        }

        // A key the plugin left alone changed nothing, so nothing is read again for it.
        if (used)
        {
            Press("key", () => { });
        }

        return used;
    }

    private void DrawKit(bool refreshing, Window owner, BarEdge edge, FrameworkElement anchor, IKitPanel view, Action<string, Action> press, Action<string, double?> send, bool middle, Windows.Foundation.Rect? at)
    {
        _kitPress = press;
        _kitSend = send;
        if (refreshing && _kit is not null && ReferenceEquals(_kitView, view))
        {
            return;
        }

        _kitView = view;
        _middle = middle;

        // Building the rows can raise their SizeChanged at once, such as when a panel that scrolls follows one that does not.
        // That fits the panel, so the item it opens from is noted first. Otherwise it is placed at the last panel's item.
        Attach(owner, edge, anchor, at);
        BuildKit();
        var slideIn = !refreshing;
        if (slideIn)
        {
            StopSlide();

            // A fit of the same size is skipped as already placed. A new open is placed even at the last panel's size.
            _specWidth = -1;
            _specHeight = -1;
        }

        Fit(_kitPanel!.Width, _kitPanel.Height, 1, hold: slideIn);
        if (slideIn)
        {
            Veil();
        }
    }

    /// <summary>Builds the open view's rows. With <paramref name="keep"/>, the new rows take over the selection of the ones they replace.</summary>
    private void BuildKit(bool keep = false)
    {
        // A rebuild belongs to the Kit that asked. A plugin can still hold the Kit of a panel that has closed,
        // and its rebuild must not draw whichever panel is open now.
        ShellKit? made = null;
        made = new ShellKit(Press, () => QueueRebuild(made));
        _kitAsked = made;
        var kit = made;
        KitPanel panel;
        try
        {
            panel = _kitView!.BuildPanel(kit);
        }
        catch (Exception ex)
        {
            Log.Error("Panel", "a panel could not be drawn", ex);
            kit = new ShellKit(Press, () => { });
            panel = kit.Panel(kit.Note(() => "This panel could not be drawn. winoma.log has the details.", PanelTone.Critical));
        }

        if (_kit is { } replaced && !ReferenceEquals(replaced, kit))
        {
            if (keep)
            {
                kit.Keep(replaced);
            }

            replaced.Release();
        }

        // The ring was around a row that has just left. A rebuild puts it back once the new rows are in.
        _ring.Hide();

        _kit = kit;
        _kitPanel = panel;
        _rows.Children.Clear();
        _rows.Children.Add(panel.Content);
        Pad(panel.Padded);
        Scrolls(panel.Scrolls);

        // A panel that does not scroll lays its rows out in the window's own height, so the rows keep their size when
        // what they hold grows, as the Menu does from its search line. The content is laid out at its own height and says so.
        if (panel.Content is FrameworkElement content)
        {
            content.SizeChanged += (_, _) =>
            {
                if (ReferenceEquals(_kitPanel, panel))
                {
                    Fit(panel.Width, panel.Height, 1, hold: false);
                }
            };
        }
    }

    /// <summary>Builds the open Kit panel again, keeping the window where it is.</summary>
    private void RebuildKit()
    {
        if (_kitView is null || !_session.Live)
        {
            return;
        }

        BuildKit(keep: true);
        Fit(_kitPanel!.Width, _kitPanel.Height, 1, hold: false);
        MarkSelection();
    }

    private void Press(string command, Action action) => _kitPress?.Invoke(command, action);

    // The Kit the open view was handed. It is not always the one drawn: a view that threw is drawn from a Kit of the window's own,
    // and its plugin still asks through the one it was given.
    private ShellKit? _kitAsked;

    // The Kit whose rebuild is queued, or null. The newest asker wins, so a view that opened since is not left waiting.
    private ShellKit? _rebuildFor;

    private void QueueRebuild(ShellKit? kit)
    {
        var queued = _rebuildFor is not null;
        _rebuildFor = kit;
        if (queued || kit is null)
        {
            return;
        }

        var enqueued = DispatcherQueue.TryEnqueue(() =>
        {
            var asked = _rebuildFor;
            _rebuildFor = null;
            if (asked is not null && ReferenceEquals(_kitAsked, asked))
            {
                RebuildKit();
            }
        });
        if (!enqueued)
        {
            _rebuildFor = null;
        }
    }

    /// <summary>Drops the Kit panel, when a document panel draws or the panel leaves.</summary>
    private void ForgetKit()
    {
        _kitView = null;
        _kit?.Release();
        _kit = null;
        _kitAsked = null;
        _kitPanel = null;
        _kitSend = null;
        _ring.Hide();
    }
}

