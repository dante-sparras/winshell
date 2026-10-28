namespace Winshell.Runtime.Quickshell;

/// <summary>
/// Quickshell's <c>QsMenuHandle</c>: a menu that QML can open or read. A service subclasses it: a menu it can only
/// show natively overrides <see cref="Display"/>; one it can read lists its <see cref="Entries"/>.
/// </summary>
[QmlSignal("menuChanged")]
public class QsMenuHandle : QObject
{
    /// <summary>The entries of the menu, for <c>QsMenuOpener.children</c>.</summary>
    public virtual IReadOnlyList<QsMenuEntry> Entries => [];

    /// <summary>Shows the menu as a platform menu at a point relative to a window, or under the pointer.</summary>
    public virtual void Display(QsWindow? window, double x, double y)
    {
    }

    /// <summary>The point on the virtual desktop, in physical pixels, of a point relative to a window.</summary>
    public static (int X, int Y)? ScreenPoint(QsWindow? window, double x, double y) => window?.Backend?.MapToScreen(x, y);
}

/// <summary>Quickshell's <c>QsMenuEntry</c>: one entry of a menu.</summary>
[QmlSignal("triggered")]
public class QsMenuEntry : QsMenuHandle
{
    [Qml] public virtual bool Enabled => true;

    [Qml] public virtual bool IsSeparator => false;

    [Qml] public virtual bool HasChildren => Entries.Count > 0;

    [Qml] public virtual int CheckState => 0;

    [Qml] public virtual string Text => "";

    [Qml] public virtual int ButtonType => 0;

    [Qml] public virtual string Icon => "";

    [Qml("display")]
    public void DisplayAt(QsWindow? parentWindow, double relativeX, double relativeY) => Display(parentWindow, relativeX, relativeY);
}

/// <summary>Quickshell's <c>QsMenuButtonType</c>.</summary>
public enum QsMenuButtonType
{
    None = 0,
    CheckBox = 1,
    RadioButton = 2,
}

/// <summary>Quickshell's <c>QsMenuOpener</c>: the entries of a menu, to draw a menu in QML.</summary>
public sealed class QsMenuOpener : QObject
{
    private QsMenuHandle? _menu;
    private IDisposable? _changed;

    public QsMenuOpener()
    {
        Children = new ObjectModel();
        Children.SetOwner(this);
    }

    [Qml]
    public QsMenuHandle? Menu
    {
        get { Track(); return _menu; }
        set
        {
            if (!SetField(ref _menu, value))
                return;
            _changed?.Dispose();
            _changed = value?.Connect("menuChanged", _ => Refresh());
            Refresh();
        }
    }

    [Qml] public ObjectModel Children { get; }

    private void Refresh() => Children.Set(_menu?.Entries.Cast<object?>().ToList() ?? []);

    protected override void OnDestroy() => _changed?.Dispose();
}

/// <summary>Quickshell's <c>QsMenuAnchor</c>: opens a menu as a platform menu next to an anchor.</summary>
[QmlSignal("opened")]
[QmlSignal("closed")]
public sealed class QsMenuAnchor : QObject
{
    private QsMenuHandle? _menu;
    private bool _visible;

    public QsMenuAnchor()
    {
        Anchor = new PopupAnchor(() => { });
        Anchor.SetOwner(this);
    }

    [Qml] public QsMenuHandle? Menu { get { Track(); return _menu; } set => SetField(ref _menu, value); }

    [Qml] public PopupAnchor Anchor { get; }

    [Qml] public bool Visible { get { Track(); return _visible; } }

    [Qml]
    public void Open()
    {
        if (_menu is null || _visible)
            return;
        var rect = Anchor.AnchorRectInWindow();
        SetField(ref _visible, true, "visible");
        Emit("opened");
        // A platform menu runs on its own; the anchor is free again once it has been shown.
        _menu.Display(Anchor.Window, rect.X, rect.Y + rect.Height);
        Engine?.Loop.Post(Close);
    }

    [Qml]
    public void Close()
    {
        if (!SetField(ref _visible, false, "visible"))
            return;
        Emit("closed");
    }
}
