using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Winshell.DefaultShell.Taskbar;
using Winshell.Windows;

namespace Winshell.DefaultShell;

/// <summary>
/// Alt+Tab and Win+Tab. The cards follow the order windows opened. Left and Right move the selection. Enter, Space
/// or a click activates it. A desktop switches and the switcher stays open. Letting go of Alt does not activate.
/// </summary>
public sealed partial class SwitcherView : UserControl
{
    private readonly DesktopSwitcher _switcher;
    private readonly WindowListService _windows;
    private readonly VirtualDesktopsService _desktops;
    private readonly List<Button> _cards = [];
    private readonly List<IOpenWindow> _watched = [];
    private readonly List<VirtualDesktop> _desktopsWatched = [];
    private int _windowCount = -1;
    private bool _showDesktops;
    private bool _rebuilding;

    public SwitcherView(DesktopSwitcher switcher, WindowListService windows, VirtualDesktopsService desktops)
    {
        _switcher = switcher;
        _windows = windows;
        _desktops = desktops;
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        _windows.Windows.CollectionChanged += OnWindows;
        _desktops.Desktops.CollectionChanged += OnDesktops;
        _desktops.PropertyChanged += OnDesktopsProperty;
        WatchDesktops();
    }

    /// <summary>The cards or the desktop row changed size, so the popup is placed again.</summary>
    internal event EventHandler? LayoutChanged;

    /// <summary>Draws the open windows and the desktops as they are now.</summary>
    public void Refresh() => Rebuild();

    /// <summary>The chosen card, or the switcher itself when nothing is open.</summary>
    public void FocusChosen()
    {
        if (_cards.Count == 0)
        {
            Focus(FocusState.Programmatic);
            return;
        }
        var card = _cards[Mod(_switcher.Selected, _cards.Count)];
        card.Focus(FocusState.Keyboard);
        card.StartBringIntoView();
    }

    /// <summary>Drops what this view follows. The popups are about to close.</summary>
    public void Release()
    {
        _windows.Windows.CollectionChanged -= OnWindows;
        _desktops.Desktops.CollectionChanged -= OnDesktops;
        _desktops.PropertyChanged -= OnDesktopsProperty;
        UnwatchWindows();
        UnwatchDesktops();
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right))
            return;
        if (FocusedIsDesktop())
            return;
        e.Handled = true;
        _switcher.Cycle(e.Key == VirtualKey.Left ? -1 : 1, Open(), null);
    }

    private void OnWindows(object? sender, NotifyCollectionChangedEventArgs e) => RefreshIfShown();

    private void OnDesktops(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WatchDesktops();
        RefreshIfShown();
    }

    private void OnDesktopsProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VirtualDesktopsService.Supported))
            RefreshIfShown();
    }

    private void OnDesktopProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VirtualDesktop.Current) or nameof(VirtualDesktop.Name))
            RefreshIfShown();
    }

    private void OnWindowProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ITaskWindow.Title) or nameof(ITaskWindow.Icon))
            RefreshIfShown();
    }

    private void RefreshIfShown()
    {
        if (IsLoaded)
            Rebuild();
    }

    private void Rebuild()
    {
        if (_rebuilding)
            return;
        _rebuilding = true;
        try
        {
            var windows = Open();
            var showDesktops = DesktopChordLayout.ShowDesktops(_desktops.Supported, _desktops.Desktops.Count);
            var layout = windows.Count != _windowCount || showDesktops != _showDesktops;
            _windowCount = windows.Count;
            _showDesktops = showDesktops;
            UnwatchWindows();
            Cards.Children.Clear();
            DeskRow.Children.Clear();
            _cards.Clear();
            EmptyText.Visibility = windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Strip.Visibility = windows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            DeskRow.Visibility = showDesktops ? Visibility.Visible : Visibility.Collapsed;
            var chosen = windows.Count == 0 ? -1 : Mod(_switcher.Selected, windows.Count);
            for (var i = 0; i < windows.Count; i++)
                Cards.Children.Add(Card(windows[i], i == chosen));
            if (showDesktops)
            {
                foreach (var desktop in _desktops.Desktops)
                    DeskRow.Children.Add(Desk(desktop));
            }
            foreach (var window in windows)
            {
                window.PropertyChanged += OnWindowProperty;
                _watched.Add(window);
            }
            if (layout && IsLoaded)
                LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private Grid Card(IOpenWindow window, bool chosen)
    {
        var root = new Grid { Width = 200, Height = 140 };
        var chosenFill = new Border { Style = (Style)Resources["ChosenFill"], Visibility = Shown(chosen) };
        var hover = new Border { Style = (Style)Resources["HoverFill"], Visibility = Visibility.Collapsed };
        var icon = new Image
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(8, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Stretch = Stretch.Uniform,
            Source = window.Icon.Length == 0 ? null : ShellIcon.Load(window.Icon, ShellIcon.Pixels(XamlRoot, 16)),
        };
        var title = new TextBlock
        {
            Style = (Style)Resources["Caption"],
            Margin = new Thickness(32, 8, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = window.Title,
        };
        // The image starts below the title. It is painted over the XAML, so the title stays above it.
        var thumbnail = new WindowThumbnail
        {
            Margin = new Thickness(8, 32, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Window = window is TaskWindow real ? real.Window : null,
        };
        var content = new Grid();
        content.Children.Add(icon);
        content.Children.Add(title);
        content.Children.Add(thumbnail);
        var button = new Button { Style = (Style)Resources["CardButton"], Content = content };
        AutomationProperties.SetName(button, window.Title);
        button.Click += (_, _) => _switcher.Choose(window);
        button.PointerEntered += (_, _) =>
        {
            if (chosenFill.Visibility != Visibility.Visible)
                hover.Visibility = Visibility.Visible;
        };
        button.PointerExited += (_, _) => hover.Visibility = Visibility.Collapsed;
        button.PointerCanceled += (_, _) => hover.Visibility = Visibility.Collapsed;
        _cards.Add(button);
        root.Children.Add(chosenFill);
        root.Children.Add(hover);
        root.Children.Add(button);
        return root;
    }

    private Grid Desk(VirtualDesktop desktop)
    {
        var root = new Grid { MinWidth = 96, Height = 32 };
        var fill = new Border { Style = (Style)Resources["HoverFill"], Visibility = Shown(desktop.Current) };
        var button = new Button
        {
            Style = (Style)Resources["CardButton"],
            MinWidth = 96,
            Height = 32,
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Style = (Style)Resources["Body"], Text = desktop.Name },
            Tag = "desktop",
        };
        AutomationProperties.SetName(button, desktop.Name);
        button.Click += (_, _) => _desktops.SwitchTo(desktop);
        button.PointerEntered += (_, _) =>
        {
            if (!desktop.Current)
                fill.Visibility = Visibility.Visible;
        };
        button.PointerExited += (_, _) => fill.Visibility = Shown(desktop.Current);
        root.Children.Add(fill);
        root.Children.Add(button);
        return root;
    }

    private List<IOpenWindow> Open() => [.. _windows.Windows.Select(static window => (IOpenWindow)new TaskWindow(window))];

    private bool FocusedIsDesktop()
    {
        if (XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused)
            return false;
        for (DependencyObject? node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { Tag: "desktop" })
                return true;
        }
        return false;
    }

    private void WatchDesktops()
    {
        UnwatchDesktops();
        foreach (var desktop in _desktops.Desktops)
        {
            desktop.PropertyChanged += OnDesktopProperty;
            _desktopsWatched.Add(desktop);
        }
    }

    private void UnwatchDesktops()
    {
        foreach (var desktop in _desktopsWatched)
            desktop.PropertyChanged -= OnDesktopProperty;
        _desktopsWatched.Clear();
    }

    private void UnwatchWindows()
    {
        foreach (var window in _watched)
            window.PropertyChanged -= OnWindowProperty;
        _watched.Clear();
    }

    private static int Mod(int value, int count) => (value % count + count) % count;

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
