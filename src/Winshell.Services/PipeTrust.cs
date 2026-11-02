using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Winshell;

/// <summary>
/// The control pipe is current-user only, and that DACL does not stop a lower-integrity process of the same user.
/// The server labels the pipe medium and refuses a client below its own integrity. The client accepts only a pipe
/// owned by its user and served by winshell.exe or winshellw.exe at its own integrity or higher.
/// The owner is the user, not the token's default owner, which is Administrators when elevated. Then a shell and a
/// CLI at different elevations still connect, and the side that refuses can say why.
/// </summary>
internal static partial class PipeTrust
{
    private const uint LabelSecurityInformation = 0x10;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const uint PipeAccessDuplex = 0x3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint FileFlagFirstPipeInstance = 0x00080000;
    private const uint PipeRejectRemoteClients = 0x8;
    private const int ErrorAccessDenied = 5;

    internal static bool IsWinshellImage(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        var name = Path.GetFileName(path.AsSpan());
        return name.Equals("winshell.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("winshellw.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="actor"/> is known and is not below <paramref name="floor"/>.</summary>
    internal static bool AtLeast(int actor, int floor) => actor != 0 && floor != 0 && actor >= floor;

    /// <summary>
    /// Creates one server instance. The medium label is part of the first instance's descriptor, which later
    /// instances inherit. Applying it afterwards leaves the next instance unlabeled, and that handle cannot be
    /// relabeled without SeSecurityPrivilege.
    /// </summary>
    internal static SafePipeHandle OpenServer(string pipe, bool first)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new IOException("The control pipe could not be labeled.");
        var labeled = TryCreate(pipe, first, $"O:{sid}D:(A;;GA;;;{sid})S:(ML;;NW;;;ME)", out var error);
        if (labeled is not null && IsMedium(labeled))
            return labeled;
        labeled?.Dispose();
        if (first && error == ErrorAccessDenied)
            throw new IOException("The control pipe is already taken.");
        var handle = TryCreate(pipe, first, $"O:{sid}D:(A;;GA;;;{sid})", out error);
        if (handle is null)
            throw new IOException(error == ErrorAccessDenied
                ? "The control pipe is already taken."
                : $"The control pipe could not be created ({error}).");
        try
        {
            if (!IsMedium(handle))
                SetMediumLabel(handle);
            return handle;
        }
        catch (IOException) when (!first)
        {
            // Relabeling a later instance is denied without SeSecurityPrivilege. The first instance's
            // descriptor, including its label when the kernel copied one, is already on this handle.
            if (IsMedium(handle))
                return handle;
            handle.Dispose();
            throw;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static bool IsMedium(SafePipeHandle pipe)
    {
        var label = MandatoryLabel(pipe);
        return label.Contains("ME", StringComparison.Ordinal) && label.Contains("NW", StringComparison.Ordinal);
    }

    private static void SetMediumLabel(SafePipeHandle pipe)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("S:(ML;;NW;;;ME)", 1, out var sd, out _))
            throw new IOException($"The control pipe could not be labeled ({Marshal.GetLastWin32Error()}).");
        try
        {
            if (!SetKernelObjectSecurity(pipe.DangerousGetHandle(), LabelSecurityInformation, sd))
                throw new IOException($"The control pipe could not be labeled ({Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            LocalFree(sd);
        }
    }

    private static SafePipeHandle? TryCreate(string pipe, bool first, string sddl, out int error)
    {
        error = 0;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var sd, out _))
        {
            error = Marshal.GetLastWin32Error();
            return null;
        }
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            SecurityDescriptor = sd,
        };
        var buffer = Marshal.AllocHGlobal(attributes.Length);
        try
        {
            Marshal.StructureToPtr(attributes, buffer, false);
            var mode = PipeAccessDuplex | FileFlagOverlapped;
            if (first)
                mode |= FileFlagFirstPipeInstance;
            var handle = CreateNamedPipeW(@"\\.\pipe\" + pipe, mode, PipeRejectRemoteClients, 255, 4096, 4096, 0, buffer);
            if (!handle.IsInvalid)
                return handle;
            error = Marshal.GetLastWin32Error();
            handle.Dispose();
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            LocalFree(sd);
        }
    }

    internal static string MandatoryLabel(SafePipeHandle pipe)
    {
        var handle = pipe.DangerousGetHandle();
        GetKernelObjectSecurity(handle, LabelSecurityInformation, 0, 0, out var needed);
        if (needed == 0)
            return "";
        var sd = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!GetKernelObjectSecurity(handle, LabelSecurityInformation, sd, needed, out _))
                return "";
            if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(sd, 1, LabelSecurityInformation, out var text, out _))
                return "";
            try
            {
                return Marshal.PtrToStringUni(text) ?? "";
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

    /// <summary>Null when the client may talk to the pipe's server. Otherwise the reason, for the user.</summary>
    internal static string? ServerRefusal(PipeStream pipe)
    {
        var user = WindowsIdentity.GetCurrent().User;
        if (user is null || !user.Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
            return "The control pipe belongs to another user.";
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid)
            || !IsWinshellImage(ImagePath((int)pid)))
            return "The control pipe is not owned by Winshell.";
        if (!AtLeast(IntegrityRid((int)pid), IntegrityRid(Environment.ProcessId)))
            return "The shell runs at a lower integrity level than this command. Run the command without elevation.";
        return null;
    }

    internal static bool ClientMayUseShell(SafePipeHandle pipe) => ClientRefusal(pipe) is null;

    /// <summary>Null when the connected client may use the shell. Otherwise the reason, for the log and the client.</summary>
    internal static string? ClientRefusal(SafePipeHandle pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.DangerousGetHandle(), out var pid))
            return $"The shell could not identify the command's process ({Marshal.GetLastWin32Error()}).";
        var client = IntegrityRid((int)pid);
        var floor = IntegrityRid(Environment.ProcessId);
        if (client == 0 || floor == 0)
            return $"The shell could not read the integrity level of process {pid}.";
        if (!AtLeast(client, floor))
            return $"The shell runs at a higher integrity level (0x{floor:X}) than process {pid} (0x{client:X}). Run the command elevated.";
        return null;
    }

    internal static string? ImagePath(int pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (process == 0)
            return null;
        try
        {
            var chars = new char[32768];
            var size = (uint)chars.Length;
            if (!QueryFullProcessImageNameW(process, 0, chars, ref size) || size == 0)
                return null;
            return new string(chars, 0, (int)size);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    internal static int IntegrityRid(int pid)
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

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out nint securityDescriptor, out uint size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSecurityDescriptorToStringSecurityDescriptorW(nint securityDescriptor, uint revision, uint securityInformation, out nint sddl, out uint size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetKernelObjectSecurity(nint handle, uint securityInformation, nint securityDescriptor);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetKernelObjectSecurity(nint handle, uint securityInformation, nint securityDescriptor, uint length, out uint needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int tokenClass, nint buffer, uint length, out uint returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthority(nint sid, uint index);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthorityCount(nint sid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(nint process, uint flags, [Out] char[] name, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(nint pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(nint pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafePipeHandle CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBuffer, uint inBuffer, uint defaultTimeout, nint securityAttributes);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint SecurityDescriptor;
        public int InheritHandle;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
