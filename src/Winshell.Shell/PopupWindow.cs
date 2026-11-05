using Windows.Foundation;

namespace Winshell;

/// <summary>A frameless window placed next to another window.</summary>
public sealed class PopupWindow : ShellWindow
{
    private Rect _rect = new(0, 0, 100, 100);
    private bool _grabFocus;

    /// <summary>
    /// A hidden popup of the running shell, 100 by 100 at the virtual desktop's origin until
    /// <see cref="PopupWindow.Rect"/> is set.
    /// </summary>
    public PopupWindow()
        : this(Shell.Current)
    {
    }

    internal PopupWindow(Shell shell)
        : base(shell)
    {
    }

    private protected override WindowKind Kind => WindowKind.Popup;

    /// <summary>Where the popup sits, in logical pixels on the virtual desktop.</summary>
    public Rect Rect
    {
        get => _rect;
        set
        {
            if (Set(ref _rect, value))
                OnPlacementChanged();
        }
    }

    /// <summary>Takes the keyboard while shown. A click outside the popup, or Esc, then dismisses it.</summary>
    public bool GrabFocus
    {
        get => _grabFocus;
        set
        {
            if (Set(ref _grabFocus, value))
                OnPlacementChanged();
        }
    }

    /// <summary>A click outside the popup, or Esc, dismissed it. It is hidden.</summary>
    public event Action? Dismissed;

    private protected override void ApplyPlacement() => Host!.Placement.SetPopup(new PopupPlacement(Screen, _rect, _grabFocus));

    private protected override void OnDismissed()
    {
        Visible = false;
        Dismissed?.Invoke();
    }
}
