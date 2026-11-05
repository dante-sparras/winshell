namespace Winshell.Session;

/// <summary>
/// The signed-in Windows session the shell runs in. <see cref="Lock"/> asks Windows to lock it, and Windows' own lock
/// screen takes over. Winshell never draws a lock screen or asks for the Windows password.
/// </summary>
public sealed class SessionService
{
    private readonly Log _log;
    private readonly ISessionBackend _backend;

    internal SessionService(Log log, ISessionBackend? backend = null)
    {
        _log = log;
        _backend = backend ?? new SessionBackend();
    }

    /// <summary>
    /// Asks Windows to lock the session, as Win+L does. Returns whether Windows took the request; the lock itself
    /// happens a moment later, in Windows.
    /// </summary>
    public bool Lock()
    {
        if (_backend.Lock())
            return true;
        _log.Warn("session", "Windows did not lock the session.");
        return false;
    }
}
