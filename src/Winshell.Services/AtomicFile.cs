using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

#pragma warning disable CA1416 // Winshell only runs on Windows.

namespace Winshell;

/// <summary>
/// How a text file is saved. A temporary file goes down beside it and is flushed to disk, then replaces it, so a crash
/// leaves the old file or the new one. A symbolic link stays a link, and a hard link is written in place.
/// </summary>
internal static class AtomicFile
{
    private const uint FileAttributeReparsePoint = 0x400;
    private static readonly IntPtr InvalidFindHandle = new(-1);

    /// <summary>
    /// A hand save landed during this write when the destination changed after the write started and is not the
    /// text about to be put down. <paramref name="diskAtStart"/> null means the file was missing.
    /// </summary>
    public static bool HandSaveWins(string? onDisk, string? diskAtStart, string incoming)
        => onDisk is not null
            && !string.Equals(onDisk, diskAtStart, StringComparison.Ordinal)
            && !string.Equals(onDisk, incoming, StringComparison.Ordinal);

    /// <summary>
    /// Puts <paramref name="text"/> at <paramref name="destination"/>, through a temporary file unless
    /// <paramref name="inPlace"/>. <paramref name="handSaved"/> is asked last, and when it says yes the destination is
    /// left alone and this returns false.
    /// </summary>
    public static bool Write(string destination, string text, bool bom, bool inPlace, Func<bool> handSaved)
    {
        if (inPlace)
        {
            if (handSaved())
                return false;
            WriteText(destination, text, bom);
            return true;
        }
        var temp = destination + ".tmp-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");
        WriteText(temp, text, bom);
        try
        {
            if (handSaved())
                return false;
            MoveReplacing(temp, destination);
            return true;
        }
        finally
        {
            try
            {
                // A successful replace has already moved the temp away. A hand save or a failed
                // replace leaves it, and the next save must not see it.
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void WriteText(string path, string text, bool bom)
    {
        // Flush to disk before a later rename. A power loss after WriteAllText can leave an empty file.
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using (var writer = new StreamWriter(stream, encoding, leaveOpen: true))
        {
            writer.Write(text);
            writer.Flush();
        }
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Replaces <paramref name="path"/> with <paramref name="temp"/>.
    /// Antivirus and indexers often hold the new file for a moment, so a sharing or lock failure is tried again.
    /// </summary>
    private static void MoveReplacing(string temp, string path)
    {
        int[] delays = [25, 50, 100, 200];
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception e) when (IsTransientLock(e) && attempt < delays.Length)
            {
                Thread.Sleep(delays[attempt]);
            }
        }
    }

    private static bool IsTransientLock(Exception exception)
    {
        // Win32: ERROR_ACCESS_DENIED 5, ERROR_SHARING_VIOLATION 32, ERROR_LOCK_VIOLATION 33.
        var code = exception.HResult & 0xFFFF;
        return code is 5 or 32 or 33;
    }

    /// <summary>
    /// Removes <c>path.tmp-*</c> left by a process that is no longer running.
    /// The suffix is a process id, or <c>pid-guid</c> for a save in flight. A live process keeps its file.
    /// </summary>
    public static void DeleteStaleTemps(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (dir is null || name.Length == 0 || !Directory.Exists(dir))
            return;
        var prefix = name + ".tmp-";
        foreach (var candidate in Directory.EnumerateFiles(dir, prefix + "*"))
        {
            var file = Path.GetFileName(candidate);
            if (file.Length <= prefix.Length || !file.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var suffix = file[prefix.Length..];
            // The save names the temp pid-guid. An older temp is only the pid. Either way a live process keeps its file.
            var pidText = suffix;
            var dash = suffix.IndexOf('-');
            if (dash >= 0)
                pidText = suffix[..dash];
            if (int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                && ProcessIsRunning(pid))
                continue;
            try
            {
                File.Delete(candidate);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool ProcessIsRunning(int pid)
    {
        if (pid == Environment.ProcessId)
            return true;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // ArgumentException is "no process with that id". Anything else means the id could not be checked.
            return e is not ArgumentException;
        }
    }

    /// <summary>
    /// The path a watch should use. A symbolic link is followed to its final target.
    /// A normal file is not opened. Counting hard links happens only when a save chooses how to write.
    /// A path that is not there yet is returned unchanged.
    /// </summary>
    public static string FollowLink(string path)
    {
        var full = Path.GetFullPath(path);
        // FindFirstFile reports the link itself. GetAttributes follows it, so it cannot tell a link from its target.
        if (TryReparsePoint(full, out var reparse) && !reparse)
            return full;
        try
        {
            var target = new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
                return target.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return full;
    }

    /// <summary>
    /// The file a save should change. A symbolic link is followed to its final target, so the link stays a link.
    /// A hard link is the same path, and <paramref name="hardLink"/> is true so the save writes in place.
    /// A path that is not there yet is returned unchanged.
    /// </summary>
    public static string SaveTarget(string path, out bool hardLink)
    {
        hardLink = false;
        var full = FollowLink(path);
        if (File.Exists(full))
            hardLink = LinkCount(full) > 1;
        return full;
    }

    public static int LinkCount(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info))
                return 1;
            return (int)info.NumberOfLinks;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    /// <summary>
    /// True when <paramref name="full"/> could be queried. <paramref name="reparse"/> is true for a symbolic link
    /// and false for a normal file or a hard link. A missing file returns false.
    /// </summary>
    private static bool TryReparsePoint(string full, out bool reparse)
    {
        reparse = false;
        var handle = FindFirstFileW(full, out var data);
        if (handle == InvalidFindHandle)
            return false;
        try
        {
            reparse = (data.FileAttributes & FileAttributeReparsePoint) != 0;
            return true;
        }
        finally
        {
            FindClose(handle);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string fileName, out Win32FindData data);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindData
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string AlternateFileName;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
