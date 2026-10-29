using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>
/// The items of one window: draws them, and delivers pointer and key input to them the way QtQuick does.
/// Windows on the platform each own one scene; tests use scenes directly.
/// </summary>
public sealed class Scene : IItemHost
{
    private readonly List<IHoverTarget> _hovered = [];
    private IPointerTarget? _grabber;
    private Item? _grabberItem;
    private int _buttons;
    private Item? _focusItem;
    private double _pressSceneX, _pressSceneY;
    private int _cursor = -1;

    public Scene(QObject? window = null)
    {
        WindowObject = window;
        Root = new Item();
        Root.SetHost(this);
    }

    /// <summary>The window's content item. Everything in the window is under it.</summary>
    public Item Root { get; }

    public QObject? WindowObject { get; }

    /// <summary>Physical pixels per logical pixel. Items that raster at it, as images and canvases do, redraw when it changes.</summary>
    public double DevicePixelRatio
    {
        get => _devicePixelRatio;
        set
        {
            if (_devicePixelRatio == value)
                return;
            _devicePixelRatio = value;
            Root.PixelRatioChanged();
            RequestUpdate();
        }
    }

    private double _devicePixelRatio = 1;

    /// <summary>The window is showing a backdrop, or its color is translucent, so text is grayscale.</summary>
    public bool TextOnBackdrop { get; set; }

    public Item? FocusItem => _focusItem;

    /// <summary>
    /// Whether focus last moved by the keyboard. The focused item shows a focus rectangle only then, as in Windows;
    /// a press of the pointer hides it again.
    /// </summary>
    public bool FocusVisible { get; private set; }

    private void SetFocusVisible(bool value)
    {
        if (FocusVisible == value)
            return;
        FocusVisible = value;
        (_focusItem as Controls.Control)?.VisualFocusChanged();
        RequestUpdate();
    }

    /// <summary>Raised when something in the scene changed and the window should draw again.</summary>
    public event Action? UpdateRequested;

    /// <summary>
    /// Raised after each frame is drawn. An item that places something the window shows over the scene, as a DWM
    /// thumbnail is, puts it where the item was drawn, or hides it when the item was not.
    /// </summary>
    public event Action? Painted;

    /// <summary>Raised when the cursor shape the pointer is over changes. -1 means the default arrow.</summary>
    public event Action<int>? CursorChanged;

    /// <summary>Raised when an item with keyboard focus appears or goes, so the window can take or give up focus.</summary>
    public event Action<Item?>? FocusChanged;

    public bool IsDirty { get; private set; } = true;

    public void RequestUpdate()
    {
        if (IsDirty)
            return;
        IsDirty = true;
        UpdateRequested?.Invoke();
    }

    public void SetFocusItem(Item? item)
    {
        if (_focusItem == item)
            return;
        var old = _focusItem;
        _focusItem = item;
        old?.SetActiveFocus(false);
        item?.SetActiveFocus(true);
        // Siblings in the same focus scope lose their focus flag.
        if (old is not null && item is not null && old.Focus)
            old.Focus = false;
        if (FocusVisible)
            RequestUpdate();
        FocusChanged?.Invoke(item);
    }

    /// <summary>Moves focus from the keyboard, so the focused item shows its focus rectangle.</summary>
    public void MoveFocus(Item item)
    {
        SetFocusVisible(true);
        item.ForceActiveFocus();
    }

    // Painting

    /// <summary>Draws the scene. The canvas is in physical pixels; the scene scales by the pixel ratio.</summary>
    public PaintContext Paint(SKCanvas canvas, TimeSpan time)
    {
        PolishQueue.Flush();
        IsDirty = false;
        var ring = FocusVisible && _focusItem is { } focused && focused is not Quick.TextInput && focused.Visible ? focused : null;
        var context = new PaintContext { DevicePixelRatio = DevicePixelRatio, Time = time, TextOnBackdrop = TextOnBackdrop, FocusRing = ring };
        canvas.Save();
        canvas.Scale((float)DevicePixelRatio);
        using (DependencyTracker.Suspend())
            PaintItem(Root, canvas, context);
        canvas.Restore();
        Painted?.Invoke();
        return context;
    }

    public static void PaintItem(Item item, SKCanvas canvas, PaintContext context)
    {
        if (!item.IsVisibleFlag || item.Opacity <= 0)
            return;
        canvas.Save();
        canvas.Translate((float)item.X, (float)item.Y);
        if (item.Scale != 1 || item.Rotation != 0)
        {
            var (ox, oy) = Origin(item);
            canvas.Translate(ox, oy);
            if (item.Rotation != 0)
                canvas.RotateDegrees((float)item.Rotation);
            if (item.Scale != 1)
                canvas.Scale((float)item.Scale);
            canvas.Translate(-ox, -oy);
        }
        var layered = item.Opacity < 1;
        if (layered)
        {
            using var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(item.Opacity * 255)) };
            canvas.SaveLayer(paint);
            context.LayerDepth++;
        }
        if (item.Layer.Enabled && item.Layer.Effect is IItemEffect layerEffect)
        {
            layerEffect.PaintWithEffect(item, canvas, context, () => PaintContent(item, canvas, context));
        }
        else
        {
            PaintContent(item, canvas, context);
        }
        if (context.FocusRing == item)
            Controls.Fluent.FocusRing(canvas, SKRect.Create((float)item.Width, (float)item.Height), (item as Controls.Control)?.FocusRadius ?? Controls.Fluent.ControlCornerRadius);
        if (layered)
        {
            context.LayerDepth--;
            canvas.Restore();
        }
        canvas.Restore();
    }

    private static void PaintContent(Item item, SKCanvas canvas, PaintContext context)
    {
        item.Paint(canvas, context);
        var clipped = item.Clip;
        if (clipped)
        {
            canvas.Save();
            if (item.ClipShape is { } shape && item is IRoundedClip)
            {
                canvas.ClipRoundRect(shape, SKClipOperation.Intersect, true);
                shape.Dispose();
            }
            else
            {
                canvas.ClipRect(SKRect.Create(0, 0, (float)item.Width, (float)item.Height));
            }
        }
        foreach (var child in item.PaintOrder)
            PaintItem(child, canvas, context);
        if (item is IPaintOverChildren over)
            over.PaintOver(canvas, context);
        if (clipped)
            canvas.Restore();
    }

    private static (float X, float Y) Origin(Item item)
    {
        var w = (float)item.Width;
        var h = (float)item.Height;
        return (Item.TransformOriginValue)item.TransformOrigin switch
        {
            Item.TransformOriginValue.TopLeft => (0, 0),
            Item.TransformOriginValue.Top => (w / 2, 0),
            Item.TransformOriginValue.TopRight => (w, 0),
            Item.TransformOriginValue.Left => (0, h / 2),
            Item.TransformOriginValue.Right => (w, h / 2),
            Item.TransformOriginValue.BottomLeft => (0, h),
            Item.TransformOriginValue.Bottom => (w / 2, h),
            Item.TransformOriginValue.BottomRight => (w, h),
            _ => (w / 2, h / 2),
        };
    }

    // Hit testing

    /// <summary>Converts a scene point into an item's coordinates, undoing scale and rotation on the way.</summary>
    public static (double X, double Y) ToLocal(Item item, double sceneX, double sceneY)
    {
        var chain = new List<Item>();
        for (var i = item; i is not null; i = i.ParentItem)
            chain.Add(i);
        chain.Reverse();
        double x = sceneX, y = sceneY;
        foreach (var i in chain.Skip(1))
        {
            x -= i.X;
            y -= i.Y;
            if (i.Scale != 1 || i.Rotation != 0)
            {
                var (ox, oy) = Origin(i);
                x -= ox;
                y -= oy;
                if (i.Rotation != 0)
                {
                    var r = -i.Rotation * Math.PI / 180;
                    (x, y) = (x * Math.Cos(r) - y * Math.Sin(r), x * Math.Sin(r) + y * Math.Cos(r));
                }
                if (i.Scale != 0)
                {
                    x /= i.Scale;
                    y /= i.Scale;
                }
                x += ox;
                y += oy;
            }
        }
        return (x, y);
    }

    /// <summary>The items under a point, topmost first, that are visible and not clipped away.</summary>
    private static void ItemsAt(Item item, double x, double y, List<(Item Item, double X, double Y)> result)
    {
        if (!item.IsVisibleFlag || item.Opacity <= 0)
            return;
        var lx = x - item.X;
        var ly = y - item.Y;
        if (item.Scale != 1 || item.Rotation != 0)
        {
            var (ox, oy) = Origin(item);
            lx -= ox;
            ly -= oy;
            if (item.Rotation != 0)
            {
                var r = -item.Rotation * Math.PI / 180;
                (lx, ly) = (lx * Math.Cos(r) - ly * Math.Sin(r), lx * Math.Sin(r) + ly * Math.Cos(r));
            }
            if (item.Scale != 0)
            {
                lx /= item.Scale;
                ly /= item.Scale;
            }
            lx += ox;
            ly += oy;
        }
        if (item.Clip && !(lx >= 0 && ly >= 0 && lx < item.Width && ly < item.Height))
            return;
        foreach (var child in item.PaintOrder.Reverse())
            ItemsAt(child, lx, ly, result);
        if (item.HitTest(lx, ly))
            result.Add((item, lx, ly));
    }

    private List<(Item Item, double X, double Y)> HitList(double x, double y)
    {
        var list = new List<(Item, double, double)>();
        using (DependencyTracker.Suspend())
        {
            // The root is the window itself; its own coordinates are the scene's.
            foreach (var child in Root.PaintOrder.Reverse())
                ItemsAt(child, x, y, list);
            if (Root.HitTest(x, y))
                list.Add((Root, x, y));
        }
        return list;
    }

    // Pointer input, in scene (logical) coordinates.

    public void PointerMove(double x, double y, int modifiers = 0)
    {
        UpdateHover(x, y, modifiers);
        if (_grabber is not null && _grabberItem is not null)
        {
            // A Flickable under the grabber can take a drag over.
            if (_grabber is not Flickable && !(_grabber is MouseArea { PreventStealing: true }) && !(_grabber is Controls.IKeepsPointerGrab { KeepsGrab: true }))
            {
                for (var p = _grabberItem.ParentItem; p is not null; p = p.ParentItem)
                {
                    if (p is IPointerTarget stealer && stealer.WantsToSteal(x - _pressSceneX, y - _pressSceneY))
                    {
                        _grabber.PointerCancel();
                        _grabber = stealer;
                        _grabberItem = p;
                        var (sx, sy) = ToLocal(p, _pressSceneX, _pressSceneY);
                        stealer.PointerPress(sx, sy, QtEnums.LeftButton, _buttons, modifiers);
                        break;
                    }
                }
            }
            var (lx, ly) = ToLocal(_grabberItem, x, y);
            _grabber.PointerMove(lx, ly, _buttons, modifiers);
        }
    }

    public void PointerPress(double x, double y, int button, int modifiers = 0, bool doubleClick = false)
    {
        _buttons |= button;
        SetFocusVisible(false);
        _pressSceneX = x;
        _pressSceneY = y;
        if (_grabber is not null && _grabberItem is not null)
        {
            // Another button while one is held goes to the same item.
            var (gx, gy) = ToLocal(_grabberItem, x, y);
            _grabber.PointerPress(gx, gy, button, _buttons, modifiers);
            return;
        }
        var focusTaken = false;
        foreach (var (item, lx, ly) in HitList(x, y))
        {
            if (!focusTaken && item is IFocusOnPress { FocusOnPress: true } && item.Enabled)
            {
                item.ForceActiveFocus();
                focusTaken = true;
            }
            foreach (var handler in item.PointerHandlers)
            {
                if (handler is IPointerTarget h && handler.Enabled && h.AcceptsButton(button) && h.PointerPress(lx, ly, button, _buttons, modifiers))
                {
                    _grabber = h;
                    _grabberItem = item;
                    return;
                }
            }
            if (item is not IPointerTarget target || !target.AcceptsButton(button))
                continue;
            if (doubleClick)
            {
                target.PointerDoubleClick(lx, ly, button, _buttons, modifiers);
                _grabber = target;
                _grabberItem = item;
                return;
            }
            if (target.PointerPress(lx, ly, button, _buttons, modifiers))
            {
                _grabber = target;
                _grabberItem = item;
                return;
            }
        }
        if (!focusTaken && _focusItem is not null)
        {
            // A click on nothing that takes focus clears it, as a click outside a text field does.
            SetFocusItem(null);
        }
    }

    public void PointerRelease(double x, double y, int button, int modifiers = 0)
    {
        _buttons &= ~button;
        if (_grabber is not null && _grabberItem is not null)
        {
            var (lx, ly) = ToLocal(_grabberItem, x, y);
            var grabber = _grabber;
            if (_buttons == 0)
            {
                _grabber = null;
                _grabberItem = null;
            }
            grabber.PointerRelease(lx, ly, button, _buttons, modifiers);
        }
        UpdateHover(x, y, modifiers);
    }

    /// <summary>The pointer left the window.</summary>
    public void PointerLeave()
    {
        foreach (var h in _hovered.ToArray())
            h.HoverLeave();
        _hovered.Clear();
        SetCursor(-1);
    }

    /// <summary>The window lost the pointer while a button was held, as when another window took it.</summary>
    public void PointerCancel()
    {
        _grabber?.PointerCancel();
        _grabber = null;
        _grabberItem = null;
        _buttons = 0;
    }

    public void Wheel(double x, double y, double angleX, double angleY, int modifiers = 0, double pixelX = 0, double pixelY = 0)
    {
        foreach (var (item, lx, ly) in HitList(x, y))
        {
            foreach (var handler in item.PointerHandlers)
            {
                if (handler is IWheelTarget h && handler.Enabled && h.Wheel(lx, ly, angleX, angleY, pixelX, pixelY, _buttons, modifiers, false))
                    return;
            }
            if (item is IWheelTarget target && item.Enabled && target.Wheel(lx, ly, angleX, angleY, pixelX, pixelY, _buttons, modifiers, false))
                return;
        }
    }

    private void UpdateHover(double x, double y, int modifiers)
    {
        var now = new List<(IHoverTarget Target, double X, double Y)>();
        using (DependencyTracker.Suspend())
            CollectHover(Root, x, y, now, isRoot: true);
        foreach (var old in _hovered.ToArray())
        {
            if (!now.Any(n => n.Target == old))
            {
                old.HoverLeave();
                _hovered.Remove(old);
            }
        }
        foreach (var (target, lx, ly) in now)
        {
            if (_hovered.Contains(target))
            {
                target.HoverMove(lx, ly, modifiers);
            }
            else
            {
                _hovered.Add(target);
                target.HoverEnter(lx, ly, modifiers);
            }
        }
        var cursor = -1;
        if (_grabber is IHoverTarget { CursorShape: >= 0 } g)
            cursor = g.CursorShape;
        else
        {
            foreach (var (target, _, _) in now)
            {
                if (target.CursorShape >= 0)
                {
                    cursor = target.CursorShape;
                    break;
                }
            }
        }
        SetCursor(cursor);
    }

    private void SetCursor(int cursor)
    {
        if (_cursor == cursor)
            return;
        _cursor = cursor;
        CursorChanged?.Invoke(cursor);
    }

    /// <summary>
    /// Collects the hover targets under a point, topmost first. Hover passes through items that do not take it,
    /// and an item that takes it is hovered with the items above it that do.
    /// </summary>
    private static bool CollectHover(Item item, double x, double y, List<(IHoverTarget, double, double)> result, bool isRoot = false)
    {
        if (!item.IsVisibleFlag || !item.Enabled)
            return false;
        var lx = isRoot ? x : x - item.X;
        var ly = isRoot ? y : y - item.Y;
        if (!isRoot && (item.Scale != 1 || item.Rotation != 0))
        {
            var (ox, oy) = Origin(item);
            lx = (lx - ox) / (item.Scale == 0 ? 1 : item.Scale) + ox;
            ly = (ly - oy) / (item.Scale == 0 ? 1 : item.Scale) + oy;
        }
        if (item.Clip && !(lx >= 0 && ly >= 0 && lx < item.Width && ly < item.Height))
            return false;
        var accepted = false;
        foreach (var child in item.PaintOrder.Reverse())
        {
            if (CollectHover(child, lx, ly, result))
            {
                accepted = true;
                break;
            }
        }
        foreach (var handler in item.PointerHandlers)
        {
            if (handler is HoverHandler { AcceptsHover: true } hover && hover.HitTest(item, lx, ly))
            {
                result.Add((hover, lx, ly));
                if (hover.Blocking)
                    accepted = true;
            }
        }
        if (item is IHoverTarget { AcceptsHover: true } target && item.HitTest(lx, ly))
        {
            result.Add((target, lx, ly));
            return true;
        }
        return accepted;
    }

    // Keys

    /// <summary>
    /// Delivers a key press to the focused item, then up through its parents. A key none of them takes moves focus:
    /// Tab and Shift+Tab to the next tab stop, an arrow key within a group, Escape out of the topmost popup.
    /// </summary>
    public bool KeyPress(KeyEvent e)
    {
        for (var item = _focusItem; item is not null; item = item.ParentItem)
        {
            if (item.FindAttached(typeof(KeysAttached)) is KeysAttached keys && keys.Priority == 0 && keys.Press(e))
                return true;
            if (item is IKeyTarget target && target.KeyPress(e))
                return true;
            if (item.FindAttached(typeof(KeysAttached)) is KeysAttached after && after.Priority == 1 && after.Press(e))
                return true;
        }
        return Navigate(e);
    }

    private bool Navigate(KeyEvent e)
    {
        var mods = e.Modifiers & (QtEnums.ShiftModifier | QtEnums.ControlModifier | QtEnums.AltModifier);
        switch (e.Key)
        {
            case 0x01000001 or 0x01000002 when (mods & ~QtEnums.ShiftModifier) == 0:
                {
                    var forward = e.Key == 0x01000001 && mods == 0;
                    if (FocusNavigation.Next(Root, _focusItem, forward) is not { } next)
                        return false;
                    MoveFocus(next);
                    return true;
                }
            case 0x01000012 or 0x01000013 or 0x01000014 or 0x01000015 when mods == 0 && _focusItem is not null:
                {
                    if (FocusNavigation.Arrow(_focusItem, e.Key) is not { } arrow)
                        return false;
                    MoveFocus(arrow.Target);
                    arrow.Select?.Invoke(arrow.Target);
                    return true;
                }
            case 0x01000000:
                {
                    var popup = Controls.PopupOverlay.OpenIn(this).LastOrDefault(p => p.Interactive && (p.ClosePolicy & (int)Controls.Popup.ClosePolicyValue.CloseOnEscape) != 0);
                    if (popup is null)
                        return false;
                    popup.Close();
                    return true;
                }
        }
        return false;
    }

    public bool KeyRelease(KeyEvent e)
    {
        for (var item = _focusItem; item is not null; item = item.ParentItem)
        {
            if (item.FindAttached(typeof(KeysAttached)) is KeysAttached keys && keys.Release(e))
                return true;
            if (item is IKeyTarget target && target.KeyRelease(e))
                return true;
        }
        return false;
    }

    /// <summary>Text typed on the keyboard, after the key press that made it.</summary>
    public void TextInput(string text)
    {
        if (_focusItem is ITextTarget target)
            target.InsertText(text);
    }

    /// <summary>The focused field shows this string as an IME preedit. It is not committed text.</summary>
    public void SetComposition(string text)
    {
        if (_focusItem is ITextTarget target)
            target.SetComposition(text);
    }

    /// <summary>The IME finished a composition. The string is inserted at the caret.</summary>
    public void CommitComposition(string text)
    {
        if (_focusItem is ITextTarget target)
            target.CommitComposition(text);
    }

    /// <summary>The IME cancelled the preedit.</summary>
    public void CancelComposition()
    {
        if (_focusItem is ITextTarget target)
            target.CancelComposition();
    }

    /// <summary>The caret, in the content item's coordinates, so the candidate window can sit under it.</summary>
    public QRect ImeCaret()
    {
        if (_focusItem is not ITextTarget target)
            return default;
        var local = target.ImeCaret();
        return _focusItem.MapToItem(Root, local.X, local.Y, local.Width, local.Height) is QRect rect ? rect : default;
    }
}

/// <summary>An item that draws on top of its children too, such as a focus ring.</summary>
public interface IPaintOverChildren
{
    void PaintOver(SKCanvas canvas, PaintContext context);
}

/// <summary>An effect set as <c>layer.effect</c>: draws the item through it.</summary>
public interface IItemEffect
{
    void PaintWithEffect(Item item, SKCanvas canvas, PaintContext context, Action paintItem);
}

/// <summary>An item that takes typed text while focused.</summary>
public interface ITextTarget
{
    void InsertText(string text);

    void SetComposition(string text) { }

    void CommitComposition(string text) { }

    void CancelComposition() { }

    QRect ImeCaret() => default;
}

/// <summary>An item that takes keyboard focus when it is pressed, such as a text field.</summary>
public interface IFocusOnPress
{
    bool FocusOnPress { get; }
}

/// <summary>An item whose clip follows its rounded corners, as ClippingRectangle's does.</summary>
public interface IRoundedClip;
