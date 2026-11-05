using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>
/// Application user model ids, the ids Windows groups taskbar buttons by: the one a window sets, and the one a
/// packaged process has.
/// </summary>
internal static unsafe class AppUserModelIds
{
    /// <summary>
    /// Characters, including the terminating null, that an application user model id can hold.
    /// <c>APPLICATION_USER_MODEL_ID_MAX_LENGTH</c>.
    /// </summary>
    internal const int MaxCharacters = (int)PInvoke.APPLICATION_USER_MODEL_ID_MAX_LENGTH;

    /// <summary>System.AppUserModel.ID, the property a window sets.</summary>
    private static readonly PROPERTYKEY Key = new(PInvoke.PKEY_AppUserModel_ID.fmtid, PInvoke.PKEY_AppUserModel_ID.pid);

    /// <summary>One <c>GetApplicationUserModelId</c> call. <see cref="Query.Length"/> counts wide characters and includes the terminating null.</summary>
    internal readonly record struct Query(WIN32_ERROR Code, uint Length);

    /// <summary>The id the window set in its property store, or empty when it set none.</summary>
    internal static string OfWindow(nint hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        void* pointer = null;
        if (PInvoke.SHGetPropertyStoreForWindow((HWND)hwnd, &iid, &pointer).Failed || pointer is null)
            return "";
        try
        {
            var store = (IPropertyStore)Marshal.GetObjectForIUnknown((nint)pointer);
            try
            {
                return PropertyStore.ReadString(store, Key) ?? "";
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return "";
        }
        finally
        {
            Marshal.Release((nint)pointer);
        }
    }

    /// <summary>
    /// The id of a process's package, or an explicit id that process set. Empty when the process has none or cannot
    /// be opened. <paramref name="processId"/> zero is empty.
    /// </summary>
    internal static string OfProcess(uint processId)
    {
        if (processId == 0)
            return "";
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
            return "";
        try
        {
            return Read((buffer, capacity) =>
            {
                var length = (uint)capacity;
                fixed (char* text = buffer)
                    return new Query(PInvoke.GetApplicationUserModelId(new HANDLE(process), &length, text), length);
            });
        }
        finally
        {
            Win32.CloseHandle(process);
        }
    }

    /// <summary>
    /// The id <paramref name="query"/> reports, or empty when the process has none or the answer does not fit. The
    /// first call asks for the size with no buffer. The second fills a buffer of that size.
    /// </summary>
    internal static string Read(Func<char[]?, int, Query> query)
    {
        var sized = query(null, 0);
        if (sized.Code != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || sized.Length <= 1 || sized.Length > MaxCharacters)
            return "";
        var buffer = new char[sized.Length];
        var read = query(buffer, buffer.Length);
        if (read.Code != WIN32_ERROR.ERROR_SUCCESS || read.Length <= 1 || read.Length > (uint)buffer.Length)
            return "";
        return new string(buffer, 0, (int)read.Length - 1);
    }
}
