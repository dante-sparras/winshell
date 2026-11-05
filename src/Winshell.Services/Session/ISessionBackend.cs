namespace Winshell.Session;

/// <summary>What <see cref="SessionService"/> asks Windows. Tests use a fake, so they never lock the PC.</summary>
internal interface ISessionBackend
{
    /// <summary>Asks Windows to lock the session. Returns whether Windows took the request.</summary>
    bool Lock();
}
