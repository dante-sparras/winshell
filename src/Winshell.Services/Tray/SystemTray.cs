using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Winshell.Tray;

/// <summary>The icon a balloon notification asked for, from its <c>NIIF_</c> flags.</summary>
public enum BalloonIcon
{
    None = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    /// <summary>The app's own icon.</summary>
    App = 4,
}

/// <summary>
/// The notification area icons of every app, in the order they were added. An icon the app hid is left out until the
/// app shows it again. Only one running instance in the Windows session owns the notification area; in the others
/// <see cref="Items"/> stays empty until that one exits.
/// </summary>
public sealed class SystemTray : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly ITrayHost _host;
    private readonly TrayHost? _owned;
    private readonly Dictionary<string, SystemTrayItem> _items = [];
    private bool _disposed;

    internal SystemTray(IEventLoop loop, Log log, ITrayHost? host = null)
    {
        _loop = loop;
        _owned = host is null ? new TrayHost(loop, log, TrayNames.Session, TrayHost.BroadcastTaskbarCreated) : null;
        _host = host ?? _owned!;
        // The host raises these on the loop's thread.
        _host.Added += OnChanged;
        _host.Changed += OnChanged;
        _host.Removed += OnRemoved;
        _host.Balloon += OnBalloon;
        foreach (var icon in _host.Icons)
            OnChanged(icon);
        if (_owned is not null)
            loop.Post(StartHost);
    }

    public ObservableCollection<SystemTrayItem> Items { get; } = [];

    private void StartHost()
    {
        if (!_disposed)
            _owned!.Start();
    }

    private void OnChanged(TrayIcon icon)
    {
        if (_items.TryGetValue(icon.Key, out var item))
        {
            if (icon.Hidden)
                OnRemoved(icon);
            else
                item.Refresh();
            return;
        }
        if (icon.Hidden)
            return;
        item = new SystemTrayItem(_host, icon);
        _items[icon.Key] = item;
        Items.Add(item);
        _loop.Post(() =>
        {
            if (!_disposed)
                item.OnReady();
        });
    }

    private void OnRemoved(TrayIcon icon)
    {
        if (_items.Remove(icon.Key, out var item))
            Items.Remove(item);
    }

    private void OnBalloon(TrayIcon icon, string title, string message, uint flags)
    {
        if (!_items.TryGetValue(icon.Key, out var item))
            return;
        var kind = (int)(flags & 0xf);
        item.OnBalloon(title, message, kind <= (int)BalloonIcon.App ? (BalloonIcon)kind : BalloonIcon.None);
    }

    /// <summary>Gives the notification area up. Call on the loop's thread.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _host.Added -= OnChanged;
        _host.Changed -= OnChanged;
        _host.Removed -= OnRemoved;
        _host.Balloon -= OnBalloon;
        _owned?.Dispose();
    }
}

/// <summary>
/// An app's notification area icon. Its menu belongs to the app, which draws it itself. <see cref="Display()"/> asks
/// the app to open it.
/// </summary>
public sealed class SystemTrayItem : ObservableObject
{
    private readonly ITrayHost _host;
    private readonly TrayIcon _icon;
    private PixelImage? _image;
    private string _tooltip;
    private bool _overflow;

    internal SystemTrayItem(ITrayHost host, TrayIcon icon)
    {
        _host = host;
        _icon = icon;
        _image = icon.Image;
        _tooltip = icon.Tip;
        _overflow = icon.Overflow;
        Menu = new SystemTrayMenu(this);
        Executable = icon.ProgramPath;
        Id = Executable.Length > 0 ? Path.GetFileNameWithoutExtension(Executable) : icon.Key;
        AppName = Id;
        if (Executable.Length > 0)
        {
            try
            {
                if (FileVersionInfo.GetVersionInfo(Executable).FileDescription is { Length: > 0 } description)
                    AppName = description;
            }
            catch (FileNotFoundException)
            {
            }
        }
    }

    /// <summary>Raised once, just after the icon is added to <see cref="SystemTray.Items"/>.</summary>
    public event Action? Ready;

    /// <summary>The app showed a balloon notification: its title, its text and its icon. Explorer still shows it as a toast.</summary>
    public event Action<string, string, BalloonIcon>? Balloon;

    /// <summary>The program's file name without its extension.</summary>
    public string Id { get; }

    /// <summary>The program's description, as Task Manager shows it, or its file name.</summary>
    public string AppName { get; }

    /// <summary>The full path of the program that added the icon.</summary>
    public string Executable { get; }

    /// <summary>The icon as the app set it, at the size it chose. It changes when the app changes the icon.</summary>
    public PixelImage? Icon => _image;

    /// <summary>The icon's tooltip, which may run over several lines.</summary>
    public string Tooltip => _tooltip;

    /// <summary>Windows shows this icon in the hidden icons flyout, not in the taskbar. Users choose in Settings.</summary>
    public bool Overflow => _overflow;

    public SystemTrayMenu Menu { get; }

    /// <summary>
    /// The icon at a size in pixels, for the screen's scale. An icon loaded from a resource is drawn again at that size.
    /// Any other is the app's own bitmap, at the size it chose.
    /// </summary>
    public PixelImage? IconAt(int size)
    {
        if (_icon.Image is not { } image)
            return null;
        return size > 0 && size != image.Width && _icon.Resource is { } resource ? TrayHost.ResourceImage(resource, size) ?? image : image;
    }

    /// <summary>A left click at the cursor, which most apps answer by showing their window.</summary>
    public void Click() => _host.Click(_icon, TrayClick.Left, null);

    /// <summary>A double click at the cursor, which many older apps need before they show their window.</summary>
    public void DoubleClick() => _host.Click(_icon, TrayClick.Double, null);

    public void MiddleClick() => _host.Click(_icon, TrayClick.Middle, null);

    /// <summary>A right click, which asks the app to open its menu at the cursor.</summary>
    public void Display() => _host.Click(_icon, TrayClick.Menu, null);

    /// <summary>A right click, which asks the app to open its menu at a point on the virtual desktop, in physical pixels.</summary>
    public void Display(int x, int y) => _host.Click(_icon, TrayClick.Menu, (x, y));

    internal void Refresh()
    {
        Set(ref _image, _icon.Image, nameof(Icon));
        Set(ref _tooltip, _icon.Tip, nameof(Tooltip));
        Set(ref _overflow, _icon.Overflow, nameof(Overflow));
    }

    internal void OnReady() => Ready?.Invoke();

    internal void OnBalloon(string title, string message, BalloonIcon icon) => Balloon?.Invoke(title, message, icon);
}

/// <summary>An app's tray menu. The app draws it; <see cref="Display()"/> asks the app to open it.</summary>
public sealed class SystemTrayMenu
{
    private readonly SystemTrayItem _item;

    internal SystemTrayMenu(SystemTrayItem item) => _item = item;

    /// <summary>Opens the menu at the cursor.</summary>
    public void Display() => _item.Display();

    /// <summary>Opens the menu at a point on the virtual desktop, in physical pixels.</summary>
    public void Display(int x, int y) => _item.Display(x, y);
}
