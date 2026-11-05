using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Pipes;
using Microsoft.Win32.SafeHandles;

namespace Winshell.Native;

/// <summary>Named pipe instances created with a security descriptor, and the mandatory label on a handle.</summary>
internal static unsafe class NamedPipeServer
{
    /// <summary>
    /// Creates one duplex, overlapped server instance of <paramref name="name"/> that refuses remote clients, with the
    /// descriptor <paramref name="sddl"/>. Null when it fails, with the Win32 error in <paramref name="error"/>.
    /// </summary>
    internal static SafePipeHandle? CreateServer(string name, bool first, string sddl, out int error)
    {
        error = 0;
        if (!PInvoke.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, PInvoke.SDDL_REVISION_1, out var sd))
        {
            error = Marshal.GetLastPInvokeError();
            return null;
        }

        try
        {
            var attributes = new SECURITY_ATTRIBUTES
                { nLength = (uint)sizeof(SECURITY_ATTRIBUTES), lpSecurityDescriptor = sd };
            var mode = FILE_FLAGS_AND_ATTRIBUTES.PIPE_ACCESS_DUPLEX | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED;
            if (first)
                mode |= FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_FIRST_PIPE_INSTANCE;
            HANDLE handle;
            fixed (char* path = name)
            {
                // The metadata leaves SetLastError off CreateNamedPipeW, so its error is read here, as CsWin32 does for
                // the calls that have it.
                Marshal.SetLastSystemError(0);
                handle = PInvoke.CreateNamedPipe(path, mode, NAMED_PIPE_MODE.PIPE_REJECT_REMOTE_CLIENTS, 255, 4096,
                    4096, 0, &attributes);
                error = Marshal.GetLastSystemError();
            }

            if ((nint)handle.Value == -1)
                return null;
            error = 0;
            return new SafePipeHandle((nint)handle.Value, true);
        }
        finally
        {
            Win32.LocalFree((nint)sd.Value);
        }
    }

    /// <summary>The mandatory label of <paramref name="handle"/> as SDDL, or empty when it has none or cannot be read.</summary>
    internal static string MandatoryLabel(SafeHandle handle)
    {
        var target = new HANDLE(handle.DangerousGetHandle());
        const uint label = (uint)OBJECT_SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION;
        PInvoke.GetKernelObjectSecurity(target, label, default, 0, out var needed);
        if (needed == 0)
            return "";
        var sd = new byte[needed];
        fixed (byte* buffer = sd)
        {
            if (!PInvoke.GetKernelObjectSecurity(target, label, new PSECURITY_DESCRIPTOR(buffer), needed, out _))
                return "";
            if (!PInvoke.ConvertSecurityDescriptorToStringSecurityDescriptor(new PSECURITY_DESCRIPTOR(buffer),
                    PInvoke.SDDL_REVISION_1,
                    OBJECT_SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION, out var text))
                return "";
            try
            {
                return text.ToString();
            }
            finally
            {
                Win32.LocalFree((nint)text.Value);
            }
        }
    }

    /// <summary>
    /// Replaces the mandatory label of <paramref name="handle"/> with the one in <paramref name="sddl"/>. Returns 0, or
    /// the Win32 error.
    /// </summary>
    internal static int SetMandatoryLabel(SafeHandle handle, string sddl)
    {
        if (!PInvoke.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, PInvoke.SDDL_REVISION_1, out var sd))
            return Marshal.GetLastPInvokeError();
        try
        {
            return PInvoke.SetKernelObjectSecurity(new HANDLE(handle.DangerousGetHandle()),
                OBJECT_SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION, sd)
                ? 0
                : Marshal.GetLastPInvokeError();
        }
        finally
        {
            Win32.LocalFree((nint)sd.Value);
        }
    }

    /// <summary>The process at the client end of a connected server instance, or null with the Win32 error.</summary>
    internal static uint? ClientProcessId(SafePipeHandle pipe, out int error)
    {
        error = 0;
        if (PInvoke.GetNamedPipeClientProcessId(new HANDLE(pipe.DangerousGetHandle()), out var pid))
            return pid;
        error = Marshal.GetLastPInvokeError();
        return null;
    }
}
