using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;

namespace Winshell.Native;

/// <summary>What a <c>.lnk</c> shortcut holds.</summary>
internal sealed record ShellLinkInfo(string Target, string Arguments, string WorkingDirectory, string IconLocation, int IconIndex, string Description, string AppUserModelId);

/// <summary>Reads <c>.lnk</c> files with the shell's own ShellLink object.</summary>
internal static class ShellLink
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

    public static ShellLinkInfo? Read(string path)
    {
        object? link = null;
        try
        {
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellLink)!);
            if (link is not IShellLinkW shell || link is not IPersistFile file)
                return null;
            file.Load(path, 0);
            var buffer = new StringBuilder(1024);
            // SLGP_RAWPATH is not asked for, so environment variables in the target are expanded.
            shell.GetPath(buffer, buffer.Capacity, 0, 0);
            var target = buffer.ToString();
            buffer.Clear();
            shell.GetArguments(buffer, buffer.Capacity);
            var arguments = buffer.ToString();
            buffer.Clear();
            shell.GetWorkingDirectory(buffer, buffer.Capacity);
            var workingDirectory = Environment.ExpandEnvironmentVariables(buffer.ToString());
            buffer.Clear();
            shell.GetIconLocation(buffer, buffer.Capacity, out var iconIndex);
            var icon = Environment.ExpandEnvironmentVariables(buffer.ToString());
            buffer.Clear();
            shell.GetDescription(buffer, buffer.Capacity);
            var description = buffer.ToString();
            var aumid = link is IPropertyStore store ? PropertyStore.ReadString(store, PropertyStore.AppUserModelId) ?? "" : "";
            return new ShellLinkInfo(target, arguments, workingDirectory, icon, iconIndex, description, aumid);
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or IOException or ArgumentException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>
    /// IPersistFile, for the same object as the shared <see cref="IShellLinkW"/>. CsWin32's struct form cannot be
    /// cast from that runtime-callable wrapper.
    /// </summary>
    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, int mode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
    }
}
