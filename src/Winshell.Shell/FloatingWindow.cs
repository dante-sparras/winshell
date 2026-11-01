namespace Winshell;

/// <summary>A normal window with a title bar, as Winshell Settings is.</summary>
public sealed class FloatingWindow : ShellWindow
{
    private string _title = "";
    private QSize _minimumSize;
    private QSize _maximumSize;
    private bool _maximized;
    private bool _minimized;
    private bool _fullscreen;
    private bool _stateRequested;
    private FloatingWindow? _owner;

    public FloatingWindow()
        : this(Shell.Current)
    {
    }

    internal FloatingWindow(Shell shell)
        : base(shell)
    {
    }

    private protected override WindowKind Kind => WindowKind.Floating;

    public string Title
    {
        get => _title;
        set
        {
            if (Set(ref _title, value ?? ""))
                OnPlacementChanged();
        }
    }

    public QSize MinimumSize
    {
        get => _minimumSize;
        set
        {
            if (Set(ref _minimumSize, value))
                OnPlacementChanged();
        }
    }

    public QSize MaximumSize
    {
        get => _maximumSize;
        set
        {
            if (Set(ref _maximumSize, value))
                OnPlacementChanged();
        }
    }

    public bool Maximized
    {
        get => _maximized;
        set => RequestState(ref _maximized, value);
    }

    public bool Minimized
    {
        get => _minimized;
        set => RequestState(ref _minimized, value);
    }

    public bool Fullscreen
    {
        get => _fullscreen;
        set => RequestState(ref _fullscreen, value);
    }

    /// <summary>The window this one stays above and closes with.</summary>
    public FloatingWindow? Owner
    {
        get => _owner;
        set
        {
            if (Set(ref _owner, value))
                OnPlacementChanged();
        }
    }

    private void RequestState(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
    {
        if (!Set(ref field, value, property))
            return;
        _stateRequested = true;
        OnPlacementChanged();
    }

    private protected override void ApplyPlacement()
    {
        Host!.SetFloating(new FloatingPlacement(
            Screen,
            _title,
            double.IsNaN(Width) ? 640 : Width,
            double.IsNaN(Height) ? 480 : Height,
            _minimumSize,
            _maximumSize,
            _maximized,
            _minimized,
            _fullscreen,
            _owner?.Host,
            _stateRequested));
        _stateRequested = false;
    }

    private protected override void OnStateChanged(bool maximized, bool minimized)
    {
        Set(ref _maximized, maximized, nameof(Maximized));
        Set(ref _minimized, minimized, nameof(Minimized));
    }

    private protected override void OnFullscreenChanged(bool fullscreen) => Set(ref _fullscreen, fullscreen, nameof(Fullscreen));
}
