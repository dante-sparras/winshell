using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>Quick settings above one taskbar's button. The tiles are shared; this window is not.</summary>
internal sealed class QuickSettingsFlyout
{
    private readonly PanelWindow _bar;
    private readonly Button _anchor;
    private readonly Preferences _preferences;
    private readonly TimeProvider _time;
    private readonly QuickSettingsPopup _quick;
    private readonly QuickSettingsView _view;
    private readonly PopupWindow _popup;
    private DateTimeOffset _dismissed;

    public QuickSettingsFlyout(PanelWindow bar, Button anchor, QuickSettings settings, Preferences preferences, TimeProvider time)
    {
        _bar = bar;
        _anchor = anchor;
        _preferences = preferences;
        _time = time;
        _quick = settings.CreatePopup();
        _view = new QuickSettingsView(_quick);
        _popup = new PopupWindow { GrabFocus = true, Corners = WindowCorners.Round, Content = _view };
        _popup.Dismissed += OnDismissed;
        _quick.PropertyChanged += OnQuick;
        _view.ContentSized += OnContentSized;
        _anchor.Click += OnClick;
    }

    /// <summary>Drops the popup. The bar is about to close, and quick settings stays.</summary>
    public void Release()
    {
        _anchor.Click -= OnClick;
        _view.ContentSized -= OnContentSized;
        _quick.PropertyChanged -= OnQuick;
        _quick.Hide();
        _view.Release();
        _popup.Dismissed -= OnDismissed;
        _popup.Dispose();
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (_popup.Visible)
            Close();
        else if (!TaskbarClockModel.ClosedBySameClick(_dismissed, _time.GetUtcNow()))
            Open();
    }

    private void Open()
    {
        if (_bar.Screen is not { } screen)
            return;
        _popup.Screen = screen;
        Place();
        _popup.Backdrop = _preferences.Material;
        _quick.Show();
        _popup.Visible = true;
        _view.Focus(FocusState.Programmatic);
    }

    private void Close() => _quick.Hide();

    private void OnDismissed()
    {
        _dismissed = _time.GetUtcNow();
        _quick.Hide();
    }

    private void OnQuick(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSettingsPopup.IsOpen) && !_quick.IsOpen)
            _popup.Visible = false;
    }

    private void OnContentSized(object sender, SizeChangedEventArgs e)
    {
        if (_quick.IsOpen)
            Place();
    }

    /// <summary>Above the quick settings button, lined up with its right edge.</summary>
    private void Place()
    {
        var point = _anchor.TransformToVisual(null).TransformPoint(default);
        var origin = _bar.Origin;
        var button = new QRect(origin.X + point.X, origin.Y + point.Y, _anchor.ActualWidth, _anchor.ActualHeight);
        var height = _view.ContentHeight;
        if (height <= 0)
        {
            // The content has no height before it is first shown. This is a first guess; ContentSized corrects it.
            _view.Measure(new Size(QuickSettingsPlacement.Width, double.PositiveInfinity));
            height = _view.DesiredSize.Height;
        }
        _popup.Rect = QuickSettingsPlacement.Above(button, height);
    }
}
