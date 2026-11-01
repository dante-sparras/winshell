using Microsoft.UI.Xaml;

namespace Winshell;

/// <summary>How a panel treats the edges other bars reserve.</summary>
public enum PanelExclusionMode
{
    /// <summary>Stays inside the work area, and reserves <see cref="PanelWindow.ExclusiveZone"/> if that is set.</summary>
    Normal = 0,

    /// <summary>Covers the whole screen, over the taskbar and other bars.</summary>
    Ignore = 1,
}

/// <summary>A window anchored to an edge of a screen. It can reserve that edge.</summary>
public sealed class PanelWindow : ShellWindow
{
    private Edges _anchors;
    private Thickness _margins;
    private double _exclusiveZone;
    private PanelExclusionMode _exclusionMode;
    private bool _aboveWindows = true;
    private bool _focusable;
    private string _namespace = "winshell";

    public PanelWindow()
        : this(Shell.Current)
    {
    }

    internal PanelWindow(Shell shell)
        : base(shell)
    {
    }

    private protected override WindowKind Kind => WindowKind.Panel;

    /// <summary>The edges the panel sticks to. Two opposite edges stretch it across the screen.</summary>
    public Edges Anchors
    {
        get => _anchors;
        set
        {
            if (Set(ref _anchors, value))
                OnPlacementChanged();
        }
    }

    public Thickness Margins
    {
        get => _margins;
        set
        {
            if (Set(ref _margins, value))
                OnPlacementChanged();
        }
    }

    /// <summary>How much of its edge the panel reserves, in logical pixels, so maximized windows stay out of it.</summary>
    public double ExclusiveZone
    {
        get => _exclusiveZone;
        set
        {
            if (Set(ref _exclusiveZone, value))
                OnPlacementChanged();
        }
    }

    public PanelExclusionMode ExclusionMode
    {
        get => _exclusionMode;
        set
        {
            if (Set(ref _exclusionMode, value))
                OnPlacementChanged();
        }
    }

    /// <summary>Stays above normal windows. False puts it below them, as a wallpaper is.</summary>
    public bool AboveWindows
    {
        get => _aboveWindows;
        set
        {
            if (Set(ref _aboveWindows, value))
                OnPlacementChanged();
        }
    }

    /// <summary>Whether the panel can take the keyboard. A panel that cannot never takes activation from the app in front.</summary>
    public bool Focusable
    {
        get => _focusable;
        set
        {
            if (Set(ref _focusable, value))
                OnPlacementChanged();
        }
    }

    public string Namespace
    {
        get => _namespace;
        set
        {
            if (Set(ref _namespace, value))
                OnPlacementChanged();
        }
    }

    private protected override void ApplyPlacement() => Host!.SetPanel(new PanelPlacement(
        Screen,
        _anchors,
        _margins.Left,
        _margins.Top,
        _margins.Right,
        _margins.Bottom,
        double.IsNaN(Width) ? 100 : Width,
        double.IsNaN(Height) ? 100 : Height,
        (int)_exclusionMode,
        _exclusiveZone,
        _aboveWindows,
        _focusable,
        _namespace));
}
