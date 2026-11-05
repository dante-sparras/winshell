using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Winshell.Tray;

/// <summary>The icon a balloon notification asked for, from its <c>NIIF_</c> flags.</summary>
public enum BalloonIcon
{
    /// <summary>No icon.</summary>
    None = 0,

    /// <summary>The information icon.</summary>
    Info = 1,

    /// <summary>The warning icon.</summary>
    Warning = 2,

    /// <summary>The error icon.</summary>
    Error = 3,

    /// <summary>The app's own icon.</summary>
    App = 4
}

/// <summary>
/// The notification area icons of every app, in the order they were added. An icon the app hid is left out until the
/// app shows it again. Only one running instance in the Windows session owns the notification area; in the others
/// <see cref="Items"/> stays empty until that one exits.
/// </summary>
public sealed class SystemTray : ObservableObject, IDisposable
{
    // The trays of this process with the notification area window open.
    private static readonly ConcurrentDictionary<SystemTray, byte> Opened = new();

    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly ITrayBackend _backend;
    private readonly TrayIcons _icons;
    private readonly TrayClicker _clicker;
    private readonly Dictionary<string, SystemTrayItem> _items = [];
    private bool _open;
    private bool _waiting;
    private nint _answered;
    private IDisposable? _timer;
    private IDisposable? _answer;
    private bool _disposed;

    internal SystemTray(IEventLoop loop, Log log, ITrayBackend? backend = null)
    {
        _loop = loop;
        _log = log;
        _backend = backend ?? new TrayBackend(loop, log, TrayNames.Session);
        _icons = new TrayIcons(_backend.ReadIcon, OwnerOf);
        _clicker = new TrayClicker(loop, _backend);
        _icons.Added += OnChanged;
        _icons.Changed += OnChanged;
        _icons.Removed += OnRemoved;
        _icons.Balloon += OnBalloon;
        // The backend raises these on the loop's thread.
        _backend.TaskbarCreated += OnTaskbarCreated;
        _backend.ExplorerInFront += _backend.Raise;
        _backend.SettingsChanged += UseSettings;
        loop.Post(Start);
    }

    /// <summary>The icons apps show, in the order they were added. Empty while another instance has the tray.</summary>
    public ObservableCollection<SystemTrayItem> Items { get; } = [];

    /// <summary>
    /// Puts Explorer's taskbar in front of this process's trays until disposed, so an app bar request this process
    /// sends reaches Explorer itself. When the first <c>Shell_TrayWnd</c> belongs to the calling process,
    /// <c>SHAppBarMessage</c> passes the rectangle by an address in that process, which Explorer cannot use once a tray
    /// passes the message on: <c>ABM_QUERYPOS</c> and <c>ABM_SETPOS</c> then do nothing, and the work area stays as it was.
    /// </summary>
    internal static IDisposable ExplorerFirst()
    {
        return new Restore([.. Opened.Keys.Where(tray => tray._backend.Lower())]);
    }

    private sealed class Restore(List<SystemTray> trays) : IDisposable
    {
        public void Dispose()
        {
            foreach (var tray in trays)
                if (Opened.ContainsKey(tray))
                    tray._backend.Raise();
        }
    }

    /// <summary>Becomes the notification area, or waits for the instance that has it to exit.</summary>
    private void Start()
    {
        _timer = null;
        if (_disposed)
            return;
        if (!_backend.TryOwn())
        {
            if (!_waiting)
                _log.Info("tray",
                    "Another winshell instance is the notification area; this one takes over when it exits");
            _waiting = true;
            _timer = _loop.Schedule(TimeSpan.FromMilliseconds(500), Start);
            return;
        }

        _waiting = false;
        if (!_backend.Open(_icons.Apply))
        {
            _backend.Close();
            _log.Warn("tray", "Could not create the notification area window; the system tray stays empty");
            return;
        }

        _open = true;
        Opened[this] = 0;
        _answered = _backend.Explorer;
        UseSettings();
        KeepInFront();
        // Every app adds its icons again, now to this window.
        _backend.Broadcast();
    }

    private void UseSettings()
    {
        _icons.UseSettings(_backend.ReadSettings());
    }

    /// <summary>
    /// Drops icons of apps that ended without removing them, and checks the taskbar order as a fallback for a
    /// missing signal. Explorer coming in front normally raises the window at once, without a timer.
    /// </summary>
    private void KeepInFront()
    {
        try
        {
            _backend.Raise();
            foreach (var icon in _icons.Icons.ToArray())
                if (_backend.ProcessOf(icon.Window) == 0)
                    _icons.Remove(icon.Key);
        }
        finally
        {
            if (_open)
                _timer = _loop.Schedule(TimeSpan.FromSeconds(30), KeepInFront);
        }
    }

    /// <summary>
    /// Explorer broadcasts <c>TaskbarCreated</c> when it makes a new taskbar: after a restart, a crash or an update.
    /// Apps then add their icons to whichever taskbar window is first, which can be Explorer's new one. The window
    /// gets back in front and asks once more a moment later, so every app registers through it. A broadcast while
    /// Explorer's taskbar is the one already answered is not answered: this tray's own, or another tray's.
    /// </summary>
    private void OnTaskbarCreated()
    {
        _backend.Raise();
        var explorer = _backend.Explorer;
        if (explorer == 0 || explorer == _answered)
            return;
        _answered = explorer;
        _log.Info("tray", "Explorer made a new taskbar; asking apps to add their icons again");
        _answer?.Dispose();
        _answer = _loop.Schedule(TimeSpan.FromSeconds(1), () =>
        {
            _answer = null;
            _backend.Broadcast();
        });
    }

    private (int ProcessId, string ProgramPath) OwnerOf(nint window)
    {
        return ((int)_backend.ProcessOf(window), _backend.ProgramOf(window));
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
        item = new SystemTrayItem(_clicker, icon);
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
        _timer?.Dispose();
        _answer?.Dispose();
        _timer = _answer = null;
        _clicker.Dispose();
        Opened.TryRemove(this, out _);
        // The window goes, the next instance may take over, and apps are asked to add their icons again to whichever
        // taskbar window is first now.
        _backend.Close();
        if (_open)
            _backend.Broadcast();
        _open = false;
        _backend.Dispose();
    }
}

/// <summary>
/// An app's notification area icon. Its menu belongs to the app, which draws it itself. <see cref="Display()"/> asks
/// the app to open it.
/// </summary>
public sealed class SystemTrayItem : ObservableObject
{
    private readonly TrayClicker _clicker;
    private readonly TrayIcon _icon;
    private PixelImage? _image;
    private string _tooltip;
    private bool _overflow;

    internal SystemTrayItem(TrayClicker clicker, TrayIcon icon)
    {
        _clicker = clicker;
        _icon = icon;
        _image = icon.Image;
        _tooltip = icon.Tip;
        _overflow = icon.Overflow;
        Menu = new SystemTrayMenu(this);
        Executable = icon.ProgramPath;
        ProcessId = icon.ProcessId;
        Id = Executable.Length > 0 ? Path.GetFileNameWithoutExtension(Executable) : icon.Key;
        AppName = Id;
        if (Executable.Length > 0)
            try
            {
                if (FileVersionInfo.GetVersionInfo(Executable).FileDescription is { Length: > 0 } description)
                    AppName = description;
            }
            catch (FileNotFoundException)
            {
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

    /// <summary>The process that added the icon, the owner of the window its clicks go to.</summary>
    public int ProcessId { get; }

    /// <summary>The icon as the app set it, at the size it chose. It changes when the app changes the icon.</summary>
    public PixelImage? Icon => _image;

    /// <summary>The icon's tooltip, which may run over several lines.</summary>
    public string Tooltip => _tooltip;

    /// <summary>Windows shows this icon in the hidden icons flyout, not in the taskbar. Users choose in Settings.</summary>
    public bool Overflow => _overflow;

    /// <summary>The icon's menu, which the app draws itself.</summary>
    public SystemTrayMenu Menu { get; }

    /// <summary>
    /// The icon at a size in pixels, for the screen's scale. An icon loaded from a resource is drawn again at that size.
    /// Any other is the app's own bitmap, at the size it chose.
    /// </summary>
    public PixelImage? IconAt(int size)
    {
        if (_icon.Image is not { } image)
            return null;
        return size > 0 && size != image.Width && _icon.Resource is { } resource
            ? IconImage.FromResource(resource, size) ?? image
            : image;
    }

    /// <summary>A left click at the cursor, which most apps answer by showing their window.</summary>
    public void Click()
    {
        _clicker.Click(_icon, TrayClick.Left, null);
    }

    /// <summary>A double click at the cursor, which many older apps need before they show their window.</summary>
    public void DoubleClick()
    {
        _clicker.Click(_icon, TrayClick.Double, null);
    }

    /// <summary>A middle click at the cursor. What it does is up to the app.</summary>
    public void MiddleClick()
    {
        _clicker.Click(_icon, TrayClick.Middle, null);
    }

    /// <summary>A right click, which asks the app to open its menu at the cursor.</summary>
    public void Display()
    {
        _clicker.Click(_icon, TrayClick.Menu, null);
    }

    /// <summary>A right click, which asks the app to open its menu at a point on the virtual desktop, in physical pixels.</summary>
    public void Display(int x, int y)
    {
        _clicker.Click(_icon, TrayClick.Menu, (x, y));
    }

    internal void Refresh()
    {
        Set(ref _image, _icon.Image, nameof(Icon));
        Set(ref _tooltip, _icon.Tip, nameof(Tooltip));
        Set(ref _overflow, _icon.Overflow, nameof(Overflow));
    }

    internal void OnReady()
    {
        Ready?.Invoke();
    }

    internal void OnBalloon(string title, string message, BalloonIcon icon)
    {
        Balloon?.Invoke(title, message, icon);
    }
}

/// <summary>An app's tray menu. The app draws it; <see cref="Display()"/> asks the app to open it.</summary>
public sealed class SystemTrayMenu
{
    private readonly SystemTrayItem _item;

    internal SystemTrayMenu(SystemTrayItem item)
    {
        _item = item;
    }

    /// <summary>Opens the menu at the cursor.</summary>
    public void Display()
    {
        _item.Display();
    }

    /// <summary>Opens the menu at a point on the virtual desktop, in physical pixels.</summary>
    public void Display(int x, int y)
    {
        _item.Display(x, y);
    }
}
