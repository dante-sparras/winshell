using System.IO.Pipes;
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
internal static class PipeTrust
{
    private const string MediumLabel = "S:(ML;;NW;;;ME)";

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
        var path = @"\\.\pipe\" + pipe;
        var labeled = NamedPipeServer.CreateServer(path, first, $"O:{sid}D:(A;;GA;;;{sid}){MediumLabel}", out var error);
        if (labeled is not null && IsMedium(labeled))
            return labeled;
        labeled?.Dispose();
        if (first && error == Win32.ERROR_ACCESS_DENIED)
            throw new IOException("The control pipe is already taken.");
        var handle = NamedPipeServer.CreateServer(path, first, $"O:{sid}D:(A;;GA;;;{sid})", out error);
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
        var label = NamedPipeServer.MandatoryLabel(pipe);
        return label.Contains("ME", StringComparison.Ordinal) && label.Contains("NW", StringComparison.Ordinal);
    }

    private static void SetMediumLabel(SafePipeHandle pipe)
    {
        var error = NamedPipeServer.SetMandatoryLabel(pipe, MediumLabel);
        if (error != 0)
            throw new IOException($"The control pipe could not be labeled ({error}).");
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
        if (NamedPipeServer.ClientProcessId(pipe, out var error) is not { } pid)
            return $"The shell could not identify the command's process ({error}).";
        if (ProcessIntegrity.TryRead(pid) is not { } client || ProcessIntegrity.TryRead((uint)Environment.ProcessId) is not { } floor)
            return $"The shell could not read the integrity level of process {pid}.";
        if (!AtLeast(client, floor))
            return $"The shell runs at a higher integrity level (0x{floor:X}) than process {pid} (0x{client:X}). Run the command elevated.";
        return null;
    }
}
