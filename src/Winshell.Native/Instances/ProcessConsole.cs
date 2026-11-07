using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Console;

namespace Winshell.Native;

internal enum StandardStream
{
    Input,
    Output,
    Error
}

/// <summary>Where a standard stream goes.</summary>
internal enum StreamTarget
{
    /// <summary>No handle, a console, or something Windows does not name.</summary>
    Other,
    File,
    Pipe
}

/// <summary>This process's console window and standard handles.</summary>
internal static class ProcessConsole
{
    /// <summary>The console window, or 0 when no console is attached.</summary>
    internal static nint Window()
    {
        return PInvoke.GetConsoleWindow();
    }

    /// <summary>Where <paramref name="stream"/> goes. A file or pipe on another computer is a file or pipe.</summary>
    internal static StreamTarget Target(StandardStream stream)
    {
        if (Handle(stream) is not { } handle)
            return StreamTarget.Other;
        return (PInvoke.GetFileType(handle) & ~FILE_TYPE.FILE_TYPE_REMOTE) switch
        {
            FILE_TYPE.FILE_TYPE_DISK => StreamTarget.File,
            FILE_TYPE.FILE_TYPE_PIPE => StreamTarget.Pipe,
            _ => StreamTarget.Other
        };
    }

    /// <summary>Stops processes started from now on from inheriting <paramref name="stream"/>, when it has a handle.</summary>
    internal static void KeepFromChildren(StandardStream stream)
    {
        if (Handle(stream) is { } handle)
            _ = PInvoke.SetHandleInformation(handle, (uint)HANDLE_FLAGS.HANDLE_FLAG_INHERIT, 0);
    }

    /// <summary>Null when the process has no such handle (0) or Windows could not say (-1).</summary>
    private static HANDLE? Handle(StandardStream stream)
    {
        var handle = PInvoke.GetStdHandle(stream switch
        {
            StandardStream.Input => STD_HANDLE.STD_INPUT_HANDLE,
            StandardStream.Output => STD_HANDLE.STD_OUTPUT_HANDLE,
            _ => STD_HANDLE.STD_ERROR_HANDLE
        });
        return (nint)handle is 0 or -1 ? null : handle;
    }
}
