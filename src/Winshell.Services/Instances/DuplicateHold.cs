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

    private DuplicateHold(Mutex mutex)
    {
        _mutex = mutex;
    }

    public bool Acquired => _mutex is not null;

    /// <summary>
    /// The mutex for a configuration's program, which is one shell by its shell id, wherever it was built. An id means
    /// that shell only in one local folder, whose instances are the ones it can see.
    /// </summary>
    public static string ShellMutexName(ShellLocations locations, string shellId)
    {
        return @"Local\winshell-shell-" + Hash(locations.LocalRoot + "\n" + shellId);
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToUpperInvariant())));
    }

    /// <summary>Claims the mutex of <paramref name="shellId"/>, unless a running instance already has that shell id.</summary>
    public static DuplicateHold TryClaim(ShellLocations locations, string shellId)
    {
        var mutex = new Mutex(false, ShellMutexName(locations, shellId));
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
        if (locations.Instances.List(false)
            .Any(i => string.Equals(i.Info.ShellId, shellId, StringComparison.OrdinalIgnoreCase)))
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
