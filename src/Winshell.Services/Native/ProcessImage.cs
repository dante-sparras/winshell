using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// The full path of a process image. <c>QueryFullProcessImageNameW</c> reports
/// <c>ERROR_INSUFFICIENT_BUFFER</c> and the required size when the first buffer is short, so a path past 1024
/// characters is read on a second call, up to the Win32 limit.
/// </summary>
internal static partial class ProcessImage
{
    /// <summary>Characters, including the terminating null, that a Win32 path can hold.</summary>
    internal const int MaxCharacters = 32_767;

    internal const int InsufficientBuffer = 122;

    private const int FirstCapacity = 1024;

    private const uint QueryLimitedInformation = 0x1000;

    internal readonly record struct QueryResult(bool Ok, int Size, int Error);

    /// <summary>The image path, or null when the process cannot be opened or the name cannot be read.</summary>
    internal static string? TryRead(uint processId)
    {
        var process = Win32.OpenProcess(QueryLimitedInformation, false, processId);
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
            if (attempt == 1 || result.Error != InsufficientBuffer || result.Size <= capacity || result.Size > MaxCharacters)
                return null;
            capacity = result.Size;
        }

        return null;
    }

    private static QueryResult Query(nint process, char[] buffer, int capacity)
    {
        var size = capacity;
        var ok = QueryFullProcessImageNameW(process, 0, buffer, ref size);
        return new QueryResult(ok, size, ok ? 0 : Marshal.GetLastPInvokeError());
    }

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(nint process, uint flags, [Out] char[] name, ref int size);
}
