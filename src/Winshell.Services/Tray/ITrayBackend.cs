namespace Winshell.Tray;

/// <summary>
/// What <see cref="SystemTray"/> needs of Windows: the window apps send their notification area icons to, which passes
/// every message on to Explorer's taskbar, and the windows, processes and pointer of the apps behind the icons. The
/// tray decides when to own the window, keep it in front and ask apps again. The signals are raised on the loop's
/// thread while the window is open.
/// </summary>
internal interface ITrayBackend : IDisposable
{
    /// <summary>Someone asked every app to add its icons again: Explorer with a new taskbar, this tray or another.</summary>
    event Action? TaskbarCreated;

    /// <summary>Explorer's taskbar came in front of the window.</summary>
    event Action? ExplorerInFront;

    /// <summary>Windows changed which icons the taskbar shows; <see cref="ReadSettings"/> has the new choices.</summary>
    event Action? SettingsChanged;

    /// <summary>Explorer's own taskbar window, or 0 when there is none.</summary>
    nint Explorer { get; }

    /// <summary>Takes the session's notification area, which one instance at a time has. False while another has it.</summary>
    bool TryOwn();

    /// <summary>Opens the window apps send their icons to. False when Windows would not make it.</summary>
    /// <param name="apply">Answers each icon call while the app waits: false when the app changed an icon it never added.</param>
    bool Open(Func<NotifyIconCall, bool> apply);

    /// <summary>Closes the window, if it is open, and gives the notification area up, if this has it.</summary>
    void Close();

    /// <summary>Puts the window in front of Explorer's taskbar, over its rectangle.</summary>
    void Raise();

    /// <summary>Puts the window behind Explorer's taskbar. False when the window is closed or there is no Explorer.</summary>
    bool Lower();

    /// <summary>Asks every app to add its icons again, to whichever taskbar window is first.</summary>
    void Broadcast();

    /// <summary>Windows' choices for which icons the taskbar shows.</summary>
    IReadOnlyList<TrayIconSetting> ReadSettings();

    /// <summary>Draws an app's icon handle, and finds the resource it was loaded from, while the app still has it.</summary>
    (PixelImage? Image, IconResource? Resource) ReadIcon(nint icon);

    /// <summary>The process that owns a window, or 0 when the window is gone.</summary>
    uint ProcessOf(nint window);

    /// <summary>The full path of the program whose process owns a window, or empty when it cannot be read.</summary>
    string ProgramOf(nint window);

    /// <summary>A process's integrity level, or null when it cannot be read.</summary>
    uint? IntegrityOf(uint processId);

    /// <summary>The pointer's position on the virtual desktop, in physical pixels.</summary>
    (int X, int Y) Cursor { get; }

    /// <summary>Moves the pointer to a point on the virtual desktop, in physical pixels.</summary>
    void MoveCursor((int X, int Y) point);

    /// <summary>
    /// Lets the app with this window and process take the foreground for its menu or window. Nothing is activated for
    /// it, so the window the user works in keeps the keyboard when the app shows nothing.
    /// </summary>
    void LetAppTakeForeground(nint window, uint processId);

    /// <summary>Sends an icon's callback message to the app's window without waiting for it.</summary>
    void Notify(nint window, uint message, nint wParam, nint lParam);
}
