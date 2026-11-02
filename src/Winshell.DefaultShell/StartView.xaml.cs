using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// Start for one taskbar: the search box, pinned apps, recommended, all apps, and search. The keyboard stays in the
/// search box. Escape clears a query, then leaves All apps, and then the window closes.
/// </summary>
public sealed partial class StartView : UserControl
{
    private readonly List<StartChunk> _chunks = [];
    private bool _settingQuery;

    public StartView(StartPopup popup)
    {
        Popup = popup;
        InitializeComponent();
        // Sizes are the Windows 11 ones. A bind to ActualHeight would stay 0, so the lists get a star row instead.
        SearchHost.Height = StartList.SearchHeight;
        SearchHost.Margin = new Thickness(StartList.SearchX, StartList.SearchY, StartList.Margin, 0);
        Body.Margin = new Thickness(StartList.Margin, 16, StartList.Margin, 0);
        PinScroll.Height = StartList.PinnedViewport;
        WebNote.Margin = new Thickness(StartList.Margin, 8, StartList.Margin, 8);
        Popup.PropertyChanged += OnPopup;
    }

    public StartPopup Popup { get; }

    /// <summary>True while Start is open, which is when tiles draw their icons.</summary>
    internal bool ShowIcons { get; private set; }

    internal void SetIcons(bool show)
    {
        ShowIcons = show;
        foreach (var chunk in _chunks.ToArray())
            chunk.Apply();
    }

    internal void Register(StartChunk chunk)
    {
        if (!_chunks.Contains(chunk))
            _chunks.Add(chunk);
    }

    internal void Unregister(StartChunk chunk) => _chunks.Remove(chunk);

    public void FocusSearch()
    {
        SearchBox.Focus(FocusState.Programmatic);
        // Focus at the moment the window appears can miss. The next pass lands in the box.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (Popup.IsOpen)
                SearchBox.Focus(FocusState.Programmatic);
        });
    }

    internal void Activate(StartEntry entry) => Popup.Activate(entry);

    internal void ShowMenu(FrameworkElement anchor)
    {
        var pin = new MenuFlyoutItem { Text = Popup.PinLabel };
        pin.Click += (_, _) => Popup.ToggleMenuPin();
        var location = new MenuFlyoutItem { Text = "Open file location" };
        location.Click += (_, _) => Popup.OpenLocation();
        var menu = new MenuFlyout();
        menu.Items.Add(pin);
        menu.Items.Add(location);
        menu.ShowAt(anchor);
    }

    /// <summary>Drops the bindings. The window is about to close.</summary>
    public void Release()
    {
        Popup.PropertyChanged -= OnPopup;
        Bindings.StopTracking();
    }

    private Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private void OnPinsLoaded(object sender, RoutedEventArgs e)
    {
        if (Pins.ItemsPanelRoot is not VariableSizedWrapGrid grid)
            return;
        grid.Orientation = Orientation.Horizontal;
        grid.MaximumRowsOrColumns = StartList.Columns;
        grid.ItemWidth = StartList.TileWidth + StartList.Gap;
        grid.ItemHeight = StartList.TileHeight + StartList.Gap;
    }

    private void OnPopup(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartPopup.Query))
            SyncQuery();
        else if (e.PropertyName == nameof(StartPopup.Selected))
            Reveal();
    }

    private void SyncQuery()
    {
        if (SearchBox.Text == Popup.Query)
            return;
        _settingQuery = true;
        SearchBox.Text = Popup.Query;
        _settingQuery = false;
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingQuery)
            Popup.SetQuery(SearchBox.Text);
    }

    private void OnAllApps(object sender, RoutedEventArgs e)
    {
        Popup.ShowAll();
        FocusSearch();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        Popup.ShowHome();
        FocusSearch();
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || !Popup.Escape())
            return;
        e.Handled = true;
        FocusSearch();
    }

    private void OnSearchKey(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Up:
                Popup.Move("up");
                break;
            case VirtualKey.Down:
                Popup.Move("down");
                break;
            case VirtualKey.Left:
                Popup.Move("left");
                break;
            case VirtualKey.Right:
                Popup.Move("right");
                break;
            case VirtualKey.Enter:
                Popup.Activate();
                break;
            case VirtualKey.Application:
                if (Popup.BeginMenuForSelection())
                    ShowMenu(SearchBox);
                break;
            case VirtualKey.F10 when ShiftDown():
                if (Popup.BeginMenuForSelection())
                    ShowMenu(SearchBox);
                break;
            default:
                return;
        }
        e.Handled = true;
        Reveal();
    }

    private static bool ShiftDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>Keeps the selected row in view. The lists scroll inside the window; the window is not sized from them.</summary>
    private void Reveal()
    {
        var index = Popup.Selected;
        if (Popup.Searching)
        {
            if ((uint)index < (uint)Popup.Results.Count)
                ResultsList.ScrollIntoView(Popup.Results[index]);
            return;
        }
        if (Popup.Page == "all")
        {
            if ((uint)index < (uint)Popup.All.Count)
                AllList.ScrollIntoView(Popup.All[index]);
            return;
        }
        if (index >= Popup.PinnedCount)
            return;
        var row = index / StartList.Columns;
        var y = row * (StartList.TileHeight + StartList.Gap);
        var viewport = PinScroll.ViewportHeight;
        if (viewport <= 0)
            return;
        var offset = PinScroll.VerticalOffset;
        if (y < offset)
            PinScroll.ChangeView(null, y, null, true);
        else if (y + StartList.TileHeight > offset + viewport)
            PinScroll.ChangeView(null, y + StartList.TileHeight - viewport, null, true);
    }
}
