namespace Winshell.Runtime.Quick;

/// <summary>
/// QtQuick's attached <c>Accessible</c>: what assistive technology is told an item is. It overrides what the
/// built-in controls say about themselves, and lets an item built from a MouseArea say it is a button.
/// </summary>
[QmlSignal("pressAction")]
[QmlSignal("toggleAction")]
public sealed class AccessibleAttached(QObject owner) : QObject
{
    private int _role;
    private string _name = "";
    private string _description = "";
    private bool _ignored;
    private bool _checkable;
    private bool _checked;

    /// <summary>Qt's <c>QAccessible::Role</c> values, and <c>Switch</c> for a toggle switch.</summary>
    [QmlEnum]
    public enum RoleValue
    {
        NoRole = 0x00,
        TitleBar = 0x01,
        MenuBar = 0x02,
        ScrollBar = 0x03,
        Window = 0x09,
        Client = 0x0A,
        PopupMenu = 0x0B,
        MenuItem = 0x0C,
        ToolTip = 0x0D,
        Application = 0x0E,
        Document = 0x0F,
        Pane = 0x10,
        Dialog = 0x12,
        Grouping = 0x14,
        Separator = 0x15,
        ToolBar = 0x16,
        StatusBar = 0x17,
        Table = 0x18,
        Cell = 0x1D,
        Link = 0x1E,
        List = 0x21,
        ListItem = 0x22,
        Tree = 0x23,
        TreeItem = 0x24,
        PageTab = 0x25,
        Indicator = 0x27,
        Graphic = 0x28,
        StaticText = 0x29,
        EditableText = 0x2A,
        Button = 0x2B,
        CheckBox = 0x2C,
        RadioButton = 0x2D,
        ComboBox = 0x2E,
        ProgressBar = 0x30,
        Dial = 0x31,
        Slider = 0x33,
        SpinBox = 0x34,
        PageTabList = 0x3C,
        LayeredPane = 0x80,
        Paragraph = 0x83,
        Section = 0x85,
        Notification = 0x86,
        Heading = 0x414,
        Switch = 0x10000,
    }

    [Qml]
    public int Role
    {
        get { Track(); return _role; }
        set { if (SetField(ref _role, value)) Changed(); }
    }

    [Qml]
    public string Name
    {
        get { Track(); return _name; }
        set { if (SetField(ref _name, value ?? "")) Changed(); }
    }

    [Qml]
    public string Description
    {
        get { Track(); return _description; }
        set { if (SetField(ref _description, value ?? "")) Changed(); }
    }

    /// <summary>Whether to leave the item out. Its children are still exposed, in its place.</summary>
    [Qml]
    public bool Ignored
    {
        get { Track(); return _ignored; }
        set { if (SetField(ref _ignored, value)) Changed(); }
    }

    [Qml]
    public bool Checkable
    {
        get { Track(); return _checkable; }
        set { if (SetField(ref _checkable, value)) Changed(); }
    }

    [Qml]
    public bool Checked
    {
        get { Track(); return _checked; }
        set { if (SetField(ref _checked, value)) Changed(); }
    }

    private int _headingLevel;

    /// <summary>
    /// The heading level, from 1 to 9. Zero, the default, means level 2. Narrator uses it when
    /// <c>role</c> is <c>Heading</c>.
    /// </summary>
    [Qml]
    public int HeadingLevel
    {
        get { Track(); return _headingLevel; }
        set { if (SetField(ref _headingLevel, value)) Changed(); }
    }

    // An accessibility change draws a frame, after which the window tells UI Automation what changed.
    private void Changed()
    {
        if (owner is not Item item)
            return;
        item.Update();
        if (item.Host is Scene scene)
            scene.Signals.MarkDirty();
        if (_role == (int)RoleValue.Notification)
            item.TrackNotification();
        else
            item.UntrackNotification();
    }
}
