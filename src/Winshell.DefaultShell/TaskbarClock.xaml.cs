using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The time and the date on the right of the taskbar. Nothing opens from it.</summary>
public sealed partial class TaskbarClock : UserControl
{
    private TaskbarClockModel? _model;
    private Preferences? _preferences;
    private TimeProvider? _time;
    private DispatcherTimer? _timer;

    public TaskbarClock() => InitializeComponent();

    public string Text { get; private set; } = "";

    public string AccessibleName { get; private set; } = "";

    public void Attach(Preferences preferences, TimeProvider time)
    {
        Detach();
        _preferences = preferences;
        _time = time;
        _model = new TaskbarClockModel(time, preferences);
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => Show();
        preferences.PropertyChanged += OnPreferences;
        Show();
    }

    public void Detach()
    {
        if (_preferences is not null)
            _preferences.PropertyChanged -= OnPreferences;
        _preferences = null;
        _time = null;
        _model = null;
        if (_timer is null)
            return;
        _timer.Stop();
        _timer = null;
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.ClockSeconds))
            Show();
    }

    private void Show()
    {
        if (_model is null || _timer is null || _time is null || _preferences is null)
            return;
        _model.Refresh();
        Text = _model.Text;
        AccessibleName = _model.AccessibleName;
        Bindings.Update();
        _timer.Stop();
        _timer.Interval = TaskbarClockModel.UntilNext(_time.GetLocalNow(), _preferences.ClockSeconds);
        _timer.Start();
    }
}
