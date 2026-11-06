using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;
using Microsoft.Win32.SafeHandles;

namespace Winshell.Native;

internal static class RegistryChange
{
    /// <summary>
    /// Asks Windows to set <paramref name="signal"/> once when a key is added, removed or renamed, or a value is
    /// written, anywhere under <paramref name="key"/>. The request outlives the thread that made it. False when
    /// Windows refused it.
    /// </summary>
    internal static bool Notify(SafeRegistryHandle key, SafeWaitHandle signal)
    {
        var keyAdded = false;
        var signalAdded = false;
        try
        {
            key.DangerousAddRef(ref keyAdded);
            signal.DangerousAddRef(ref signalAdded);
            const REG_NOTIFY_FILTER filter = REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_NAME |
                                             REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET |
                                             REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC;
            return PInvoke.RegNotifyChangeKeyValue((HKEY)key.DangerousGetHandle(), true, filter,
                (HANDLE)signal.DangerousGetHandle(), true) == WIN32_ERROR.NO_ERROR;
        }
        finally
        {
            if (signalAdded)
                signal.DangerousRelease();
            if (keyAdded)
                key.DangerousRelease();
        }
    }
}
