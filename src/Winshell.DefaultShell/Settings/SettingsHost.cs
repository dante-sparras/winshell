using System.ComponentModel;
using Windows.Foundation;
using Winshell.DefaultShell.Taskbar;
using Winshell.Input;

namespace Winshell.DefaultShell;

/// <summary>
/// Winshell Settings. The hotkey and the <c>settings</c> IPC target exist as soon as the shell starts. The window is
/// built the first time it opens and kept after that, so a resting shell pays nothing for it. Closing hides it.
/// </summary>
internal sealed class SettingsHost
{
    private readonly Shell _shell;
    private readonly Preferences _preferences;
    private readonly Start _start;
    private readonly QuickSettingsLayout _layout;
    private readonly Hotkey _startHotkey;
    private readonly Hotkey _hotkey;
    private readonly IDisposable _ipc;
    private FloatingWindow? _window;
    private SettingsView? _view;

    public SettingsHost(Shell shell, Preferences preferences, Start start, QuickSettingsLayout layout, Hotkey startHotkey)
    {
        _shell = shell;
        _preferences = preferences;
        _start = start;
        _layout = layout;
        _startHotkey = startHotkey;
        _hotkey = shell.NewHotkey();
        _hotkey.Pressed += (_, _) => Open("");
        _hotkey.Sequence = preferences.SettingsHotkey;
        preferences.PropertyChanged += OnPreferences;
        _ipc = shell.Ipc.Handle("settings", "open", OpenPage);
    }

    /// <summary>Opens a page, or the page of a setting. An empty page keeps the one already shown and takes the search box.</summary>
    public void Open(string? page)
    {
        Ensure();
        var showing = _window!.Visible;
        var found = string.IsNullOrEmpty(page) || _view!.Open(page!);
        _window.Visible = true;
        _window.Focus();
        if (showing)
            _view!.RefreshStartup();
        if (string.IsNullOrEmpty(page))
            _view!.FocusSearch();
        else if (found)
            _view!.FocusPending();
    }

    private void OpenPage(string page) => Open(page);

    private void Ensure()
    {
        if (_window is not null)
            return;
        _view = new SettingsView(_preferences, _shell, _start, _layout, _startHotkey, _hotkey);
        _window = new FloatingWindow
        {
            Title = "Winshell Settings",
            Width = SettingsCatalog.WindowWidth,
            Height = SettingsCatalog.WindowHeight,
            MinimumSize = new Size(SettingsCatalog.MinimumWidth, SettingsCatalog.MinimumHeight),
            Backdrop = WindowBackdrop.Mica,
            Content = _view,
        };
        _window.PropertyChanged += OnWindowChanged;
    }

    private void OnWindowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_view is null || _window is null)
            return;
        if (e.PropertyName == nameof(ShellWindow.Visible) && _window.Visible)
            _view.RefreshStartup();
        else if (e.PropertyName == nameof(ShellWindow.Active) && _window.Active)
            _view.RefreshStartup();
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.SettingsHotkey))
            _hotkey.Sequence = _preferences.SettingsHotkey;
    }
}
