using System.Security.Cryptography;
using System.Text;

namespace Winshell;

/// <summary>
/// Session mutex for one shell. <c>--no-duplicate</c> holds it for the life of the shell, so two starts that both pass
/// the instance list, because neither has written it yet, cannot both keep running. Owned until disposed, on the thread
/// that claimed it. A failed claim owns nothing, and that start exits.
/// </summary>
internal readonly struct DuplicateHold : IDisposable
{
    private readonly Mutex? _mutex;

    private DuplicateHold(Mutex mutex) => _mutex = mutex;

    public bool Acquired => _mutex is not null;

    /// <summary>The mutex for a <c>shell.qml</c>. The name is a hash so a long path still fits, and the hash ignores case so Windows paths match.</summary>
    public static string ConfigMutexName(string configPath) =>
        @"Local\winshell-" + Hash(Path.GetFullPath(configPath));

    /// <summary>
    /// The mutex for a configuration's program, which is one shell by its shell id, wherever it was built. An id means
    /// that shell only in one local folder, whose instances are the ones it can see.
    /// </summary>
    public static string ShellMutexName(string shellId) => @"Local\winshell-shell-" + Hash(ShellLocations.LocalRoot + "\n" + shellId);

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToUpperInvariant())));

    /// <summary>Claims <paramref name="mutexName"/>, unless <paramref name="isSameShell"/> matches a running instance.</summary>
    public static DuplicateHold TryClaim(string mutexName, Func<InstanceInfo, bool> isSameShell)
    {
        var mutex = new Mutex(false, mutexName);
        bool mine;
        try
        {
            mine = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The previous shell crashed while it held this. The wait succeeded and this start owns it.
            mine = true;
        }
        if (!mine)
        {
            mutex.Dispose();
            return default;
        }
        // A shell started without --no-duplicate holds no mutex. Its instance.json is the only record of it.
        if (Instances.List(false).Any(i => isSameShell(i.Info)))
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            return default;
        }
        return new DuplicateHold(mutex);
    }

    public void Dispose()
    {
        if (_mutex is null)
            return;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
