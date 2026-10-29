using System.Runtime.InteropServices;

namespace Winshell.Platform.Windows.Services.WindowList;

/// <summary>The Win32 calls that list, watch and drive other programs' windows.</summary>
internal static unsafe partial class NativeMethods
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    public const nint HSHELL_REDRAW = 6;
    public const nint HSHELL_HIGHBIT = 0x8000;

    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;

    public const uint GW_OWNER = 4;
    public const uint GA_ROOT = 2;
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_CHILD = 0x40000000;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint DWM_TNP_RECTDESTINATION = 0x1;
    public const uint DWM_TNP_RECTSOURCE = 0x2;
    public const uint DWM_TNP_OPACITY = 0x4;
    public const uint DWM_TNP_VISIBLE = 0x8;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public const int SW_MAXIMIZE = 3;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const nint SC_CLOSE = 0xF060;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const byte VK_MENU = 0x12;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x1;
    public const uint KEYEVENTF_KEYUP = 0x2;

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterShellHookWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint hwnd, char* text, int max);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    public static partial int GetClassName(nint hwnd, char* text, int max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out Native.RECT rect);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindowAsync(nint hwnd, int command);

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint WPF_ASYNCWINDOWPLACEMENT = 0x0004;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWMINIMIZED = 2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPlacement(nint hwnd, in WINDOWPLACEMENT placement);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [LibraryImport("user32.dll")]
    public static partial void keybd_event(byte key, byte scan, uint flags, nuint extra);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, char* name, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static partial int DwmGetWindowRect(nint hwnd, int attribute, out Native.RECT value, int size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmUnregisterThumbnail(nint thumbnail);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmUpdateThumbnailProperties(nint thumbnail, in DWM_THUMBNAIL_PROPERTIES properties);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmQueryThumbnailSourceSize(nint thumbnail, out THUMBNAIL_SIZE size);

    [DllImport("shell32.dll")]
    public static extern int SHGetPropertyStoreForWindow(nint hwnd, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PROPVARIANT value);

    public static string WindowText(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";
        var buffer = stackalloc char[Math.Min(length + 1, 4096)];
        var got = GetWindowText(hwnd, buffer, Math.Min(length + 1, 4096));
        return new string(buffer, 0, Math.Max(got, 0));
    }

    public static string ClassName(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var got = GetClassName(hwnd, buffer, 256);
        return new string(buffer, 0, Math.Max(got, 0));
    }

    /// <summary>The full path of the program that owns a window, or empty when it cannot be read.</summary>
    public static string ExePath(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return "";
        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, size) : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static bool IsCloaked(nint hwnd) => DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>System.AppUserModel.ID, the id an app gives a window for taskbar grouping.</summary>
    public static readonly PROPERTYKEY AppUserModelId = new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

    /// <summary>A string property of a property store, or empty.</summary>
    public static string GetString(IPropertyStore store, in PROPERTYKEY key)
    {
        var value = default(PROPVARIANT);
        try
        {
            // VT_LPWSTR
            return store.GetValue(key, out value) == 0 && value.vt == 31 && value.pointer != 0 ? Marshal.PtrToStringUni(value.pointer) ?? "" : "";
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary>The AppUserModelID of a window, when its app set one.</summary>
    public static string WindowAppId(nint hwnd)
    {
        try
        {
            if (SHGetPropertyStoreForWindow(hwnd, typeof(IPropertyStore).GUID, out var store) != 0 || store is null)
                return "";
            try
            {
                return GetString(store, AppUserModelId);
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (COMException)
        {
            return "";
        }
    }
}

/// <summary>The restored position of a window, in the same pixels <c>GetWindowRect</c> uses for a per-monitor aware process.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public uint Length;
    public uint Flags;
    public uint ShowCmd;
    public Native.POINT MinPosition;
    public Native.POINT MaxPosition;
    public Native.RECT Normal;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort vt;
    public ushort reserved1;
    public ushort reserved2;
    public ushort reserved3;
    public nint pointer;
    public nint extra;
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PROPERTYKEY key);

    [PreserveSig]
    int GetValue(in PROPERTYKEY key, out PROPVARIANT value);

    [PreserveSig]
    int SetValue(in PROPERTYKEY key, in PROPVARIANT value);

    [PreserveSig]
    int Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal struct DWM_THUMBNAIL_PROPERTIES
{
    public uint Flags;
    public Native.RECT Destination;
    public Native.RECT Source;
    public byte Opacity;
    public int Visible;
    public int SourceClientAreaOnly;
}

[StructLayout(LayoutKind.Sequential)]
internal struct THUMBNAIL_SIZE
{
    public int Width;
    public int Height;
}
