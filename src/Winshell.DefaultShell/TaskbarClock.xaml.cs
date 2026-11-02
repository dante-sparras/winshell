using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The time and the date on the right of the taskbar. Nothing opens from it.</summary>
public sealed partial class TaskbarClock : UserControl
{
    private (TaskbarClockModel Model, Preferences Preferences, TimeProvider Time, DispatcherTimer Timer)? _run;

    public TaskbarClock() => InitializeComponent();

    public void Attach(Preferences preferences, TimeProvider time)
    {
        Detach();
        var model = new TaskbarClockModel(time, preferences);
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => Show();
        _run = (model, preferences, time, timer);
        preferences.PropertyChanged += OnPreferences;
        Show();
    }

    public void Detach()
    {
        if (_run is not { } run)
            return;
        run.Preferences.PropertyChanged -= OnPreferences;
        run.Timer.Stop();
        _run = null;
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.ClockSeconds))
            Show();
    }

    private void Show()
    {
        if (_run is not { } run)
            return;
        run.Model.Refresh();
        Readout.Text = run.Model.Text;
        AutomationProperties.SetName(Readout, run.Model.AccessibleName);
        run.Timer.Stop();
        run.Timer.Interval = TaskbarClockModel.UntilNext(run.Time.GetLocalNow(), run.Preferences.ClockSeconds);
        run.Timer.Start();
    }
}
