using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// The right side of one taskbar, left of the clock: hidden icons, the icons on the bar, a ready update, and quick
/// settings. A click on quick settings or the update raises an event. Nothing opens from them.
/// </summary>
public sealed partial class TrayAreaView : UserControl
{
    private readonly Grid _hidden;
    private readonly Flyout _flyout;

    public TrayAreaView(TrayArea area, ShellWindow window)
    {
        Area = area;
        Window = window;
        InitializeComponent();
        _hidden = new Grid();
        AutomationProperties.SetName(_hidden, "Hidden icons");
        for (var column = 0; column < 5; column++)
            _hidden.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var presenter = new Style(typeof(FlyoutPresenter));
        presenter.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(4)));
        presenter.Setters.Add(new Setter(AutomationProperties.NameProperty, "Hidden icons"));
        _flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Top,
            ShouldConstrainToRootBounds = false,
            Content = _hidden,
            FlyoutPresenterStyle = presenter,
        };
        _flyout.Opened += OnFlyoutOpened;
        _flyout.Closed += OnFlyoutClosed;
        Chevron.Flyout = _flyout;
        Area.Hidden.CollectionChanged += OnHidden;
        Area.PropertyChanged += OnArea;
        BarIcons.ElementPrepared += OnPrepared;
        BarIcons.ElementClearing += OnClearing;
        PlaceHidden();
    }

    public TrayArea Area { get; }

    public ShellWindow Window { get; }

    /// <summary>Drops the icons and the bindings. The window is about to close.</summary>
    public void Release()
    {
        Area.Hidden.CollectionChanged -= OnHidden;
        Area.PropertyChanged -= OnArea;
        BarIcons.ElementPrepared -= OnPrepared;
        BarIcons.ElementClearing -= OnClearing;
        _flyout.Opened -= OnFlyoutOpened;
        _flyout.Closed -= OnFlyoutClosed;
        _flyout.Hide();
        ClearHidden();
        Bindings.StopTracking();
    }

    private void OnHidden(object? sender, NotifyCollectionChangedEventArgs e) => PlaceHidden();

    private void OnArea(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && e.PropertyName != nameof(TrayArea.HasOverflow))
            return;
        if (!Area.HasOverflow)
            _flyout.Hide();
    }

    private void OnPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is TrayIconButton button)
            button.Prepare(Window);
    }

    private void OnClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is TrayIconButton button)
            button.Release();
    }

    private void OnFlyoutOpened(object? sender, object args) => ChevronIcon.Glyph = TrayArea.ChevronGlyph(true);

    private void OnFlyoutClosed(object? sender, object args) => ChevronIcon.Glyph = TrayArea.ChevronGlyph(false);

    private void PlaceHidden()
    {
        ClearHidden();
        var items = Area.Hidden;
        var rows = (items.Count + 4) / 5;
        while (_hidden.RowDefinitions.Count < rows)
            _hidden.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        while (_hidden.RowDefinitions.Count > rows)
            _hidden.RowDefinitions.RemoveAt(_hidden.RowDefinitions.Count - 1);
        for (var i = 0; i < items.Count; i++)
        {
            var button = new TrayIconButton
            {
                Window = Window,
                Item = items[i],
                Width = 40,
                Height = 40,
            };
            Grid.SetColumn(button, i % 5);
            Grid.SetRow(button, i / 5);
            _hidden.Children.Add(button);
        }
    }

    private void ClearHidden()
    {
        foreach (var child in _hidden.Children)
        {
            if (child is TrayIconButton button)
                button.Release();
        }
        _hidden.Children.Clear();
    }

    private Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GlyphShown(string glyph) => string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
}
