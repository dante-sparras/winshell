using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Winshell;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// The preview above one taskbar button. Hovering does not take the keyboard. Up on a running app does, and the
/// window is focused, because showing it is not enough. The taskbar's keyboard mode keeps Escape on a button that
/// is still there: when the app goes away, focus lands on one, or on Start.
/// </summary>
internal sealed class TaskPreviewFlyout
{
    private readonly PanelWindow _bar;
    private readonly Preferences _preferences;
    private readonly Func<string, Button?> _button;
    private readonly Func<Button?> _anyButton;
    private readonly Button _start;
    private readonly TaskPreview _model;
    private readonly TaskPreviewView _view;
    private readonly PopupWindow _popup;
    private readonly TaskbarModel _taskbar;

    public TaskPreviewFlyout(
        PanelWindow bar,
        TaskbarModel model,
        Preferences preferences,
        TimeProvider time,
        Func<string, Button?> button,
        Func<Button?> anyButton,
        Button start)
    {
        _bar = bar;
        _preferences = preferences;
        _button = button;
        _anyButton = anyButton;
        _start = start;
        _model = new TaskPreview(key => Find(model, key), time);
        _view = new TaskPreviewView(_model);
        _popup = new PopupWindow { Corners = WindowCorners.Round, Content = _view };
        _model.PropertyChanged += OnModel;
        _model.Dismissed += OnDismissed;
        _model.AnchorGone += OnAnchorGone;
        model.Updated += OnUpdated;
        _bar.PropertyChanged += OnBar;
        _preferences.PropertyChanged += OnPreferences;
        _popup.Dismissed += OnWindowDismissed;
        _taskbar = model;
    }

    public TaskPreview Model => _model;

    /// <summary>Drops the window. The taskbar is about to close.</summary>
    public void Release()
    {
        _taskbar.Updated -= OnUpdated;
        _bar.PropertyChanged -= OnBar;
        _preferences.PropertyChanged -= OnPreferences;
        _model.PropertyChanged -= OnModel;
        _model.Dismissed -= OnDismissed;
        _model.AnchorGone -= OnAnchorGone;
        _view.Release();
        _popup.Dismissed -= OnWindowDismissed;
        _popup.Dispose();
        _model.Dispose();
    }

    private void OnUpdated(object? sender, EventArgs e) => _model.Refresh();

    private void OnModel(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskPreview.IsOpen))
        {
            if (_model.IsOpen)
                Show();
            else
                _popup.Visible = false;
            return;
        }
        if (!_model.IsOpen)
            return;
        if (e.PropertyName is nameof(TaskPreview.Width) or nameof(TaskPreview.Height) or nameof(TaskPreview.Listed))
            Place();
        else if (e.PropertyName == nameof(TaskPreview.GrabFocus))
            TakeKeyboard();
    }

    private void OnDismissed(object? sender, EventArgs e) => Focus(_button(_model.TaskKey) ?? _start);

    private void OnAnchorGone(object? sender, EventArgs e)
    {
        var queue = _view.DispatcherQueue;
        if (queue is null)
            Focus(_anyButton() ?? _start);
        else
            queue.TryEnqueue(() => Focus(_anyButton() ?? _start));
    }

    private void OnWindowDismissed()
    {
        var keyboard = _model.GrabFocus;
        var key = _model.TaskKey;
        _model.Hide();
        if (keyboard)
            Focus(_button(key) ?? _start);
    }

    private void OnBar(object? sender, PropertyChangedEventArgs e)
    {
        if (_model.IsOpen && e.PropertyName is nameof(ShellWindow.Origin) or nameof(ShellWindow.ActualWidth) or nameof(ShellWindow.ActualHeight) or nameof(ShellWindow.Screen))
            Place();
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.Backdrop) && _popup.Visible)
            _popup.Backdrop = _preferences.Material;
    }

    private void Show()
    {
        if (_bar.Screen is null || _button(_model.TaskKey) is null)
            return;
        _popup.Screen = _bar.Screen;
        _popup.GrabFocus = _model.GrabFocus;
        _popup.Backdrop = _preferences.Material;
        Place();
        if (!_popup.Visible)
            _popup.Visible = true;
        TakeKeyboard();
    }

    private void TakeKeyboard()
    {
        if (!_model.IsOpen || !_model.GrabFocus)
            return;
        _popup.GrabFocus = true;
        _popup.Focus();
        _view.ApplyFocus();
    }

    /// <summary>Centered on the button, one gap above it, kept inside the work area. The size is the content's.</summary>
    private void Place()
    {
        if (_bar.Screen is not { } screen || _button(_model.TaskKey) is not { } button)
            return;
        var work = screen.WorkArea;
        _model.SetWorkArea(work.Width, work.Height);
        var point = button.TransformToVisual(null).TransformPoint(default);
        var origin = _bar.Origin;
        var width = button.ActualWidth > 0 ? button.ActualWidth : button.Width;
        var height = button.ActualHeight > 0 ? button.ActualHeight : button.Height;
        var anchor = new Rect(origin.X + point.X, origin.Y + point.Y, width, height);
        var size = _view.SizeForPopup();
        _popup.Rect = TaskPreview.Place(anchor, size.Width, size.Height, work);
    }

    private void Focus(Button button)
    {
        _bar.Focus();
        button.Focus(FocusState.Programmatic);
    }

    private static TaskItem? Find(TaskbarModel model, string key)
    {
        foreach (var task in model.Tasks)
        {
            if (task.Key == key)
                return task;
        }
        return null;
    }
}
