using Winshell.Apps;
using Winshell.Audio;
using Winshell.Bluetooth;
using Winshell.ExplorerTaskbar;
using Winshell.Input;
using Winshell.Media;
using Winshell.Network;
using Winshell.Notifications;
using Winshell.Power;
using Winshell.Session;
using Winshell.Tray;
using Winshell.Updates;
using Winshell.Windows;
using Winshell.Winland;

namespace Winshell;

/// <summary>
/// The singletons of a shell. Each is made the first time it is read, so a service a configuration does not use costs
/// nothing and asks Windows for nothing.
/// </summary>
public sealed partial class Shell
{
    private readonly List<IDisposable> _services = [];
    private AppsService? _apps;
    private AudioService? _audio;
    private BluetoothService? _bluetooth;
    private BrightnessService? _brightness;
    private ExplorerTaskbarSession? _explorerTaskbarSession;
    private ExplorerTaskbarService? _explorerTaskbar;
    private JumpListService? _jumpLists;
    private MediaService? _media;
    private NetworkingService? _networking;
    private NotificationListener? _notifications;
    private PowerService? _power;
    private PowerModeService? _powerMode;
    private RecentDocuments? _recentDocuments;
    private SessionService? _session;
    private StartupService? _startup;
    private SystemTray? _systemTray;
    private UpdatesService? _updates;
    private VirtualDesktopsService? _virtualDesktops;
    private WindowListService? _windows;
    private WinlandService? _winland;

    /// <summary>The apps Start lists.</summary>
    public AppsService Apps => Own(ref _apps, () => new AppsService(Loop, Log));

    /// <summary>Playback and recording devices, and the audio sessions on them.</summary>
    public AudioService Audio => Own(ref _audio, () => new AudioService(Loop, Log));

    public BluetoothService Bluetooth => Own(ref _bluetooth, () => new BluetoothService(Loop, Log));

    public BrightnessService Brightness => Own(ref _brightness, () => new BrightnessService(Loop, Log));

    /// <summary>Whether Explorer's taskbar is hidden. Winshell shows it again when it exits.</summary>
    public ExplorerTaskbarService ExplorerTaskbar => _explorerTaskbar ??= new ExplorerTaskbarService(Own(ref _explorerTaskbarSession, ExplorerTaskbarSession.ForThisProcess));

    public JumpListService JumpLists => _jumpLists ??= new JumpListService();

    /// <summary>The media sessions Windows lets the shell see and control.</summary>
    public MediaService Media => Own(ref _media, () => new MediaService(Loop, Log));

    public NetworkingService Networking => Own(ref _networking, () => new NetworkingService(Loop));

    /// <summary>Windows Notification Center. Reading this never prompts for access.</summary>
    public NotificationListener Notifications => Own(ref _notifications, () => new NotificationListener(Loop));

    /// <summary>The batteries of this PC.</summary>
    public PowerService Power => Own(ref _power, () => new PowerService(Loop));

    public PowerModeService PowerMode => Own(ref _powerMode, () => new PowerModeService(Loop, Log));

    public RecentDocuments RecentDocuments => _recentDocuments ??= new RecentDocuments();

    /// <summary>The Windows session: locking it, and what state it is in.</summary>
    public SessionService Session => _session ??= new SessionService(Log);

    /// <summary>Whether this shell runs when the user signs in.</summary>
    public StartupService Startup => _startup ??= new StartupService(Log, StartupRegistry());

    /// <summary>The notification area. Only one running instance in the Windows session owns it.</summary>
    public SystemTray SystemTray => Own(ref _systemTray, () => new SystemTray(Loop, Log));

    /// <summary>Winshell's own updates. A configuration's own program does not update itself.</summary>
    public UpdatesService Updates => Own(ref _updates, () => new UpdatesService(Loop, Log, Updater));

    public VirtualDesktopsService VirtualDesktops => Own(ref _virtualDesktops, () => new VirtualDesktopsService(Loop, Log));

    /// <summary>The app windows the taskbar and Alt+Tab list.</summary>
    public WindowListService WindowList => Own(ref _windows, () => new WindowListService(Loop, Log, VirtualDesktops));

    public WinlandService Winland => Own(ref _winland, () => new WinlandService(Loop, Log));

    /// <summary>What <see cref="Updates"/> follows. <c>winshell.exe</c> sets it, and a configuration's program leaves it null.</summary>
    internal Updater? Updater { get; set; }

    /// <summary>A hotkey that works anywhere in Windows. Set its <see cref="Hotkey.Sequence"/>, and dispose it to give the keys back.</summary>
    public Hotkey NewHotkey() => new(Loop);

    /// <summary>Tells when there has been no input for a while.</summary>
    public IdleMonitor NewIdleMonitor() => new(Loop);

    private T Own<T>(ref T? field, Func<T> make)
        where T : class, IDisposable
    {
        if (field is null)
        {
            field = make();
            _services.Add(field);
        }
        return field;
    }

    private static IStartupRegistry? StartupRegistry()
    {
        if (Packaging.IsPackaged)
            return WindowsStartupTasks.Open() is { } task ? new StartupTaskRegistry(task) : null;
        return Environment.ProcessPath is { } exe
            ? new WindowsStartupRegistry(WindowsStartupRegistry.CommandFor(exe, Environment.GetCommandLineArgs().Skip(1)))
            : null;
    }

    private void DisposeServices()
    {
        foreach (var service in Enumerable.Reverse(_services))
            service.Dispose();
        _services.Clear();
    }
}
