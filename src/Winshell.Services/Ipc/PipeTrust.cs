using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// The control pipe is current-user only, and that DACL does not stop a lower-integrity process of the same user.
/// The server labels the pipe medium and refuses a client below its own integrity. The client accepts only a pipe
/// owned by its user and served at its own integrity or higher. A configuration's program can have any name, so
/// neither side looks at the other's file name.
/// The owner is the user, not the token's default owner, which is Administrators when elevated. Then a shell and a
/// CLI at different elevations still connect, and the side that refuses can say why.
/// </summary>
internal static partial class PipeTrust
{
    private const uint LabelSecurityInformation = 0x10;
    private const uint PipeAccessDuplex = 0x3;
    private const uint FileFlagFirstPipeInstance = 0x00080000;
    private const uint PipeRejectRemoteClients = 0x8;

    /// <summary>
    /// True when both levels are known and <paramref name="actor"/> is not below <paramref name="floor"/>. Untrusted, 0,
    /// passes on neither side.
    /// </summary>
    internal static bool AtLeast(uint? actor, uint? floor) => actor >= floor && floor > 0;

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
        if (first && error == Win32.ERROR_ACCESS_DENIED)
            throw new IOException("The control pipe is already taken.");
        var handle = TryCreate(pipe, first, $"O:{sid}D:(A;;GA;;;{sid})", out error);
        if (handle is null)
            throw new IOException(error == Win32.ERROR_ACCESS_DENIED
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
            Win32.LocalFree(sd);
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
            var mode = PipeAccessDuplex | Win32.FILE_FLAG_OVERLAPPED;
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
            Win32.LocalFree(sd);
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
                Win32.LocalFree(text);
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
        if (!Win32.GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid))
            return "The shell behind the control pipe could not be identified.";
        if (!AtLeast(ProcessIntegrity.TryRead(pid), ProcessIntegrity.TryRead((uint)Environment.ProcessId)))
            return "The shell runs at a lower integrity level than this command. Run the command without elevation.";
        return null;
    }

    /// <summary>Null when the connected client may use the shell. Otherwise the reason, for the log and the client.</summary>
    internal static string? ClientRefusal(SafePipeHandle pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.DangerousGetHandle(), out var pid))
            return $"The shell could not identify the command's process ({Marshal.GetLastWin32Error()}).";
        if (ProcessIntegrity.TryRead(pid) is not { } client || ProcessIntegrity.TryRead((uint)Environment.ProcessId) is not { } floor)
            return $"The shell could not read the integrity level of process {pid}.";
        if (!AtLeast(client, floor))
            return $"The shell runs at a higher integrity level (0x{floor:X}) than process {pid} (0x{client:X}). Run the command elevated.";
        return null;
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
}
