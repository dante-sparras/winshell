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
using Winshell.Windows;
using Winshell.Winland;

namespace Winshell;

// The shell's services. Shell's summary is in Shell.cs.
public sealed partial class Shell
{
    private AppsService? _apps;
    private AudioService? _audio;
    private BluetoothService? _bluetooth;
    private BrightnessService? _brightness;
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
    private VirtualDesktopsService? _virtualDesktops;
    private WindowListService? _windows;
    private WinlandService? _winland;

    /// <summary>The apps Start lists.</summary>
    public AppsService Apps => _apps ??= new AppsService(Loop, Log);

    /// <summary>Playback and recording devices, and the audio sessions on them.</summary>
    public AudioService Audio => _audio ??= new AudioService(Loop, Log);

    /// <summary>The Bluetooth adapter and the paired and nearby devices.</summary>
    public BluetoothService Bluetooth => _bluetooth ??= new BluetoothService(Loop, Log);

    /// <summary>The brightness of the connected monitors that report one.</summary>
    public BrightnessService Brightness => _brightness ??= new BrightnessService(Loop, Log);

    /// <summary>Whether Explorer's taskbar is hidden. Winshell shows it again when it exits.</summary>
    public ExplorerTaskbarService ExplorerTaskbar => _explorerTaskbar ??= new ExplorerTaskbarService(Loop, Log);

    /// <summary>The recent and frequent documents of an app, as its jump list shows them.</summary>
    public JumpListService JumpLists => _jumpLists ??= new JumpListService();

    /// <summary>The media sessions Windows lets the shell see and control.</summary>
    public MediaService Media => _media ??= new MediaService(Loop, Log);

    /// <summary>The network adapters, connection profiles, radios and connectivity.</summary>
    public NetworkingService Networking => _networking ??= new NetworkingService(Loop);

    /// <summary>Windows Notification Center. Reading this never prompts for access.</summary>
    public NotificationListener Notifications => _notifications ??= new NotificationListener(Loop, Log);

    /// <summary>The batteries of this PC.</summary>
    public PowerService Power => _power ??= new PowerService(Loop);

    /// <summary>The Windows power mode: best power efficiency, balanced, or best performance.</summary>
    public PowerModeService PowerMode => _powerMode ??= new PowerModeService(Loop, Log);

    /// <summary>The documents in the user's Recent folder, found by name.</summary>
    public RecentDocuments RecentDocuments => _recentDocuments ??= new RecentDocuments();

    /// <summary>The Windows session: locking it, and what state it is in.</summary>
    public SessionService Session => _session ??= new SessionService(Log);

    /// <summary>Whether this shell runs when the user signs in.</summary>
    public StartupService Startup => _startup ??= StartupService.ForThisProcess(Log);

    /// <summary>The notification area. Only one running instance in the Windows session owns it.</summary>
    public SystemTray SystemTray => _systemTray ??= new SystemTray(Loop, Log);

    /// <summary>The virtual desktops Task View shows.</summary>
    public VirtualDesktopsService VirtualDesktops => _virtualDesktops ??= new VirtualDesktopsService(Loop, Log);

    /// <summary>The app windows the taskbar and Alt+Tab list.</summary>
    public WindowListService WindowList =>
        _windows ?? Connect(_windows = new WindowListService(Loop, Log, VirtualDesktops));

    /// <summary>
    /// The Winland window manager's Monitors, Workspaces and windows. The lists are empty while it is not running.
    /// </summary>
    public WinlandService Winland => _winland ?? Connect(_winland = new WinlandService(Loop, Log));

    /// <summary>A hotkey that works anywhere in Windows. Set its <see cref="Hotkey.Sequence"/>, and dispose it to give the keys back.</summary>
    public Hotkey NewHotkey()
    {
        return new Hotkey(Loop);
    }

    /// <summary>Tells when there has been no input for a while.</summary>
    public IdleMonitor NewIdleMonitor()
    {
        return new IdleMonitor(Loop, Log);
    }

    /// <summary>Winland resolves its windows from the window list once both are in use. Neither is made for the other.</summary>
    private T Connect<T>(T made)
    {
        _winland?.Attach(_windows);
        return made;
    }

    /// <summary>
    /// A service goes before the ones it reads: Winland reads the window list, which reads the virtual desktops.
    /// Explorer's taskbar may go before or after the tray: its restore moves the tray out of the way itself.
    /// </summary>
    private void DisposeServices()
    {
        IDisposable?[] services =
        [
            _winland, _windows, _virtualDesktops, _apps, _audio, _bluetooth, _brightness, _explorerTaskbar, _media,
            _networking, _notifications, _power, _powerMode, _systemTray
        ];
        foreach (var service in services)
            service?.Dispose();
    }
}
