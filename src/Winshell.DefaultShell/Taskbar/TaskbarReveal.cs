namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// Whether an auto-hiding taskbar is up. The pointer brings it back at once. It stays while something it opened is
/// open, and hides a second after the pointer has left and nothing is open. Turning auto-hide on hides it at once
/// and reserves nothing; turning it off reserves the edge again.
/// </summary>
public sealed class TaskbarReveal : ObservableObject, IDisposable
{
    /// <summary>How long the pointer must stay away, with nothing open, before the bar hides.</summary>
    public const int HideDelayMs = 1000;

    /// <summary>The taskbar's height, in logical pixels.</summary>
    public const double ShownHeight = 48;

    /// <summary>The line left at the bottom of the screen, so the pointer can still reach the bar.</summary>
    public const double HiddenHeight = 2;

    private readonly SynchronizationContext? _ui;
    private readonly ITimer _hide;
    private bool _autoHide;
    private bool _pointer;
    private bool _busy;
    private bool _keyboard;
    private bool _revealed = true;
    private bool _disposed;

    public TaskbarReveal(TimeProvider time)
    {
        _ui = SynchronizationContext.Current;
        _hide = time.CreateTimer(_ => OnUi(OnHide), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The window's height. Hidden, it is the two-pixel line.</summary>
    public double WindowHeight => _autoHide && !_revealed ? HiddenHeight : ShownHeight;

    /// <summary>False while auto-hide is on, so a hidden bar leaves no zone reserved. Turning it off reserves the edge.</summary>
    public bool ReservesEdge => !_autoHide;

    /// <summary>The preference changed. The bar hides or comes back at once, without waiting out the second.</summary>
    public void SetAutoHide(bool autoHide)
    {
        if (_autoHide == autoHide)
            return;
        _autoHide = autoHide;
        Stop();
        Changed(nameof(ReservesEdge));
        SetRevealed(!autoHide);
        Changed(nameof(WindowHeight));
    }

    /// <summary>The pointer entered or left the bar. Entering brings it back. Leaving starts the second.</summary>
    public void SetPointer(bool over)
    {
        if (_disposed || _pointer == over)
            return;
        _pointer = over;
        if (over)
        {
            Stop();
            SetRevealed(true);
        }
        else
            Arm();
    }

    /// <summary>
    /// What the bar has open. Any of it keeps an auto-hiding bar up. Keyboard mode also brings the bar back, which
    /// is what Win+T does. Search is part of Start.
    /// </summary>
    public void SetOpen(TaskbarSurfaces open)
    {
        if (_disposed)
            return;
        var keyboardBegan = open.Keyboard && !_keyboard;
        _keyboard = open.Keyboard;
        if (keyboardBegan)
            SetRevealed(true);
        var busy = open.Busy;
        if (_busy == busy)
            return;
        _busy = busy;
        // Closing the last thing starts the second again. Opening one does not, on its own, bring the bar back.
        if (!busy)
            Arm();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _hide.Dispose();
    }

    private void OnHide()
    {
        if (_autoHide && !_pointer && !_busy)
            SetRevealed(false);
    }

    private void SetRevealed(bool value)
    {
        Set(ref _revealed, value, nameof(WindowHeight));
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

    private void Arm()
    {
        if (!_autoHide)
            return;
        _hide.Change(TimeSpan.FromMilliseconds(HideDelayMs), Timeout.InfiniteTimeSpan);
    }

    private void Stop() => _hide.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
}

/// <summary>What one taskbar has open. Any of it keeps an auto-hiding bar from leaving.</summary>
public readonly record struct TaskbarSurfaces(
    bool Start = false,
    bool QuickSettings = false,
    bool Calendar = false,
    bool Preview = false,
    bool TaskMenu = false,
    bool Tray = false,
    bool BarMenu = false,
    bool Keyboard = false)
{
    /// <summary>The notification center is the calendar. The hidden icons are the tray. A task menu is an app's menu.</summary>
    public bool Busy => Start || QuickSettings || Calendar || Preview || TaskMenu || Tray || BarMenu || Keyboard;
}
