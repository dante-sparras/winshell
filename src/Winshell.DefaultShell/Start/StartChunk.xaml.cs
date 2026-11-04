using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>One pinned tile or one row in Start. Icons are drawn only while Start is open.</summary>
public sealed partial class StartChunk : UserControl
{
    private StartView? _view;
    private StartEntry? _watched;
    private bool _hover;
    private bool _pressed;

    public StartChunk()
    {
        InitializeComponent();
        Row.PointerEntered += (_, _) => { _hover = true; Paint(); };
        Row.PointerExited += (_, _) => { _hover = false; _pressed = false; Paint(); };
        // Button marks a left press and release handled, so these take handled events too.
        Row.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => { _pressed = true; Paint(); }), handledEventsToo: true);
        Row.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => { _pressed = false; Paint(); }), handledEventsToo: true);
        Row.PointerCanceled += (_, _) => { _pressed = false; Paint(); };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
        nameof(Entry), typeof(StartEntry), typeof(StartChunk), new PropertyMetadata(null, OnEntryChanged));

    public StartEntry? Entry
    {
        get => (StartEntry?)GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    private static void OnEntryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((StartChunk)sender).Watch(args.NewValue as StartEntry);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not StartView view)
                continue;
            _view = view;
            view.Register(this);
            break;
        }
        Watch(Entry);
        Apply();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _view?.Unregister(this);
        _view = null;
        Watch(null);
    }

    private void Watch(StartEntry? entry)
    {
        if (ReferenceEquals(_watched, entry))
        {
            Apply();
            return;
        }
        if (_watched is not null)
            _watched.PropertyChanged -= OnEntry;
        _watched = entry;
        if (entry is not null)
            entry.PropertyChanged += OnEntry;
        Apply();
    }

    private void OnEntry(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartEntry.IsSelected))
            Paint();
    }

    /// <summary>Draws or drops the icon. Called when Start opens and when it closes.</summary>
    internal void Apply()
    {
        var entry = Entry;
        if (entry is null)
            return;
        var tile = entry.Section == "Pinned";
        var icon = tile ? StartList.PinnedIcon : entry.Section == "All apps" ? StartList.AllIcon : StartList.RowIcon;
        var row = entry.Section == "All apps" ? StartList.AllRow : StartList.RecommendedRow;
        Heading.Text = entry.Header;
        Heading.Visibility = entry.Leads ? Visibility.Visible : Visibility.Collapsed;
        if (entry.Leads)
            AutomationProperties.SetName(Heading, entry.Header);
        AutomationProperties.SetName(Row, entry.Name);
        AutomationProperties.SetHelpText(Row, entry.Section);
        Label.Text = entry.Name;
        if (tile)
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            Width = StartList.TileWidth;
            Body.Width = StartList.TileWidth;
            Body.Height = StartList.TileHeight;
            IconImage.Width = icon;
            IconImage.Height = icon;
            IconImage.HorizontalAlignment = HorizontalAlignment.Center;
            IconImage.VerticalAlignment = VerticalAlignment.Top;
            IconImage.Margin = new Thickness(0, 8, 0, 0);
            Mark.Visibility = Visibility.Collapsed;
            Label.Margin = new Thickness(4, 44, 4, 0);
            Label.HorizontalAlignment = HorizontalAlignment.Stretch;
            Label.VerticalAlignment = VerticalAlignment.Top;
            Label.TextAlignment = TextAlignment.Center;
            Label.FontSize = 12;
        }
        else
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Width = double.NaN;
            Body.Width = double.NaN;
            Body.Height = row;
            IconImage.Width = icon;
            IconImage.Height = icon;
            IconImage.HorizontalAlignment = HorizontalAlignment.Left;
            IconImage.VerticalAlignment = VerticalAlignment.Center;
            IconImage.Margin = new Thickness(8, 0, 0, 0);
            Mark.Margin = new Thickness(8, 0, 0, 0);
            Label.Margin = new Thickness(icon + 16, 0, 12, 0);
            Label.HorizontalAlignment = HorizontalAlignment.Stretch;
            Label.VerticalAlignment = VerticalAlignment.Center;
            Label.TextAlignment = TextAlignment.Left;
            Label.FontSize = 14;
        }
        var glyph = entry.Glyph.Length > 0 && entry.Icon.Length == 0;
        Mark.Visibility = glyph ? Visibility.Visible : Visibility.Collapsed;
        Mark.Glyph = entry.Glyph;
        var showIcon = _view?.ShowIcons == true && entry.Icon.Length > 0;
        IconImage.Source = showIcon ? ShellIcon.Load(entry.Icon, ShellIcon.Pixels(XamlRoot, icon)) : null;
        IconImage.Visibility = IconImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        Paint();
    }

    private void Paint()
    {
        FillPressed.Visibility = _pressed ? Visibility.Visible : Visibility.Collapsed;
        FillHover.Visibility = !_pressed && (_hover || Entry?.IsSelected == true) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (Entry is { } entry)
            _view?.Activate(entry);
    }

    private void OnContext(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Entry is not { } entry || _view is not { } view || !view.Popup.BeginMenu(entry))
            return;
        args.Handled = true;
        view.ShowMenu(Row);
    }
}
