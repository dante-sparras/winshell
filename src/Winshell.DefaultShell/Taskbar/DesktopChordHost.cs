using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
using Winshell.Apps;
using Winshell.Windows;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The switcher and the Win+X menu above the main taskbar. The chords live on <see cref="DesktopChordSession"/>.
/// This draws them, and only while that preference is on: the shell does not construct it otherwise.
/// </summary>
internal sealed class DesktopChordHost : IDisposable, IDesktopCommandTarget
{
    private readonly DesktopSwitcher _switcher;
    private readonly WindowListService _windows;
    private readonly VirtualDesktopsService _desktops;
    private readonly Preferences _preferences;
    private readonly Func<PanelWindow?> _bar;
    private readonly Func<TaskbarView?> _view;
    private readonly AppsService _apps;
    private readonly Log _log;
    private readonly Action _openSettings;
    private readonly SwitcherView _switcherView;
    private readonly PowerMenuView _powerView;
    private readonly PopupWindow _switcherPopup;
    private readonly PopupWindow _powerPopup;
    private bool _disposed;

    public DesktopChordHost(
        Shell shell,
        DesktopSwitcher switcher,
        Preferences preferences,
        Func<PanelWindow?> bar,
        Func<TaskbarView?> view,
        Action openSettings)
    {
        _switcher = switcher;
        _windows = shell.WindowList;
        _desktops = shell.VirtualDesktops;
        _preferences = preferences;
        _bar = bar;
        _view = view;
        _apps = shell.Apps;
        _log = shell.Log;
        _openSettings = openSettings;
        _switcherView = new SwitcherView(switcher, _windows, _desktops);
        _powerView = new PowerMenuView(Run);
        _switcherPopup = new PopupWindow { GrabFocus = true, Corners = WindowCorners.Round, Content = _switcherView };
        _powerPopup = new PopupWindow { GrabFocus = true, Corners = WindowCorners.Round, Content = _powerView };
        _switcher.Changed += OnChanged;
        _switcherPopup.Dismissed += OnSwitcherDismissed;
        _powerPopup.Dismissed += OnPowerDismissed;
        _switcherView.LayoutChanged += OnLayout;
        _preferences.PropertyChanged += OnPreferences;
    }

    /// <summary>The main taskbar moved. An open switcher or menu follows it.</summary>
    public void Retarget()
    {
        if (_switcher.SwitcherOpen || _switcher.PowerOpen)
            Apply();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _switcher.Changed -= OnChanged;
        _switcherPopup.Dismissed -= OnSwitcherDismissed;
        _powerPopup.Dismissed -= OnPowerDismissed;
        _switcherView.LayoutChanged -= OnLayout;
        _preferences.PropertyChanged -= OnPreferences;
        _switcherView.Release();
        _switcherPopup.Dispose();
        _powerPopup.Dispose();
    }

    public void OpenUrl(string url) => ProgramStart.Start(url, _log);

    public void OpenExplorer()
    {
        var app = _apps.Find("Microsoft.Windows.Explorer") ?? _apps.Find("File Explorer");
        app?.Launch();
    }

    public void OpenSearch() => _view()?.OpenSearch();

    public void OpenSettings() => _openSettings();

    private void Run(PowerCommand command) => _switcher.Run(command, this, Windows());

    private void OnChanged(object? sender, EventArgs e) => Apply();

    private void OnSwitcherDismissed() => _switcher.CloseSwitcher();

    private void OnPowerDismissed() => _switcher.ClosePower();

    private void OnLayout(object? sender, EventArgs e)
    {
        if (_switcher.SwitcherOpen)
            PlaceSwitcher();
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Preferences.Backdrop))
            return;
        _switcherPopup.Backdrop = _preferences.Material;
        _powerPopup.Backdrop = _preferences.Material;
    }

    private void Apply()
    {
        if (_disposed)
            return;
        if (_switcher.SwitcherOpen && TryBar(out var bar, out var screenWidth))
        {
            var desktops = DesktopChordLayout.ShowDesktops(_desktops.Supported, _desktops.Desktops.Count);
            _switcherView.Refresh();
            Show(_switcherPopup, DesktopChordLayout.Switcher(bar, screenWidth, Windows().Count, desktops));
            _switcherPopup.Focus();
            _switcherView.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, _switcherView.FocusChosen);
        }
        else
            _switcherPopup.Visible = false;

        if (_switcher.PowerOpen && TryBar(out var powerBar, out _))
        {
            Show(_powerPopup, DesktopChordLayout.Power(powerBar, DesktopSwitcher.Commands.Count));
            _powerPopup.Focus();
            _powerView.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, _powerView.FocusFirst);
        }
        else
            _powerPopup.Visible = false;
    }

    private void PlaceSwitcher()
    {
        if (_disposed || !_switcher.SwitcherOpen || !TryBar(out var bar, out var screenWidth))
        {
            if (!_disposed)
                _switcherPopup.Visible = false;
            return;
        }
        var desktops = DesktopChordLayout.ShowDesktops(_desktops.Supported, _desktops.Desktops.Count);
        Show(_switcherPopup, DesktopChordLayout.Switcher(bar, screenWidth, Windows().Count, desktops));
    }

    private void Show(PopupWindow popup, Rect rect)
    {
        if (_bar()?.Screen is { } screen)
            popup.Screen = screen;
        popup.Backdrop = _preferences.Material;
        popup.Rect = rect;
        popup.Visible = true;
    }

    private bool TryBar(out Rect bar, out double screenWidth)
    {
        var window = _bar();
        var view = _view();
        if (window?.Screen is not { } screen || view is null)
        {
            bar = default;
            screenWidth = 0;
            return false;
        }
        var width = view.ActualWidth > 1 ? view.ActualWidth : screen.Geometry.Width;
        var height = view.ActualHeight > 1 ? view.ActualHeight : 48;
        bar = new Rect(window.Origin.X, window.Origin.Y, width, height);
        screenWidth = screen.Geometry.Width;
        return true;
    }

    private List<IOpenWindow> Windows() => [.. _windows.Windows.Select(static window => (IOpenWindow)new TaskWindow(window))];
}
