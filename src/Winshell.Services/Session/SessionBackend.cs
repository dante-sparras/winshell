using Winshell.Native;

namespace Winshell.Session;

internal sealed class SessionBackend : ISessionBackend
{
    public bool Lock() => Workstation.Lock();
}
