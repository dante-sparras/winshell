using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Winshell.Platform.Windows.Services.Updates;

/// <summary>
/// The verified zip and its digest, kept beside the updates folder. The version folder can be rewritten after the
/// download check; apply hashes this zip again and extracts that, so a swapped file in the version folder is not copied.
/// </summary>
internal static partial class UpdateTrust
{
    private const uint LabelSecurityInformation = 0x10;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int Low = 0x1000;
    private const int Medium = 0x2000;
    private const int High = 0x3000;
    private const int System = 0x4000;

    internal static string Root(string updatesDir)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(updatesDir));
        return Path.Combine(string.IsNullOrEmpty(parent) ? updatesDir : parent, "update-trust");
    }

    internal static string ZipPath(string updatesDir, string version) => Path.Combine(Root(updatesDir), version + ".zip");

    internal static string DigestPath(string updatesDir, string version) => Path.Combine(Root(updatesDir), version + ".sha256");

    /// <summary>Moves a hash-checked download out of the updates folder and records the digest that matched.</summary>
    internal static void Keep(string updatesDir, string version, string partialZip, string sha256)
    {
        Directory.CreateDirectory(Root(updatesDir));
        var digest = DigestPath(updatesDir, version);
        var written = digest + ".partial";
        File.WriteAllText(written, sha256.ToLowerInvariant());
        MoveOver(partialZip, ZipPath(updatesDir, version));
        MoveOver(written, digest);
    }

    internal static void Discard(string updatesDir, string version)
    {
        TryDelete(ZipPath(updatesDir, version));
        TryDelete(DigestPath(updatesDir, version));
        TryDelete(DigestPath(updatesDir, version) + ".partial");
    }

    /// <summary>Deletes saved zips for versions that are no newer than <paramref name="current"/>, and leftover scratch.</summary>
    internal static void Clean(string updatesDir, Version current)
    {
        var root = Root(updatesDir);
        if (!Directory.Exists(root))
            return;
        foreach (var file in Directory.EnumerateFiles(root))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (Updater.ParseVersion(name) is { } version && version <= current)
                TryDelete(file);
        }
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (Path.GetFileName(dir).StartsWith("extract-", StringComparison.Ordinal))
                TryDeleteDirectory(dir);
        }
    }

    internal static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
            sha.AppendData(buffer, 0, read);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    /// <summary>
    /// An elevated process may apply storage only at its own integrity or higher. An unreadable label fails closed.
    /// A process that is not elevated applies its own download; that same-user case stays the accepted residual.
    /// </summary>
    internal static bool ElevationAllows(bool privileged, int directoryRid, int processRid) =>
        !privileged || (directoryRid != 0 && processRid != 0 && directoryRid >= processRid);

    internal static int CurrentRid() => IntegrityRid(Environment.ProcessId);

    internal static int LabelRid(string path)
    {
        if (!GetFileSecurityW(path, LabelSecurityInformation, 0, 0, out var needed) && needed == 0)
            return 0;
        var sd = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!GetFileSecurityW(path, LabelSecurityInformation, sd, needed, out _))
                return 0;
            if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(sd, 1, LabelSecurityInformation, out var text, out _))
                return 0;
            try
            {
                return RidOf(Marshal.PtrToStringUni(text) ?? "");
            }
            finally
            {
                LocalFree(text);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(sd);
        }
    }

    private static int RidOf(string label)
    {
        const string marker = ";;;";
        var at = label.LastIndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return 0;
        var token = label[(at + marker.Length)..].Trim().TrimEnd(')');
        if (token.StartsWith("S-1-16-", StringComparison.Ordinal) && int.TryParse(token["S-1-16-".Length..], out var rid))
            return rid;
        return token switch
        {
            "LW" => Low,
            "ME" => Medium,
            "HI" => High,
            "SI" => System,
            _ => 0,
        };
    }

    private static int IntegrityRid(int pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (process == 0)
            return 0;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
                return 0;
            try
            {
                var buffer = Marshal.AllocHGlobal(256);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, 256, out _))
                        return 0;
                    var sid = Marshal.ReadIntPtr(buffer);
                    var countPtr = GetSidSubAuthorityCount(sid);
                    if (countPtr == 0)
                        return 0;
                    var count = Marshal.ReadByte(countPtr);
                    if (count == 0)
                        return 0;
                    var ridPtr = GetSidSubAuthority(sid, (uint)(count - 1));
                    return ridPtr == 0 ? 0 : Marshal.ReadInt32(ridPtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static void MoveOver(string source, string destination)
    {
        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(source, destination);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileSecurityW(string name, uint information, nint descriptor, uint length, out uint needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSecurityDescriptorToStringSecurityDescriptorW(nint descriptor, uint revision, uint information, out nint text, out uint size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int tokenClass, nint buffer, uint length, out uint returned);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthority(nint sid, uint index);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthorityCount(nint sid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
