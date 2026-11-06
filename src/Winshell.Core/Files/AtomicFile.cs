using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Winshell.Native;

namespace Winshell;

/// <summary>Whether <see cref="AtomicFile.Save"/> replaced the file, or left a hand save that landed first.</summary>
internal enum AtomicSave
{
    /// <summary>The file holds the text this save wrote.</summary>
    Saved,

    /// <summary>The file changed after it was read, and it was not the text being saved, so it was left alone.</summary>
    HandSaveWon
}

/// <summary>
/// How a text file is saved. <see cref="Save"/> follows a link, creates the folder, drops a stale temp file, and
/// replaces the file only when a hand save did not land first. A temporary file goes down beside it and is flushed
/// to disk, then replaces it, so a crash leaves the old file or the new one. A symbolic link stays a link, and a
/// hard link is written in place. <see cref="FollowLink"/> is separate: a watch uses it to find the file it follows.
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// Puts <paramref name="text"/> at <paramref name="path"/>. A symbolic link is followed so the link stays a link,
    /// a hard link is written in place, the folder is created, and a temp file left by a process that is gone is
    /// removed. The destination is read again before it is replaced. When that text is neither
    /// <paramref name="expectedOnDisk"/> nor <paramref name="text"/>, a hand save landed and the file is left alone.
    /// A lock while replacing is tried again.
    /// </summary>
    /// <param name="path">The file. The folder need not exist yet.</param>
    /// <param name="text">What the file should hold.</param>
    /// <param name="bom">Whether the file starts with a UTF-8 byte order mark.</param>
    /// <param name="expectedOnDisk">
    /// The text last read from the file, or null when it was missing. The comparison is the UTF-8 text with a byte
    /// order mark stripped.
    /// </param>
    public static AtomicSave Save(string path, string text, bool bom, string? expectedOnDisk)
    {
        var destination = SaveTarget(path, out var hardLink);
        if (Path.GetDirectoryName(destination) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        DeleteStaleTemps(destination);
        var written = Write(destination, text, bom, hardLink,
            () => HandSaveWins(ReadText(destination), expectedOnDisk, text));
        return written ? AtomicSave.Saved : AtomicSave.HandSaveWon;
    }

    /// <summary>
    /// A hand save landed during this write when the destination changed after the write started and is not the
    /// text about to be put down. <paramref name="diskAtStart"/> null means the file was missing.
    /// </summary>
    private static bool HandSaveWins(string? onDisk, string? diskAtStart, string incoming)
    {
        return onDisk is not null
               && !string.Equals(onDisk, diskAtStart, StringComparison.Ordinal)
               && !string.Equals(onDisk, incoming, StringComparison.Ordinal);
    }

    /// <summary>The destination as UTF-8 text, or null when it is missing or cannot be read.</summary>
    private static string? ReadText(string path)
    {
        try
        {
            // Share the file so a save can replace it while this read is open.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException
                                      or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Puts <paramref name="text"/> at <paramref name="destination"/>, through a temporary file unless
    /// <paramref name="inPlace"/>. <paramref name="handSaved"/> is asked last, and when it says yes the destination is
    /// left alone and this returns false.
    /// </summary>
    private static bool Write(string destination, string text, bool bom, bool inPlace, Func<bool> handSaved)
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
        var encoding = new UTF8Encoding(bom);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using (var writer = new StreamWriter(stream, encoding, leaveOpen: true))
        {
            writer.Write(text);
            writer.Flush();
        }

        stream.Flush(true);
    }

    /// <summary>
    /// Replaces <paramref name="path"/> with <paramref name="temp"/>.
    /// Antivirus and indexers often hold the new file for a moment, so a sharing or lock failure is tried again.
    /// </summary>
    private static void MoveReplacing(string temp, string path)
    {
        int[] delays = [25, 50, 100, 200];
        for (var attempt = 0;; attempt++)
            try
            {
                File.Move(temp, path, true);
                return;
            }
            catch (Exception e) when (IsTransientLock(e) && attempt < delays.Length)
            {
                Thread.Sleep(delays[attempt]);
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
    private static void DeleteStaleTemps(string path)
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
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
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
        if (FileLinks.TryReparsePoint(full, out var reparse) && !reparse)
            return full;
        try
        {
            var target = new FileInfo(full).ResolveLinkTarget(true);
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
    private static string SaveTarget(string path, out bool hardLink)
    {
        hardLink = false;
        var full = FollowLink(path);
        if (File.Exists(full))
            hardLink = LinkCount(full) > 1;
        return full;
    }

    private static int LinkCount(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return FileLinks.Count(stream.SafeFileHandle) ?? 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 1;
        }
    }
}
