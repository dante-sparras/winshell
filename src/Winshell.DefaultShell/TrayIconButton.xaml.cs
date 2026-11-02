using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Winshell.DefaultShell.Taskbar;
using Winshell.Tray;

namespace Winshell.DefaultShell;

/// <summary>
/// One notification-area icon. A left click, a middle click and a double click reach the app. A right click or the
/// menu key asks the app for its menu at the middle of the icon's top edge.
/// </summary>
public sealed partial class TrayIconButton : Button
{
    private SystemTrayItem? _item;
    private SystemTrayItem? _followed;
    private PixelImage? _image;
    private int _pixels;

    public TrayIconButton()
    {
        InitializeComponent();
        Click += OnClick;
        DoubleTapped += OnDoubleTapped;
        PointerReleased += OnPointerReleased;
        ContextRequested += OnContextRequested;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ShellWindow? Window { get; set; }

    public SystemTrayItem? Item
    {
        get => _item;
        set
        {
            _item = value;
            Follow(value);
            Show();
        }
    }

    /// <summary>The icon was realized on the bar. <paramref name="window"/> maps its menu onto the screen.</summary>
    public void Prepare(ShellWindow window)
    {
        Window = window;
        WatchRoot();
        Follow(Item);
        Show();
    }

    /// <summary>Drops the icon and the scale watch. Safe to call again.</summary>
    public void Release()
    {
        if (XamlRoot is not null)
            XamlRoot.Changed -= OnRootChanged;
        Follow(null);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WatchRoot();
        Follow(Item);
        Show();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Release();

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Show();

    private void WatchRoot()
    {
        if (XamlRoot is null)
            return;
        XamlRoot.Changed -= OnRootChanged;
        XamlRoot.Changed += OnRootChanged;
    }

    private void OnClick(object sender, RoutedEventArgs e) => Item?.Click();

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Item?.DoubleClick();

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonReleased)
            return;
        Item?.MiddleClick();
        e.Handled = true;
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Item is not { } item || Window is not { } window)
            return;
        args.Handled = true;
        var point = TransformToVisual(null).TransformPoint(new Point(ActualWidth / 2, 0));
        var screen = window.MapToScreen(point.X, point.Y);
        item.Display(screen.X, screen.Y);
    }

    private void Follow(SystemTrayItem? item)
    {
        if (ReferenceEquals(_followed, item))
            return;
        if (_followed is not null)
            _followed.PropertyChanged -= OnItemChanged;
        _followed = item;
        if (item is not null)
            item.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(SystemTrayItem.Icon) or nameof(SystemTrayItem.Tooltip))
            Show();
    }

    private void Show()
    {
        var item = Item;
        var tip = item is null ? "" : TrayArea.IconTip(item.Tooltip, item.AppName);
        AutomationProperties.SetName(this, item is null ? "" : TrayArea.IconName(item.Tooltip, item.AppName));
        ToolTipService.SetToolTip(this, tip.Length == 0 ? null : tip);
        var pixels = Pixels(16);
        var image = item?.IconAt(pixels);
        if (ReferenceEquals(image, _image) && pixels == _pixels)
            return;
        _image = image;
        _pixels = pixels;
        Icon.Source = TrayImage.Bitmap(image);
    }

    private int Pixels(int size)
    {
        var scale = XamlRoot?.RasterizationScale ?? 1;
        if (scale < 1)
            scale = 1;
        return Math.Max(1, (int)Math.Ceiling(size * scale));
    }
}
