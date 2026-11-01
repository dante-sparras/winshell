using System.Runtime.InteropServices;

namespace Winshell.Session;

/// <summary>
/// The signed-in Windows session the shell runs in. <see cref="Lock"/> asks Windows to lock it, and Windows' own lock
/// screen takes over. Winshell never draws a lock screen or asks for the Windows password (ADR-0008).
/// </summary>
public sealed class SessionService
{
    private readonly Log _log;
    private readonly IWorkstationLock _locker;

    internal SessionService(Log log, IWorkstationLock? locker = null)
    {
        _log = log;
        _locker = locker ?? WindowsWorkstationLock.Instance;
    }

    /// <summary>
    /// Asks Windows to lock the session, as Win+L does. Returns whether Windows took the request; the lock itself
    /// happens a moment later, in Windows.
    /// </summary>
    public bool Lock()
    {
        if (_locker.Lock())
            return true;
        _log.Warn("session", "Windows did not lock the session.");
        return false;
    }
}

/// <summary>Locks the Windows session. Tests use a fake, so they never lock the PC.</summary>
internal interface IWorkstationLock
{
    bool Lock();
}

internal sealed partial class WindowsWorkstationLock : IWorkstationLock
{
    public static readonly WindowsWorkstationLock Instance = new();

    public bool Lock() => LockWorkStation();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();
}
