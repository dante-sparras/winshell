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

// The shell's services. Shell's summary is in Shell.cs.
public sealed partial class Shell
{
    private readonly List<IDisposable> _services = [];
    private AppsService? _apps;
    private AudioGraph? _audioGraph;
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

    // The graph is owned first, so it is disposed after the service whose peak meters read it.
    /// <summary>Playback and recording devices, and the audio sessions on them.</summary>
    public AudioService Audio => Own(ref _audio,
        () => new AudioService(Own(ref _audioGraph, () => new AudioGraph(Loop, Log)), Log));

    /// <summary>The Bluetooth adapter and the paired and nearby devices.</summary>
    public BluetoothService Bluetooth => Own(ref _bluetooth, () => new BluetoothService(Loop, Log));

    /// <summary>The brightness of the connected monitors that report one.</summary>
    public BrightnessService Brightness => Own(ref _brightness, () => new BrightnessService(Loop, Log));

    /// <summary>Whether Explorer's taskbar is hidden. Winshell shows it again when it exits.</summary>
    public ExplorerTaskbarService ExplorerTaskbar => _explorerTaskbar ??=
        new ExplorerTaskbarService(Own(ref _explorerTaskbarSession, ExplorerTaskbarSession.ForThisProcess));

    /// <summary>The recent and frequent documents of an app, as its jump list shows them.</summary>
    public JumpListService JumpLists => _jumpLists ??= new JumpListService();

    /// <summary>The media sessions Windows lets the shell see and control.</summary>
    public MediaService Media => Own(ref _media, () => new MediaService(Loop, Log));

    /// <summary>The network adapters, connection profiles, radios and connectivity.</summary>
    public NetworkingService Networking => Own(ref _networking, () => new NetworkingService(Loop));

    /// <summary>Windows Notification Center. Reading this never prompts for access.</summary>
    public NotificationListener Notifications => Own(ref _notifications, () => new NotificationListener(Loop));

    /// <summary>The batteries of this PC.</summary>
    public PowerService Power => Own(ref _power, () => new PowerService(Loop));

    /// <summary>The Windows power mode: best power efficiency, balanced, or best performance.</summary>
    public PowerModeService PowerMode => Own(ref _powerMode, () => new PowerModeService(Loop, Log));

    /// <summary>The documents in the user's Recent folder, found by name.</summary>
    public RecentDocuments RecentDocuments => _recentDocuments ??= new RecentDocuments();

    /// <summary>The Windows session: locking it, and what state it is in.</summary>
    public SessionService Session => _session ??= new SessionService(Log);

    /// <summary>Whether this shell runs when the user signs in.</summary>
    public StartupService Startup => _startup ??= StartupService.ForThisProcess(Log);

    /// <summary>The notification area. Only one running instance in the Windows session owns it.</summary>
    public SystemTray SystemTray => Own(ref _systemTray, () => new SystemTray(Loop, Log));

    /// <summary>Winshell's own updates. A configuration's own program does not update itself.</summary>
    public UpdatesService Updates => Own(ref _updates, () => new UpdatesService(Loop, Log, Updater));

    /// <summary>The virtual desktops Task View shows.</summary>
    public VirtualDesktopsService VirtualDesktops =>
        Own(ref _virtualDesktops, () => new VirtualDesktopsService(Loop, Log));

    /// <summary>The app windows the taskbar and Alt+Tab list.</summary>
    public WindowListService WindowList => Own(ref _windows, () =>
    {
        var list = new WindowListService(Loop, Log, VirtualDesktops);
        _winland?.Attach(list);
        return list;
    });

    /// <summary>
    /// The Winland window manager's Monitors, Workspaces and windows. The lists are empty while it is not running.
    /// </summary>
    public WinlandService Winland => Own(ref _winland, () =>
    {
        var service = new WinlandService(Loop, Log);
        if (_windows is not null)
            service.Attach(_windows);
        return service;
    });

    /// <summary>What <see cref="Updates"/> follows. <see cref="Run{T}"/> copies it from <see cref="ShellOptions.Updater"/>.</summary>
    internal Updater? Updater { get; set; }

    /// <summary>Winshell's own notices. Set as the shell starts.</summary>
    internal ShellNotices? Notices { get; set; }

    /// <summary>A hotkey that works anywhere in Windows. Set its <see cref="Hotkey.Sequence"/>, and dispose it to give the keys back.</summary>
    public Hotkey NewHotkey()
    {
        return new Hotkey(Loop);
    }

    /// <summary>Tells when there has been no input for a while.</summary>
    public IdleMonitor NewIdleMonitor()
    {
        return new IdleMonitor(Loop);
    }

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

    private void DisposeServices()
    {
        foreach (var service in Enumerable.Reverse(_services))
            service.Dispose();
        _services.Clear();
    }
}
