using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace Winshell.Native;

/// <summary>What a <see cref="DirectoryChanges"/> reports.</summary>
[Flags]
internal enum DirectoryChange : uint
{
    /// <summary>A file was created, deleted or renamed.</summary>
    FileName = FILE_NOTIFY_CHANGE.FILE_NOTIFY_CHANGE_FILE_NAME,

    /// <summary>A folder was created, deleted or renamed.</summary>
    FolderName = FILE_NOTIFY_CHANGE.FILE_NOTIFY_CHANGE_DIR_NAME,

    /// <summary>A file's size changed.</summary>
    Size = FILE_NOTIFY_CHANGE.FILE_NOTIFY_CHANGE_SIZE,

    /// <summary>A file's last write time changed.</summary>
    LastWrite = FILE_NOTIFY_CHANGE.FILE_NOTIFY_CHANGE_LAST_WRITE
}

/// <summary>
/// One folder opened for overlapped <c>ReadDirectoryChangesW</c>, with its buffer and completion event. Windows
/// cancels a read when the thread that made it ends, so one thread makes every <see cref="Read"/> and
/// <see cref="Wait"/>. <see cref="Cancel"/> may come from any thread. Dispose only after the reading thread has
/// stopped: an outstanding read writes into the buffer.
/// </summary>
internal sealed unsafe class DirectoryChanges : IDisposable
{
    private const int BufferSize = 16 * 1024;

    private const uint FileListDirectory = 1;

    // NextEntryOffset, Action and FileNameLength come before the name.
    private const int HeaderSize = 3 * sizeof(uint);

    private readonly SafeFileHandle _handle;
    private readonly DirectoryChange _filter;
    private readonly bool _subtree;
    private readonly ManualResetEvent _completed = new(false);
    private readonly byte* _buffer = (byte*)NativeMemory.Alloc(BufferSize);

    private readonly NativeOverlapped* _overlapped =
        (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));

    private int _disposed;

    private DirectoryChanges(SafeFileHandle handle, DirectoryChange filter, bool subtree)
    {
        _handle = handle;
        _filter = filter;
        _subtree = subtree;
        _overlapped->EventHandle = _completed.SafeWaitHandle.DangerousGetHandle();
    }

    /// <summary>Opens <paramref name="path"/>, or returns null when the folder can't be opened.</summary>
    /// <param name="filter">What to report.</param>
    /// <param name="subtree">Also report changes in folders under <paramref name="path"/>.</param>
    internal static DirectoryChanges? Open(string path, DirectoryChange filter, bool subtree)
    {
        var handle = Win32.CreateFileW(path, FileListDirectory, (uint)(FileShare.ReadWrite | FileShare.Delete), 0,
            (uint)FileMode.Open,
            (uint)FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS | Win32.FILE_FLAG_OVERLAPPED, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return new DirectoryChanges(handle, filter, subtree);
    }

    /// <summary>Starts the next read. False when the folder can no longer be read.</summary>
    internal bool Read()
    {
        return PInvoke.ReadDirectoryChanges(Handle, _buffer, BufferSize, _subtree, (FILE_NOTIFY_CHANGE)_filter, null,
            _overlapped, null);
    }

    /// <summary>
    /// Waits for the read to finish and adds the name of each thing that changed, relative to the folder, to
    /// <paramref name="names"/>. False when the read failed, as for a folder that went away, or reported nothing,
    /// as for a buffer that overflowed and dropped changes.
    /// </summary>
    internal bool Wait(List<string> names)
    {
        uint returned;
        if (!PInvoke.GetOverlappedResult(Handle, _overlapped, &returned, true) || returned == 0)
            return false;
        var count = (int)Math.Min(returned, BufferSize);
        var offset = 0;
        while (offset + HeaderSize <= count)
        {
            var entry = (FILE_NOTIFY_INFORMATION*)(_buffer + offset);
            if (entry->FileNameLength > (uint)(count - offset - HeaderSize))
                break;
            names.Add(new string((char*)&entry->FileName, 0, (int)entry->FileNameLength / sizeof(char)));
            if ((int)entry->NextEntryOffset <= 0)
                break;
            offset += (int)entry->NextEntryOffset;
        }

        return true;
    }

    /// <summary>Cancels the outstanding read, so <see cref="Wait"/> returns.</summary>
    internal void Cancel()
    {
        PInvoke.CancelIoEx(Handle, null);
    }

    private HANDLE Handle => new(_handle.DangerousGetHandle());

    public void Dispose()
    {
        // A second call would free the buffer again.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _handle.Dispose();
        _completed.Dispose();
        NativeMemory.Free(_buffer);
        NativeMemory.Free(_overlapped);
    }
}
