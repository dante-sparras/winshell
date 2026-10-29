using System.Runtime.InteropServices;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Session;

/// <summary>
/// The <c>Session</c>: the signed-in Windows session the shell runs in. <c>lock()</c> asks Windows to lock it, and
/// Windows' own lock screen takes over. Winshell never draws a lock screen or asks for the Windows password (ADR-0008).
/// </summary>
public sealed class SessionService : QObject
{
    private readonly QmlEngine _engine;

    internal SessionService(QmlEngine engine) => _engine = engine;

    /// <summary>
    /// Asks Windows to lock the session, as Win+L does. Returns whether Windows took the request; the lock itself
    /// happens a moment later, in Windows.
    /// </summary>
    [Qml]
    public bool Lock()
    {
        var locker = _engine.GetService<IWorkstationLock>();
        if (locker is null)
        {
            ServiceSupport.Warn(this, "session", "Session.lock() needs a shell running on the Windows desktop.");
            return false;
        }
        if (locker.Lock())
            return true;
        ServiceSupport.Warn(this, "session", "Windows did not lock the session.");
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

internal static class SessionModule
{
    public static void Register(ModuleRegistry registry) =>
        registry.Module("Winshell")
            .Singleton(e => new SessionService(e), "Session")
            .Singleton(e => new StartupService(e), "Startup");
}
