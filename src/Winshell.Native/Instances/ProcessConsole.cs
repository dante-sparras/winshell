using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Console;

namespace Winshell.Native;

internal enum StandardStream
{
    Input,
    Output,
    Error,
}

/// <summary>This process's console window and standard handles.</summary>
internal static class ProcessConsole
{
    /// <summary>The console window, or 0 when no console is attached.</summary>
    internal static nint Window() => PInvoke.GetConsoleWindow();

    /// <summary>The handle: 0 when the process has none, and -1 when Windows could not say.</summary>
    internal static nint StandardHandle(StandardStream stream) => PInvoke.GetStdHandle(stream switch
    {
        StandardStream.Input => STD_HANDLE.STD_INPUT_HANDLE,
        StandardStream.Output => STD_HANDLE.STD_OUTPUT_HANDLE,
        _ => STD_HANDLE.STD_ERROR_HANDLE,
    });

    /// <summary>A <c>FILE_TYPE_*</c> value: what a handle refers to.</summary>
    internal static uint FileType(nint handle) => (uint)PInvoke.GetFileType(new HANDLE(handle));

    /// <summary>Stops processes started from now on from inheriting <paramref name="handle"/>.</summary>
    internal static bool KeepFromChildren(nint handle) =>
        PInvoke.SetHandleInformation(new HANDLE(handle), (uint)HANDLE_FLAGS.HANDLE_FLAG_INHERIT, 0);
}
