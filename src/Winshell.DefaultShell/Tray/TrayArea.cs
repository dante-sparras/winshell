using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Tray;
using Winshell.Updates;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The icons on the right of every taskbar: the hidden-icon chevron, the icons Settings shows on the bar, a ready
/// update, and quick settings. One instance is shared. Reading the tray makes this process the notification area,
/// so a bar that opens or closes does not read it again.
/// </summary>
public sealed class TrayArea : ObservableObject
{
    private readonly SystemTray _tray;
    private readonly UpdatesService _updates;
    private bool _hasOverflow;
    private bool _updateReady;
    private string _updateLabel = "";

    public TrayArea(SystemTray tray, SystemStatus status, UpdatesService updates, WinlandMenu? winland = null)
    {
        _tray = tray;
        Status = status;
        Winland = winland;
        _updates = updates;
        tray.Items.CollectionChanged += OnItems;
        foreach (var item in tray.Items)
        {
            item.PropertyChanged += OnItem;
            winland?.Notice(item);
        }
        updates.PropertyChanged += OnUpdates;
        Place();
        PublishUpdate();
    }

    public SystemStatus Status { get; }

    /// <summary>The menu Winland's icon opens here, or null when a click on it goes to Winland.</summary>
    public WinlandMenu? Winland { get; }

    /// <summary>Icons Settings shows on the taskbar, in the order the apps added them.</summary>
    public ObservableCollection<SystemTrayItem> OnBar { get; } = [];

    /// <summary>Icons that open from the chevron, in the same order.</summary>
    public ObservableCollection<SystemTrayItem> Hidden { get; } = [];

    public bool HasOverflow
    {
        get => _hasOverflow;
        private set => Set(ref _hasOverflow, value);
    }

    public bool UpdateReady
    {
        get => _updateReady;
        private set => Set(ref _updateReady, value);
    }

    public string UpdateLabel
    {
        get => _updateLabel;
        private set => Set(ref _updateLabel, value);
    }

    /// <summary>The network, volume and battery icons were clicked.</summary>
    public event EventHandler? QuickSettingsRequested;

    /// <summary>The update icon was clicked. Nothing opens.</summary>
    public event EventHandler? UpdatesRequested;

    public void RequestQuickSettings() => QuickSettingsRequested?.Invoke(this, EventArgs.Empty);

    public void RequestUpdates() => UpdatesRequested?.Invoke(this, EventArgs.Empty);

    public static bool ShowsUpdate(UpdateStatus status) => status == UpdateStatus.Ready;

    public static string UpdateCaption(string version) =>
        "Winshell " + version + " is ready. Restart Winshell to update";

    /// <summary>The chevron while the hidden icons are open, and while they are closed.</summary>
    public static string ChevronGlyph(bool open) => open ? "\uE70D" : "\uE70E";

    /// <summary>The icon's accessible name: the first line of its tooltip, or the app name when it has none.</summary>
    public static string IconName(string tooltip, string appName)
    {
        var text = tooltip.Length > 0 ? tooltip : appName;
        var line = text.IndexOf('\n');
        return line < 0 ? text : text[..line];
    }

    /// <summary>The icon's tooltip. The whole text, over several lines when the app wrote it that way.</summary>
    public static string IconTip(string tooltip, string appName) =>
        tooltip.Length > 0 ? tooltip : appName;

    private void OnItems(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.NewItems?.OfType<SystemTrayItem>() ?? [])
        {
            item.PropertyChanged += OnItem;
            Winland?.Notice(item);
        }
        foreach (var item in e.OldItems?.OfType<SystemTrayItem>() ?? [])
            item.PropertyChanged -= OnItem;
        Place();
    }

    private void OnItem(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemTrayItem.Overflow))
            Place();
    }

    private void OnUpdates(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UpdatesService.Status) or nameof(UpdatesService.Version))
            PublishUpdate();
    }

    private void Place()
    {
        List<SystemTrayItem> onBar = [];
        List<SystemTrayItem> hidden = [];
        foreach (var item in _tray.Items)
        {
            if (item.Overflow)
                hidden.Add(item);
            else
                onBar.Add(item);
        }
        // The same icons in the same order stay put, so an open flyout is not rebuilt when only the other row changes.
        Assign(OnBar, onBar);
        Assign(Hidden, hidden);
        HasOverflow = hidden.Count > 0;
    }

    private static void Assign(ObservableCollection<SystemTrayItem> current, List<SystemTrayItem> next)
    {
        if (current.SequenceEqual(next))
            return;
        current.Clear();
        foreach (var item in next)
            current.Add(item);
    }

    private void PublishUpdate()
    {
        UpdateReady = ShowsUpdate(_updates.Status);
        // Only a ready update has a version to name, and the button is hidden until then.
        UpdateLabel = UpdateReady ? UpdateCaption(_updates.Version) : "";
    }
}
