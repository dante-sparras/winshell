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
        set
        {
            if (!Set(ref _maximized, value))
                return;
            // Fullscreen is borderless, so maximized is false while it is on. Setting it true leaves fullscreen, and
            // only this says what comes next: leaving by Fullscreen = false goes back to what the window was.
            if (value && _fullscreen)
            {
                _fullscreen = false;
                Changed(nameof(Fullscreen));
                _stateRequested = true;
            }
            OnPlacementChanged();
        }
    }

    public bool Minimized
    {
        get => _minimized;
        set
        {
            if (Set(ref _minimized, value))
                OnPlacementChanged();
        }
    }

    /// <summary>Whether the window is borderless and covering its monitor. A minimized fullscreen window stays fullscreen.</summary>
    public bool Fullscreen
    {
        get => _fullscreen;
        set
        {
            if (Set(ref _fullscreen, value))
                OnPlacementChanged();
        }
    }

    /// <summary>
    /// The window this one stays above and closes with. It can be set before that window exists, or changed later. A
    /// window can't own itself or a window that owns it.
    /// </summary>
    public FloatingWindow? Owner
    {
        get => _owner;
        set
        {
            if (ReferenceEquals(_owner, value) || ReferenceEquals(value, this) || (value is not null && WouldOwn(value)))
                return;
            if (_owner is { } previous)
            {
                previous.HostCreated -= OnPlacementChanged;
                previous.Disposing -= OnOwnerDisposing;
            }
            _owner = value;
            if (value is not null)
            {
                value.HostCreated += OnPlacementChanged;
                value.Disposing += OnOwnerDisposing;
            }
            Changed();
            OnPlacementChanged();
        }
    }

    /// <summary>The native owner stays linked: Windows then closes this window, and its handle goes with it.</summary>
    private void OnOwnerDisposing()
    {
        if (_owner is not { } owner)
            return;
        owner.HostCreated -= OnPlacementChanged;
        owner.Disposing -= OnOwnerDisposing;
        _owner = null;
        Changed(nameof(Owner));
    }

    private bool WouldOwn(FloatingWindow owner)
    {
        FloatingWindow? hop = owner;
        for (var n = 0; hop is not null && n < 16; n++)
        {
            if (ReferenceEquals(hop, this))
                return true;
            hop = hop._owner;
        }
        return false;
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
