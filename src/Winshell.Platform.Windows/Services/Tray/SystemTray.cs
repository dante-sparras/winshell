using System.Diagnostics;
using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Tray;

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
/// The <c>SystemTray</c>: the notification area icons of every app, in the order they were added. An icon the app
/// hid is left out until the app shows it again.
/// </summary>
public sealed class SystemTray : QObject
{
    private readonly TrayHost? _host;
    private readonly Dictionary<string, SystemTrayItem> _items = [];

    internal SystemTray(QmlEngine engine)
    {
        Engine = engine;
        Items = new ObjectModel();
        Items.SetOwner(this);
        _host = engine.GetService<TrayHost>() ?? (engine.GetService<IShellPlatform>() as WindowsPlatform)?.Tray;
        if (_host is null)
            return;
        _host.Added += OnChanged;
        _host.Changed += OnChanged;
        _host.Removed += OnRemoved;
        _host.Balloon += OnBalloon;
        foreach (var icon in _host.Icons)
            OnChanged(icon);
    }

    [Qml] public ObjectModel Items { get; }

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
        item = new SystemTrayItem(_host!, icon) { Engine = Engine };
        item.SetOwner(this);
        _items[icon.Key] = item;
        Items.Insert(Items.Rows.Count, item);
        Engine?.Loop.Post(() => item.Emit("ready"));
    }

    private void OnRemoved(TrayIcon icon)
    {
        if (!_items.Remove(icon.Key, out var item))
            return;
        Items.Remove(item);
        item.Destroy();
    }

    private void OnBalloon(TrayIcon icon, string title, string message, uint flags)
    {
        if (!_items.TryGetValue(icon.Key, out var item))
            return;
        var kind = (int)(flags & 0xf);
        item.Emit("balloon", title, message, kind <= (int)BalloonIcon.App ? kind : (int)BalloonIcon.None);
    }

    protected override void OnDestroy()
    {
        if (_host is null)
            return;
        _host.Added -= OnChanged;
        _host.Changed -= OnChanged;
        _host.Removed -= OnRemoved;
        _host.Balloon -= OnBalloon;
    }
}

/// <summary>
/// The <c>SystemTrayItem</c> for an app's notification area icon. Its menu belongs to the app, which only
/// shows it itself: <c>display()</c> and <c>MenuAnchor</c> open it, while <c>MenuOpener</c> sees no entries.
/// </summary>
[QmlSignal("ready")]
[QmlSignal("balloon", "title", "message", "icon")]
public sealed class SystemTrayItem : QObject
{
    private readonly TrayHost _host;
    private readonly TrayIcon _icon;

    internal SystemTrayItem(TrayHost host, TrayIcon icon)
    {
        _host = host;
        _icon = icon;
        Menu = new TrayMenu(this);
        Menu.SetOwner(this);
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

    /// <summary>The program's file name without its extension.</summary>
    [Qml] public string Id { get; }

    /// <summary>The program's description, as Task Manager shows it, or its file name.</summary>
    [Qml] public string AppName { get; }

    /// <summary>The full path of the program that added the icon.</summary>
    [Qml] public string Executable { get; }

    [Qml] public string Icon { get { Track(); return $"image://trayicon/{_icon.Key}/{_icon.ImageGeneration}"; } }

    /// <summary>The icon's tooltip, which may run over several lines.</summary>
    [Qml] public string Tooltip { get { Track(); return _icon.Tip; } }

    /// <summary>Windows shows this icon in the hidden icons flyout, not in the taskbar. Users choose in Settings.</summary>
    [Qml] public bool Overflow { get { Track(); return _icon.Overflow; } }

    [Qml] public MenuHandle Menu { get; }

    /// <summary>A left click at the cursor, which most apps answer by showing their window.</summary>
    [Qml]
    public void Click() => _host.Click(_icon, TrayClick.Left, null);

    /// <summary>A double click at the cursor, which many older apps need before they show their window.</summary>
    [Qml]
    public void DoubleClick() => _host.Click(_icon, TrayClick.Double, null);

    [Qml]
    public void MiddleClick() => _host.Click(_icon, TrayClick.Middle, null);

    /// <summary>A right click, which asks the app to open its menu at the point in the window, or at the cursor.</summary>
    [Qml]
    public void Display(ShellWindow? parentWindow, double relativeX, double relativeY) =>
        _host.Click(_icon, TrayClick.Menu, MenuHandle.ScreenPoint(parentWindow, relativeX, relativeY));

    internal void Refresh()
    {
        foreach (var name in new[] { "icon", "tooltip", "overflow" })
            NotifyChanged(name);
    }

    private sealed class TrayMenu(SystemTrayItem item) : MenuHandle
    {
        public override void Display(ShellWindow? window, double x, double y) => item.Display(window, x, y);
    }
}

/// <summary>The images of tray icons: <c>image://trayicon/key/generation</c>.</summary>
internal sealed class TrayIconProvider : IImageProvider
{
    public static readonly TrayIconProvider Instance = new();

    public static TrayHost? Host { get; set; }

    public SKImage? Load(string id, int requestedWidth, int requestedHeight)
    {
        var key = id.Split('/')[0];
        // The host keeps the image; the cache gets a copy it may dispose of.
        return Host?.Find(key)?.Image is { } image ? SKImage.FromBitmap(SKBitmap.FromImage(image)) : null;
    }
}

internal static class SystemTrayModule
{
    public static void Register(ModuleRegistry registry)
    {
        ImageLoader.AddProvider("trayicon", TrayIconProvider.Instance);
        registry.Module("Winshell.Tray")
            .Singleton(e => new SystemTray(e), "SystemTray")
            .Uncreatable<SystemTrayItem>("SystemTrayItem")
            .Enum<BalloonIcon>(b => b.ToString());
    }
}
