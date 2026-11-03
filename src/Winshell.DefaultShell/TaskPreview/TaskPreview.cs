using System.ComponentModel;
using Windows.Foundation;
using Windows.System;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>How big a preview is for a screen's work area. The width and height are the popup; zero means the name is measured.</summary>
public readonly record struct PreviewLayout(
    bool Listed,
    double Width,
    double Height,
    double CardWidth,
    double CardHeight,
    double ListWidth,
    double ViewportHeight);

/// <summary>
/// What hovering a taskbar button shows: a live preview of each of the app's windows, or only its name when it is
/// not running. The pointer rests before it opens, and it closes a moment after the pointer leaves both the button
/// and the preview. The keyboard opens it at once. It has no window of its own.
/// </summary>
public sealed class TaskPreview : ObservableObject, IDisposable
{
    /// <summary>How long the pointer rests on a button before the preview opens.</summary>
    public const int OpenDelayMs = 400;

    /// <summary>How long after the pointer leaves before the preview closes.</summary>
    public const int CloseDelayMs = 300;

    /// <summary>How long the pointer rests on one window before Windows peeks at it.</summary>
    public const int PeekDelayMs = 400;

    /// <summary>A row in the list of titles, in logical pixels.</summary>
    public const int RowHeight = 40;

    /// <summary>The gap between the preview and the button, in logical pixels.</summary>
    public const double Gap = 8;

    private const int CardMax = 216;
    private const int ListBelow = 152;

    private readonly Func<string, TaskItem?> _task;
    private readonly SynchronizationContext? _ui;
    private readonly ITimer _openTimer;
    private readonly ITimer _closeTimer;
    private readonly ITimer _peekTimer;
    private readonly List<INotifyPropertyChanged> _watched = [];
    private IReadOnlyList<ITaskWindow> _windows = [];
    private ITaskWindow? _hovered;
    private ITaskWindow? _peek;
    private string _key = "";
    private string _name = "";
    private bool _isOpen;
    private bool _grabFocus;
    private bool _running;
    private bool _listed;
    private bool _focusClose;
    private bool _onButton;
    private bool _inside;
    private bool _disposed;
    private int _focusIndex;
    private double _workWidth = 1920;
    private double _workHeight = 1032;
    private double _width;
    private double _height;
    private double _cardWidth;
    private double _cardHeight;
    private double _listWidth;
    private double _viewport;
    private double _scroll;

    public TaskPreview(Func<string, TaskItem?> task, TimeProvider time)
    {
        _task = task;
        _ui = SynchronizationContext.Current;
        _openTimer = time.CreateTimer(_ => OnUi(OnOpen), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _closeTimer = time.CreateTimer(_ => OnUi(OnClose), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _peekTimer = time.CreateTimer(_ => OnUi(OnPeek), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The preview is on screen.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>The keyboard opened it, so the pointer must not move it or close it.</summary>
    public bool GrabFocus => _grabFocus;

    /// <summary>The button it belongs to. Kept after it closes, so the keyboard can return there.</summary>
    public string TaskKey => _key;

    /// <summary>The app has at least one window.</summary>
    public bool Running => _running;

    /// <summary>The windows are titles in a list. A card would be under 152 pixels wide.</summary>
    public bool Listed => _listed;

    /// <summary>The app's name, shown when it is not running.</summary>
    public string Name => _name;

    /// <summary>The windows, in the order the taskbar has them.</summary>
    public IReadOnlyList<ITaskWindow> Windows => _windows;

    /// <summary>The window the keyboard is on.</summary>
    public int FocusIndex => _focusIndex;

    /// <summary>The keyboard is on that window's close button.</summary>
    public bool FocusClose => _focusClose;

    /// <summary>The window the keyboard is on, when there is one.</summary>
    public ITaskWindow? Focused => _running && _focusIndex >= 0 && _focusIndex < _windows.Count ? _windows[_focusIndex] : null;

    /// <summary>The window the pointer is on. Its close button is shown before the peek.</summary>
    public ITaskWindow? HoveredWindow => _hovered;

    /// <summary>The window Windows is peeking at, after the pointer has rested on it.</summary>
    public ITaskWindow? PeekWindow => _peek;

    /// <summary>How far down the list is scrolled, so the focused row is fully in view.</summary>
    public double ScrollY => _scroll;

    /// <summary>The popup's width. Zero when the name has to be measured.</summary>
    public double Width => _width;

    /// <summary>The popup's height. Zero when the name has to be measured.</summary>
    public double Height => _height;

    public double CardWidth => _cardWidth;

    public double CardHeight => _cardHeight;

    public double ListWidth => _listWidth;

    /// <summary>The list's visible height, inside the popup's padding.</summary>
    public double ViewportHeight => _viewport;

    /// <summary>Escape, or the last window of an app that is still pinned, closed it. The keyboard goes back to its button.</summary>
    public event EventHandler? Dismissed;

    /// <summary>The app went away while the keyboard was here. Its button is going too.</summary>
    public event EventHandler? AnchorGone;

    /// <summary>The pointer entered a button. An open preview moves at once; a closed one waits.</summary>
    public void HoverButton(string key)
    {
        if (_isOpen && _grabFocus)
            return;
        Stop(_closeTimer);
        _onButton = true;
        if (_isOpen)
        {
            if (!string.Equals(_key, key, StringComparison.Ordinal))
            {
                Use(key);
                Retarget();
            }
            return;
        }
        Use(key);
        Arm(_openTimer, OpenDelayMs);
    }

    /// <summary>The pointer left a button. Only the button the preview is for can close it.</summary>
    public void LeaveButton(string key)
    {
        if (_isOpen && _grabFocus)
            return;
        if (!string.Equals(_key, key, StringComparison.Ordinal))
            return;
        _onButton = false;
        Stop(_openTimer);
        if (_isOpen)
            Arm(_closeTimer, CloseDelayMs);
    }

    /// <summary>The pointer entered or left the preview itself.</summary>
    public void PointerInside(bool inside)
    {
        if (_grabFocus)
            return;
        _inside = inside;
        if (!_isOpen)
            return;
        if (inside)
            Stop(_closeTimer);
        else
            Arm(_closeTimer, CloseDelayMs);
    }

    /// <summary>The pointer entered a window's preview, or left them all when <paramref name="window"/> is null.</summary>
    public void PointerOn(ITaskWindow? window)
    {
        if (window is not null && _hovered is not null && Equals(window, _hovered))
            return;
        Set(ref _hovered, window, nameof(HoveredWindow));
        Stop(_peekTimer);
        Set(ref _peek, null, nameof(PeekWindow));
        if (window is not null && _isOpen)
            Arm(_peekTimer, PeekDelayMs);
    }

    /// <summary>Up on a running app. The preview opens now, on the first window, and takes the keyboard.</summary>
    public void OpenFromKeyboard(string key)
    {
        Stop(_openTimer);
        Stop(_closeTimer);
        ClearPeek();
        _onButton = false;
        Use(key);
        var task = _task(key);
        if (task is null || task.Windows.Count == 0)
        {
            if (_isOpen)
                Hide();
            return;
        }
        Set(ref _grabFocus, true, nameof(GrabFocus));
        Bind(task, previous: null, focusFirst: true, keepClose: false);
        Set(ref _isOpen, true, nameof(IsOpen));
    }

    /// <summary>Closes the preview. The button it was for is remembered, and a peek does not come back when it opens again.</summary>
    public void Hide()
    {
        Stop(_openTimer);
        Stop(_closeTimer);
        ClearPeek();
        _inside = false;
        _onButton = false;
        Set(ref _grabFocus, false, nameof(GrabFocus));
        Set(ref _isOpen, false, nameof(IsOpen));
    }

    /// <summary>The work area of the taskbar's screen, in logical pixels. An open preview is laid out again.</summary>
    public void SetWorkArea(double width, double height)
    {
        if (_workWidth == width && _workHeight == height)
            return;
        _workWidth = width;
        _workHeight = height;
        if (_isOpen)
            Relayout();
    }

    /// <summary>The taskbar finished rebuilding its buttons. Read once here, not on each button as it changes.</summary>
    public void Refresh()
    {
        if (!_isOpen || _key.Length == 0)
            return;
        var task = _task(_key);
        if (task is null)
        {
            var keyboard = _grabFocus;
            Hide();
            if (keyboard)
                AnchorGone?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_grabFocus && task.Windows.Count == 0)
        {
            Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }
        Bind(task, Focused, focusFirst: false, keepClose: _focusClose);
    }

    /// <summary>Brings a window forward and closes the preview. The keyboard stays with that window, so this is not <see cref="Dismissed"/>.</summary>
    public void Activate(ITaskWindow window)
    {
        if (window is not IOpenWindow open)
            return;
        Hide();
        open.Activate();
    }

    /// <summary>Asks a window to close. The preview stays until the window has left the taskbar.</summary>
    public void Close(ITaskWindow window)
    {
        if (window is IOpenWindow open)
            open.Close();
    }

    /// <summary>Enter: the focused window comes forward, or its close button closes it.</summary>
    public bool ActivateFocused()
    {
        if (!_isOpen || !_running || Focused is not { } window)
            return false;
        if (_focusClose)
            Close(window);
        else
            Activate(window);
        return true;
    }

    /// <summary>Delete closes the focused window.</summary>
    public bool PressDelete()
    {
        if (!_isOpen || !_running || Focused is not { } window)
            return false;
        Close(window);
        return true;
    }

    /// <summary>Left and Right move between cards and their close buttons. A list does not use them.</summary>
    public bool MoveHorizontal(int direction)
    {
        if (!_isOpen || !_running || _listed || direction == 0)
            return false;
        var count = _windows.Count * 2;
        var slot = _focusIndex * 2 + (_focusClose ? 1 : 0);
        var next = slot + (direction > 0 ? 1 : -1);
        if ((uint)next >= (uint)count)
            return true;
        Set(ref _focusIndex, next / 2, nameof(FocusIndex));
        Set(ref _focusClose, next % 2 == 1, nameof(FocusClose));
        return true;
    }

    /// <summary>Up and Down move between rows. The close button is skipped, except Up from it returns to its row.</summary>
    public bool MoveVertical(int direction)
    {
        if (!_isOpen || !_running || !_listed || direction == 0)
            return false;
        var step = direction > 0 ? 1 : -1;
        if (_focusClose)
        {
            if (step < 0)
            {
                Set(ref _focusClose, false, nameof(FocusClose));
                return true;
            }
            if (_focusIndex + 1 >= _windows.Count)
                return true;
            Set(ref _focusIndex, _focusIndex + 1, nameof(FocusIndex));
            Set(ref _focusClose, false, nameof(FocusClose));
            Reveal();
            return true;
        }
        var next = _focusIndex + step;
        if (next < 0 || next >= _windows.Count)
            return true;
        Set(ref _focusIndex, next, nameof(FocusIndex));
        Reveal();
        return true;
    }

    /// <summary>Tab reaches a row's close button, then the next row. On cards it moves the same way as Left and Right.</summary>
    public bool Tab(bool forward)
    {
        if (!_isOpen || !_running)
            return false;
        if (!_listed)
            return MoveHorizontal(forward ? 1 : -1);
        if (!_focusClose && forward)
        {
            Set(ref _focusClose, true, nameof(FocusClose));
            return true;
        }
        if (_focusClose && !forward)
        {
            Set(ref _focusClose, false, nameof(FocusClose));
            return true;
        }
        if (_focusClose && forward)
        {
            if (_focusIndex + 1 >= _windows.Count)
                return true;
            Set(ref _focusIndex, _focusIndex + 1, nameof(FocusIndex));
            Set(ref _focusClose, false, nameof(FocusClose));
            Reveal();
            return true;
        }
        if (_focusIndex == 0)
            return true;
        Set(ref _focusIndex, _focusIndex - 1, nameof(FocusIndex));
        Set(ref _focusClose, true, nameof(FocusClose));
        Reveal();
        return true;
    }

    /// <summary>
    /// A key pressed in the preview. True when the preview used it: Escape while it is open, the arrows, Tab (Shift+Tab
    /// goes back), Enter and Delete, each as its own method says.
    /// </summary>
    internal bool Press(VirtualKey key, bool shift)
    {
        if (key == VirtualKey.Escape)
        {
            if (!_isOpen)
                return false;
            Escape();
            return true;
        }
        return key switch
        {
            VirtualKey.Left => MoveHorizontal(-1),
            VirtualKey.Right => MoveHorizontal(1),
            VirtualKey.Up => MoveVertical(-1),
            VirtualKey.Down => MoveVertical(1),
            VirtualKey.Tab => Tab(!shift),
            VirtualKey.Enter => ActivateFocused(),
            VirtualKey.Delete => PressDelete(),
            _ => false,
        };
    }

    /// <summary>Escape closes a preview the keyboard opened and sends the keyboard back to its button.</summary>
    public void Escape()
    {
        if (!_isOpen)
            return;
        var keyboard = _grabFocus;
        Hide();
        if (keyboard)
            Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether this window's close button is shown: the pointer is on it, or the keyboard is.</summary>
    public bool ShowsClose(int index) =>
        index >= 0 && index < _windows.Count
        && ((_hovered is not null && Equals(_hovered, _windows[index])) || (_grabFocus && _focusIndex == index));

    /// <summary>What Narrator reads for a window's close button.</summary>
    public static string CloseName(string title) => "Close " + title;

    /// <summary>
    /// Where the popup sits: centered on the button, <see cref="Gap"/> pixels above it, kept inside the work area.
    /// </summary>
    public static Rect Place(Rect button, double width, double height, Rect work)
    {
        var x = button.X + button.Width / 2 - width / 2;
        var y = button.Top - Gap - height;
        if (work.Width > 0)
            x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        if (work.Height > 0)
            y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// The popup for <paramref name="count"/> windows in a work area. Cards shrink to fit and keep their shape.
    /// Below 152 pixels they become a list, as wide as the screen allows up to 320.
    /// </summary>
    public static PreviewLayout Measure(int count, double workWidth, double workHeight)
    {
        if (count <= 0)
            return default;
        var roomWide = (workWidth > 0 ? workWidth : 1920) - 32;
        var roomHigh = (workHeight > 0 ? workHeight : 1032) * 0.6;
        var cardWidth = Math.Min(CardMax, Math.Floor((roomWide - 16 - Math.Max(0, count - 1) * 4) / Math.Max(1, count)));
        var cardHeight = Math.Round(cardWidth * 160 / 216);
        if (cardWidth < ListBelow)
        {
            var listWidth = Math.Max(96, Math.Min(320, roomWide - 16));
            var viewport = Math.Min(count * (double)RowHeight, roomHigh);
            return new PreviewLayout(true, listWidth + 16, viewport + 16, cardWidth, cardHeight, listWidth, viewport);
        }
        var width = cardWidth * count + Math.Max(0, count - 1) * 4 + 16;
        return new PreviewLayout(false, width, cardHeight + 16, cardWidth, cardHeight, 0, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _openTimer.Dispose();
        _closeTimer.Dispose();
        _peekTimer.Dispose();
        Unwatch();
    }

    private void OnOpen()
    {
        if (!_onButton || _grabFocus || _isOpen)
            return;
        var task = _task(_key);
        if (task is null)
            return;
        Stop(_openTimer);
        Set(ref _grabFocus, false, nameof(GrabFocus));
        Bind(task, previous: null, focusFirst: true, keepClose: false);
        Set(ref _isOpen, true, nameof(IsOpen));
    }

    private void OnClose()
    {
        if (_inside || _onButton || _grabFocus || !_isOpen)
            return;
        Hide();
    }

    private void OnPeek()
    {
        if (_hovered is null || !_isOpen)
            return;
        Set(ref _peek, _hovered, nameof(PeekWindow));
    }

    private void Retarget()
    {
        var task = _task(_key);
        if (task is null)
        {
            Hide();
            return;
        }
        ClearPeek();
        Bind(task, previous: null, focusFirst: false, keepClose: false);
    }

    private void Bind(TaskItem task, ITaskWindow? previous, bool focusFirst, bool keepClose)
    {
        Watch(task);
        var windows = task.Windows.ToArray();
        var wasListed = _listed;
        Set(ref _running, windows.Length > 0, nameof(Running));
        Set(ref _name, Tasks.Name(task), nameof(Name));
        ReplaceWindows(windows);
        Apply(Measure(windows.Length, _workWidth, _workHeight));
        if (windows.Length == 0)
        {
            Set(ref _focusIndex, 0, nameof(FocusIndex));
            Set(ref _focusClose, false, nameof(FocusClose));
            Set(ref _scroll, 0, nameof(ScrollY));
            return;
        }
        var index = 0;
        var same = false;
        if (!focusFirst && previous is not null)
        {
            var found = IndexOf(windows, previous);
            if (found >= 0)
            {
                index = found;
                same = true;
            }
        }
        Set(ref _focusIndex, index, nameof(FocusIndex));
        Set(ref _focusClose, same && keepClose && wasListed == _listed, nameof(FocusClose));
        Reveal();
    }

    private void Relayout()
    {
        var was = _listed;
        Apply(Measure(_windows.Count, _workWidth, _workHeight));
        if (was != _listed)
            Set(ref _focusClose, false, nameof(FocusClose));
        if (_running)
            Reveal();
    }

    private void Apply(PreviewLayout layout)
    {
        Set(ref _listed, layout.Listed, nameof(Listed));
        Set(ref _width, layout.Width, nameof(Width));
        Set(ref _height, layout.Height, nameof(Height));
        Set(ref _cardWidth, layout.CardWidth, nameof(CardWidth));
        Set(ref _cardHeight, layout.CardHeight, nameof(CardHeight));
        Set(ref _listWidth, layout.ListWidth, nameof(ListWidth));
        Set(ref _viewport, layout.ViewportHeight, nameof(ViewportHeight));
    }

    private void Reveal()
    {
        if (!_listed)
        {
            Set(ref _scroll, 0, nameof(ScrollY));
            return;
        }
        var top = _focusIndex * (double)RowHeight;
        var scroll = _scroll;
        if (top < scroll)
            scroll = top;
        else if (top + RowHeight > scroll + _viewport)
            scroll = top + RowHeight - _viewport;
        Set(ref _scroll, scroll, nameof(ScrollY));
    }

    private void ReplaceWindows(IReadOnlyList<ITaskWindow> next)
    {
        if (Same(_windows, next))
            return;
        _windows = next;
        Changed(nameof(Windows));
    }

    private static bool Same(IReadOnlyList<ITaskWindow> current, IReadOnlyList<ITaskWindow> next)
    {
        if (current.Count != next.Count)
            return false;
        for (var i = 0; i < current.Count; i++)
        {
            if (!Equals(current[i], next[i]))
                return false;
        }
        return true;
    }

    private static int IndexOf(IReadOnlyList<ITaskWindow> windows, ITaskWindow window)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            if (Equals(windows[i], window))
                return i;
        }
        return -1;
    }

    private void Use(string key)
    {
        if (_key == key)
            return;
        _key = key;
        Changed(nameof(TaskKey));
    }

    private void ClearPeek()
    {
        Stop(_peekTimer);
        Set(ref _hovered, null, nameof(HoveredWindow));
        Set(ref _peek, null, nameof(PeekWindow));
    }

    private void Watch(TaskItem task)
    {
        Unwatch();
        foreach (var window in task.Windows)
        {
            if (window is not INotifyPropertyChanged source)
                continue;
            source.PropertyChanged += OnWindowProperty;
            _watched.Add(source);
        }
    }

    private void Unwatch()
    {
        foreach (var source in _watched)
            source.PropertyChanged -= OnWindowProperty;
        _watched.Clear();
    }

    private void OnWindowProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ITaskWindow.Title) or nameof(ITaskWindow.Icon))
            Changed(nameof(Windows));
    }

    private void OnUi(Action action)
    {
        if (_disposed)
            return;
        if (_ui is null || SynchronizationContext.Current == _ui)
            action();
        else
            _ui.Post(_ =>
            {
                if (!_disposed)
                    action();
            }, null);
    }

    private static void Arm(ITimer timer, int milliseconds) =>
        timer.Change(TimeSpan.FromMilliseconds(milliseconds), Timeout.InfiniteTimeSpan);

    private static void Stop(ITimer timer) => timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
}
