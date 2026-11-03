using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Power;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>The brightness slider. It follows and sets the built-in panel only, and a PC without one has no slider.</summary>
public sealed class QuickSettingsBrightness : ObservableObject
{
    private readonly BrightnessService _brightness;
    private BrightnessMonitor? _panel;

    internal QuickSettingsBrightness(BrightnessService brightness)
    {
        _brightness = brightness;
        brightness.Monitors.CollectionChanged += OnMonitors;
        Bind();
    }

    /// <summary>Where the panel is in its own range, from 0 to 1.</summary>
    public double Level { get; private set => Set(ref field, value); }

    public bool HasPanel { get; private set => Set(ref field, value); }

    public void SetLevel(double value)
    {
        if (_panel is null)
            return;
        var next = (int)Math.Round(_panel.Minimum + value * (_panel.Maximum - _panel.Minimum), MidpointRounding.AwayFromZero);
        if (next == _panel.Brightness)
            return;
        _panel.Brightness = next;
    }

    private void OnMonitors(object? sender, NotifyCollectionChangedEventArgs e) => Bind();

    private void OnPanel(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrightnessMonitor.Brightness) or nameof(BrightnessMonitor.Minimum) or nameof(BrightnessMonitor.Maximum) or nameof(BrightnessMonitor.Connection))
            Publish();
    }

    private void Bind()
    {
        var panel = _brightness.Monitors.FirstOrDefault(monitor => monitor.Connection == BrightnessConnection.Internal);
        if (!ReferenceEquals(panel, _panel))
        {
            if (_panel is not null)
                _panel.PropertyChanged -= OnPanel;
            _panel = panel;
            if (_panel is not null)
                _panel.PropertyChanged += OnPanel;
        }
        Publish();
    }

    private void Publish()
    {
        HasPanel = _panel is not null;
        if (_panel is null)
        {
            Level = 0;
            return;
        }
        Level = (_panel.Brightness - _panel.Minimum) / (double)Math.Max(1, _panel.Maximum - _panel.Minimum);
    }
}
