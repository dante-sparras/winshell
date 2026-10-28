using System.Diagnostics;
using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Tray;

public enum Status
{
    Passive = 0,
    Active = 1,
    NeedsAttention = 2,
}

public enum Category
{
    Hardware = 0,
    SystemServices = 1,
    ApplicationStatus = 2,
    Communications = 3,
}

/// <summary>The <c>SystemTray</c>: the notification area icons of every app.</summary>
public sealed class SystemTray : QObject
{
    private readonly TrayHost? _host;
    private readonly Dictionary<string, SystemTrayItem> _items = [];

    internal SystemTray(QmlEngine engine)
    {
        Engine = engine;
        Items = new ObjectModel();
        Items.SetOwner(this);
        if (engine.GetService<IShellPlatform>() is not WindowsPlatform platform)
            return;
        _host = TrayHost.Get(platform);
        _host.Added += OnAdded;
        _host.Changed += OnChanged;
        _host.Removed += OnRemoved;
        foreach (var icon in _host.Icons)
            OnAdded(icon);
    }

    [Qml] public ObjectModel Items { get; }

    private void OnAdded(TrayIcon icon)
    {
        var item = new SystemTrayItem(_host!, icon) { Engine = Engine };
        item.SetOwner(this);
        _items[icon.Key] = item;
        Items.Insert(Items.Rows.Count, item);
        Engine?.Loop.Post(() => item.Emit("ready"));
    }

    private void OnChanged(TrayIcon icon)
    {
        if (_items.TryGetValue(icon.Key, out var item))
            item.Refresh();
    }

    private void OnRemoved(TrayIcon icon)
    {
        if (!_items.Remove(icon.Key, out var item))
            return;
        Items.Remove(item);
        item.Destroy();
    }

    protected override void OnDestroy()
    {
        if (_host is null)
            return;
        _host.Added -= OnAdded;
        _host.Changed -= OnChanged;
        _host.Removed -= OnRemoved;
    }
}

/// <summary>
/// The <c>SystemTrayItem</c> for an app's notification area icon. Its menu belongs to the app, which only
/// shows it itself: <c>display()</c> and <c>MenuAnchor</c> open it, while <c>MenuOpener</c> sees no entries.
/// Windows has no scroll action for these icons.
/// </summary>
[QmlSignal("ready")]
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
        var program = icon.ProgramPath;
        Id = program.Length > 0 ? Path.GetFileNameWithoutExtension(program) : icon.Key;
        Title = Id;
        if (program.Length > 0)
        {
            try
            {
                if (FileVersionInfo.GetVersionInfo(program).FileDescription is { Length: > 0 } description)
                    Title = description;
            }
            catch (FileNotFoundException)
            {
            }
        }
    }

    [Qml] public string Id { get; }

    [Qml] public string Title { get; }

    [Qml] public int Status { get { Track(); return (int)(_icon.Hidden ? Tray.Status.Passive : Tray.Status.Active); } }

    [Qml] public int Category => (int)Tray.Category.ApplicationStatus;

    [Qml] public string Icon { get { Track(); return $"image://trayicon/{_icon.Key}/{_icon.ImageGeneration}"; } }

    [Qml] public string TooltipTitle { get { Track(); return FirstLine(_icon.Tip); } }

    [Qml] public string TooltipDescription { get { Track(); return Rest(_icon.Tip); } }

    [Qml] public bool HasMenu => true;

    [Qml] public bool OnlyMenu => false;

    [Qml] public MenuHandle Menu { get; }

    [Qml]
    public void Activate() => _host.Click(_icon, 0, null);

    [Qml]
    public void SecondaryActivate() => _host.Click(_icon, 2, null);

    [Qml]
    public void Scroll(double delta, bool horizontal)
    {
    }

    [Qml]
    public void Display(ShellWindow? parentWindow, double relativeX, double relativeY) =>
        _host.Click(_icon, 1, MenuHandle.ScreenPoint(parentWindow, relativeX, relativeY));

    internal void Refresh()
    {
        foreach (var name in new[] { "status", "icon", "tooltipTitle", "tooltipDescription" })
            NotifyChanged(name);
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].TrimEnd('\r');

    private static string Rest(string text) => text.Split('\n', 2) is [_, var rest] ? rest.Trim() : "";

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
            .Enum<Status>(s => s.ToString())
            .Enum<Category>(c => c.ToString());
    }
}
