using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>
/// The full path of a process image. <c>QueryFullProcessImageNameW</c> reports
/// <c>ERROR_INSUFFICIENT_BUFFER</c> and the required size when the first buffer is short, so a path past 1024
/// characters is read on a second call, up to the Win32 limit.
/// </summary>
internal static class ProcessImage
{
    /// <summary>Characters, including the terminating null, that a Win32 path can hold.</summary>
    internal const int MaxCharacters = 32_767;

    private const int FirstCapacity = 1024;

    internal readonly record struct QueryResult(bool Ok, int Size, int Error);

    /// <summary>The image path, or null when the process cannot be opened or the name cannot be read.</summary>
    internal static string? TryRead(uint processId)
    {
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
            return null;
        try
        {
            return Read((buffer, capacity) => Query(process, buffer, capacity));
        }
        finally
        {
            Win32.CloseHandle(process);
        }
    }

    /// <summary>Grows once when <paramref name="query"/> asks for a larger buffer. A second failure returns null.</summary>
    internal static string? Read(Func<char[], int, QueryResult> query)
    {
        var capacity = FirstCapacity;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var buffer = new char[capacity];
            var result = query(buffer, capacity);
            if (result.Ok)
                return (uint)result.Size > (uint)buffer.Length ? null : new string(buffer, 0, result.Size);
            if (attempt == 1 || result.Error != Win32.ERROR_INSUFFICIENT_BUFFER || result.Size <= capacity || result.Size > MaxCharacters)
                return null;
            capacity = result.Size;
        }

        return null;
    }

    private static unsafe QueryResult Query(nint process, char[] buffer, int capacity)
    {
        var size = (uint)capacity;
        fixed (char* name = buffer)
        {
            var ok = PInvoke.QueryFullProcessImageName(new HANDLE(process), 0, name, &size);
            var error = ok ? 0 : Marshal.GetLastPInvokeError();
            return new QueryResult(ok, size > int.MaxValue ? -1 : (int)size, error);
        }
    }
}
