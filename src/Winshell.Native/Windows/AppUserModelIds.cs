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
                return PropertyStore.ReadString(store, PropertyStore.AppUserModelId) ?? "";
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
        using var process = ProcessHandle.Open(processId);
        if (!process.IsOpen)
            return "";
        var handle = process.Handle;
        return Read(buffer =>
        {
            var length = (uint)buffer.Length;
            fixed (char* text = buffer)
            {
                return new Query(PInvoke.GetApplicationUserModelId(handle, &length, text), length);
            }
        });
    }

    /// <summary>
    /// The id <paramref name="query"/> reports, or empty when the process has none or the answer does not fit. The
    /// first call asks for the size with an empty buffer. The second fills a buffer of that size.
    /// </summary>
    internal static string Read(Func<char[], Query> query)
    {
        return TextBuffer.Read(0, MaxCharacters, buffer =>
        {
            var answer = query(buffer);
            if (answer.Code != WIN32_ERROR.ERROR_SUCCESS)
                return new TextBuffer.Answer(answer.Code, (int)answer.Length);
            // The length counts the terminating null, which must fit too.
            return new TextBuffer.Answer(answer.Code,
                answer.Length > (uint)buffer.Length ? -1 : (int)answer.Length - 1);
        }) ?? "";
    }
}
