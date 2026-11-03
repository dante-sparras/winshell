using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Winshell.DefaultShell.Taskbar;
using Winshell.Tray;

namespace Winshell.DefaultShell;

/// <summary>
/// One notification-area icon. A left click, a middle click and a double click reach the app. A right click asks the
/// app for its menu at the pointer. Winland's icon opens <see cref="WinlandMenu"/> on a click or a right click, which
/// the shell draws itself.
/// </summary>
public sealed partial class TrayIconButton : Button
{
    private SystemTrayItem? _item;
    private PixelImage? _image;
    private int _pixels;
    private MenuFlyout? _menu;

    public TrayIconButton()
    {
        InitializeComponent();
        Click += (_, _) =>
        {
            if (!ShowWinlandMenu())
                Item?.Click();
        };
        // Winland takes the second click of a double click for one that opens its menu.
        DoubleTapped += (_, _) =>
        {
            if (Winland() is null)
                Item?.DoubleClick();
        };
        PointerReleased += OnPointerReleased;
        ContextRequested += OnContextRequested;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The tray area this icon sits in. It sets this as it shows the icon.</summary>
    internal TrayAreaView? Tray { get; set; }

    public SystemTrayItem? Item
    {
        get => _item;
        set
        {
            Follow(false);
            _item = value;
            Follow(true);
            Show();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is not null)
            XamlRoot.Changed += OnRootChanged;
        Follow(true);
        Show();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is not null)
            XamlRoot.Changed -= OnRootChanged;
        Follow(false);
        _menu?.Hide();
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Show();

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonReleased)
            return;
        Item?.MiddleClick();
        e.Handled = true;
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Item is not { } item)
            return;
        args.Handled = true;
        if (!ShowWinlandMenu())
            item.Display();
    }

    /// <summary>The menu, when this icon is Winland's and the tray draws Winland's menu.</summary>
    private WinlandMenu? Winland() =>
        Item is { } item && Tray?.Area.Winland is { } menu && menu.Owns(item) ? menu : null;

    private bool ShowWinlandMenu()
    {
        if (Winland() is not { } model || Tray is not { } tray)
            return false;
        // The rows are read as the menu opens, so Open log follows the log file.
        var menu = TaskMenuFlyout.Create(WinlandMenu.Name, model.Rows(), model.Choose, ShellIcon.Pixels(XamlRoot, 16));
        menu.Opened += (_, _) => tray.SetMenuOpen(true);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, menu))
                _menu = null;
            tray.SetMenuOpen(false);
        };
        _menu = menu;
        menu.ShowAt(this);
        return true;
    }

    private void Follow(bool on)
    {
        if (_item is null)
            return;
        _item.PropertyChanged -= OnItemChanged;
        if (on)
            _item.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemTrayItem.Icon) or nameof(SystemTrayItem.Tooltip))
            Show();
    }

    private void Show()
    {
        var item = Item;
        var tip = item is null ? "" : TrayArea.IconTip(item.Tooltip, item.AppName);
        AutomationProperties.SetName(this, item is null ? "" : TrayArea.IconName(item.Tooltip, item.AppName));
        ToolTipService.SetToolTip(this, tip.Length == 0 ? null : tip);
        var pixels = ShellIcon.Pixels(XamlRoot, 16);
        var image = item?.IconAt(pixels);
        if (ReferenceEquals(image, _image) && pixels == _pixels)
            return;
        _image = image;
        _pixels = pixels;
        Icon.Source = TrayImage.Bitmap(image);
    }
}
