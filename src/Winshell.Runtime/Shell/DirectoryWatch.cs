using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

#pragma warning disable CA1416 // Winshell only runs on Windows; this project targets plain net10.0.

namespace Winshell.Runtime.Shell;

/// <summary>
/// Watches one folder on a thread of its own. FileSystemWatcher delivers on thread pool workers, so a busy pool
/// holds a change back. The first read starts before <see cref="Start"/> returns, so a change made after that is
/// not missed. Callbacks run on the watch thread and must not block.
/// </summary>
internal sealed class DirectoryWatch : IDisposable
{
    // FILE_NOTIFY_CHANGE_FILE_NAME | SIZE | LAST_WRITE.
    public const uint FileChanges = 0x00000001 | 0x00000008 | 0x00000010;
    // FILE_NOTIFY_CHANGE_FILE_NAME | DIR_NAME.
    public const uint NameChanges = 0x00000001 | 0x00000002;

    private const int BufferSize = 16 * 1024;
    private const uint FileListDirectory = 1;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOverlapped = 0x40000000;

    private readonly SafeFileHandle _handle;
    private readonly uint _filter;
    private readonly Action<string> _changed;
    private readonly Action _lost;
    private readonly ManualResetEvent _completed = new(false);
    private readonly nint _buffer = Marshal.AllocHGlobal(BufferSize);
    private readonly nint _overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
    private readonly Thread _thread;
    private volatile bool _stopped;

    private DirectoryWatch(SafeFileHandle handle, uint filter, Action<string> changed, Action lost)
    {
        _handle = handle;
        _filter = filter;
        _changed = changed;
        _lost = lost;
        Marshal.StructureToPtr(new NativeOverlapped { EventHandle = _completed.SafeWaitHandle.DangerousGetHandle() }, _overlapped, false);
        _thread = new Thread(Run) { IsBackground = true, Name = "Directory watch" };
    }

    /// <summary>Starts watching, or returns null when the folder can't be opened.</summary>
    /// <param name="path">The folder.</param>
    /// <param name="filter"><see cref="FileChanges"/> or <see cref="NameChanges"/>.</param>
    /// <param name="changed">Called with the name of each thing that changed, relative to the folder.</param>
    /// <param name="lost">Called once when the folder went away or changes were dropped. Nothing is reported after it.</param>
    public static DirectoryWatch? Start(string path, uint filter, Action<string> changed, Action lost)
    {
        var handle = CreateFileW(path, FileListDirectory, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open,
            FileFlagBackupSemantics | FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        var watch = new DirectoryWatch(handle, filter, changed, lost);
        if (!watch.Read())
        {
            watch.Dispose();
            return null;
        }
        watch._thread.Start();
        return watch;
    }

    private bool Read() => ReadDirectoryChangesW(_handle, _buffer, BufferSize, false, _filter, IntPtr.Zero, _overlapped, IntPtr.Zero);

    private void Run()
    {
        while (true)
        {
            var ok = GetOverlappedResult(_handle, _overlapped, out var returned, true);
            if (_stopped)
                return;
            // A failed read is a folder that went away. An empty one is a buffer that overflowed.
            if (!ok || returned == 0)
            {
                _lost();
                return;
            }
            Report(returned);
            if (!Read())
            {
                _lost();
                return;
            }
        }
    }

    private void Report(int count)
    {
        var offset = 0;
        while (offset + 12 <= count)
        {
            var next = Marshal.ReadInt32(_buffer, offset);
            var nameBytes = Marshal.ReadInt32(_buffer, offset + 8);
            if ((uint)nameBytes > (uint)(count - offset - 12))
                return;
            _changed(Marshal.PtrToStringUni(_buffer + offset + 12, nameBytes / sizeof(char)));
            if (next <= 0)
                return;
            offset += next;
        }
    }

    public void Dispose()
    {
        _stopped = true;
        if (_thread.ThreadState != ThreadState.Unstarted)
        {
            // Cancel until the thread ends. It may be between reads, where one cancel would miss it.
            do
            {
                CancelIoEx(_handle, IntPtr.Zero);
            }
            while (!_thread.Join(1));
        }
        _handle.Dispose();
        _completed.Dispose();
        Marshal.FreeHGlobal(_buffer);
        Marshal.FreeHGlobal(_overlapped);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadDirectoryChangesW(
        SafeFileHandle directory, nint buffer, int bufferLength, bool watchSubtree, uint filter, nint bytesReturned,
        nint overlapped, nint completion);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle file, nint overlapped, out int bytes, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle file, nint overlapped);
}
