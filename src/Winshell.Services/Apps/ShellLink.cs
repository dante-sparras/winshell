using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Winshell.Native;
using Winshell.Windows;

namespace Winshell.Apps;

/// <summary>What a <c>.lnk</c> shortcut holds.</summary>
internal sealed record ShellLinkInfo(string Target, string Arguments, string WorkingDirectory, string IconLocation, int IconIndex, string Description, string AppUserModelId);

/// <summary>Reads <c>.lnk</c> files with the shell's own ShellLink object.</summary>
internal static partial class ShellLink
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

    public static void Write(string path, string target, string arguments, string workingDirectory, string description)
    {
        object? link = null;
        try
        {
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellLink)!);
            if (link is not IShellLinkW shell || link is not IPersistFile file)
                throw new InvalidOperationException("The shell link object is not available.");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            shell.SetPath(target);
            shell.SetArguments(arguments);
            shell.SetWorkingDirectory(workingDirectory);
            shell.SetDescription(description);
            file.Save(Path.GetFullPath(path), true);
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.ReleaseComObject(link);
        }
    }

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
            var aumid = link is IPropertyStore store ? NativeMethods.GetString(store, NativeMethods.AppUserModelId) : "";
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

    /// <summary>A command line's arguments, split as programs on Windows split their own.</summary>
    public static unsafe List<string> SplitArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return [];
        // The first word is read as a program name, by other rules, so a placeholder takes its place.
        var argv = Win32.CommandLineToArgvW("x " + arguments, out var count);
        if (argv == 0)
            return [];
        try
        {
            var result = new List<string>(count);
            for (var i = 1; i < count; i++)
                result.Add(Marshal.PtrToStringUni(((nint*)argv)[i]) ?? "");
            return result;
        }
        finally
        {
            Win32.LocalFree(argv);
        }
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint findData, uint flags);

        void GetIDList(out nint idList);

        void SetIDList(nint idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int max, out int icon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        void Resolve(nint hwnd, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
