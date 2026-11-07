using Microsoft.UI.Xaml;

namespace Winshell;

/// <summary>A window anchored to an edge of a screen. It can reserve that edge.</summary>
public sealed class PanelWindow : ShellWindow
{
    private Edges _anchors;
    private Thickness _margins;
    private double _reservedSize;
    private bool _coversScreen;
    private bool _aboveWindows = true;
    private bool _focusable;

    /// <summary>A hidden panel of the running shell, anchored to no edge until <see cref="Anchors"/> is set.</summary>
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

    /// <summary>
    /// The gap, in logical pixels, between the panel and each edge it is anchored to. Other edges' margins are unused.
    /// </summary>
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
    public double ReservedSize
    {
        get => _reservedSize;
        set
        {
            if (Set(ref _reservedSize, value))
                OnPlacementChanged();
        }
    }

    /// <summary>Lays the panel out on the whole screen, over the taskbar and other bars, and reserves nothing. False keeps it in the work area.</summary>
    public bool CoversScreen
    {
        get => _coversScreen;
        set
        {
            if (Set(ref _coversScreen, value))
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
                Host?.Focus.SetPanel(value);
        }
    }

    private protected override void ApplyPlacement()
    {
        ((PanelEdge)Host!.Placer).Set(new PanelPlacement(
            Screen,
            _anchors,
            _margins.Left,
            _margins.Top,
            _margins.Right,
            _margins.Bottom,
            double.IsNaN(Width) ? 100 : Width,
            double.IsNaN(Height) ? 100 : Height,
            _coversScreen,
            _reservedSize,
            _aboveWindows));
    }

    private protected override void ApplyFocus()
    {
        Host!.Focus.SetPanel(_focusable);
    }
}
