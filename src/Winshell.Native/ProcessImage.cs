using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;

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

    /// <summary>
    /// The image of the nearest ancestor of this process that <paramref name="matches"/>, walking from its parent, or
    /// null when none within eight parents does.
    /// </summary>
    internal static string? Ancestor(Func<string, bool> matches)
    {
        try
        {
            var id = (uint)Environment.ProcessId;
            var seen = new HashSet<uint>();
            for (var depth = 0; depth < 8 && seen.Add(id); depth++)
            {
                if (ParentOf(id) is not uint parent || parent == 0 || parent == id)
                    return null;
                if (TryRead(parent) is { } image && matches(image))
                    return image;
                id = parent;
            }

            return null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

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
            if (attempt == 1 || result.Error != Win32.ERROR_INSUFFICIENT_BUFFER || result.Size <= capacity ||
                result.Size > MaxCharacters)
                return null;
            capacity = result.Size;
        }

        return null;
    }

    /// <summary>The parent process id, or null when the snapshot cannot be read or the process is not in it.</summary>
    internal static uint? ParentOf(uint processId)
    {
        var snapshot = PInvoke.CreateToolhelp32Snapshot(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot == new HANDLE(-1))
            return null;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            for (var more = PInvoke.Process32FirstW(snapshot, ref entry);
                 more;
                 more = PInvoke.Process32NextW(snapshot, ref entry))
                if (entry.th32ProcessID == processId)
                    return entry.th32ParentProcessID;
            return null;
        }
        finally
        {
            PInvoke.CloseHandle(snapshot);
        }
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
