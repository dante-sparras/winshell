using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.Tray;

/// <summary>Sends an app the clicks on its notification area icon, the way Explorer sends them.</summary>
/// <param name="loop">The loop the pointer is put back on.</param>
/// <param name="letAppTakeForeground">
/// Lets the app with this window and process id take the foreground; <see cref="TrayHost"/> passes
/// <see cref="LetAppTakeForeground"/>.
/// </param>
internal sealed unsafe partial class TrayClicker(IEventLoop loop, Action<nint, uint> letAppTakeForeground) : IDisposable
{
    internal const uint MediumIntegrity = 0x2000;
    private static readonly uint ShellIntegrity = IntegrityLevel((uint)Environment.ProcessId);

    private readonly TrayCursor _cursor = new(loop, CursorPosition, point => SetCursorPos(point.X, point.Y));

    public void Dispose() => _cursor.Dispose();

    public void Click(TrayIcon icon, TrayClick click, (int X, int Y)? at)
    {
        if (!Win32.IsWindow(icon.Window) || icon.CallbackMessage == 0)
            return;
        Win32.GetWindowThreadProcessId(icon.Window, out var pid);
        if (!MayClick(ShellIntegrity, IntegrityLevel(pid)))
            return;
        var point = at ?? CursorPosition();
        // Apps read the cursor when they open a menu, and many ignore the point in the message.
        if (at is { } target)
            _cursor.Place(target);
        letAppTakeForeground(icon.Window, pid);
        foreach (var message in TrayProtocol.ClickMessages(click, icon.Version))
            Send(icon, message, point);
    }

    /// <summary>
    /// Whether a click may reach an app at this integrity level. Any app may add an icon here, naming any window and
    /// callback, and nothing says which app sent it. Explorer clicks icons with an unelevated user's rights, so
    /// Windows keeps its clicks from an elevated app unless that app lets them in. An elevated shell would not be
    /// kept out, so it clicks no app above medium integrity, nor one whose level it cannot read.
    /// </summary>
    internal static bool MayClick(uint shell, uint app) => shell <= MediumIntegrity || app <= MediumIntegrity;

    /// <summary>
    /// Lets the app take the foreground for its menu or window. Nothing is activated for it, so the window the user
    /// works in keeps the keyboard when the app shows nothing.
    /// </summary>
    internal static void LetAppTakeForeground(nint window, uint pid)
    {
        // A click on a shell window is input this process just received, which lets it pass the right on.
        if (AllowSetForegroundWindow(pid))
            return;
        // A click from IPC or a timer is not. Join the foreground thread long enough to hand the foreground over.
        var foregroundThread = Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out _);
        var thread = Win32.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thread && Win32.AttachThreadInput(foregroundThread, thread, true);
        try
        {
            Win32.SetForegroundWindow(window);
            AllowSetForegroundWindow(pid);
        }
        finally
        {
            if (attached)
                Win32.AttachThreadInput(foregroundThread, thread, false);
        }
    }

    private static void Send(TrayIcon icon, uint message, (int X, int Y) point)
    {
        var (wParam, lParam) = TrayProtocol.Callback(icon.Version, icon.Id, message, point.X, point.Y);
        SendNotifyMessageW(icon.Window, icon.CallbackMessage, wParam, lParam);
    }

    private static (int X, int Y) CursorPosition()
    {
        Win32.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>A process's mandatory integrity level, or <see cref="uint.MaxValue"/> when it cannot be read.</summary>
    internal static uint IntegrityLevel(uint pid)
    {
        var process = Win32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return uint.MaxValue;
        try
        {
            if (!Win32.OpenProcessToken(process, TOKEN_QUERY, out var token))
                return uint.MaxValue;
            try
            {
                // A TOKEN_MANDATORY_LABEL, followed by the label's SID, S-1-16-level.
                var label = stackalloc byte[64];
                if (!Win32.GetTokenInformation(token, TokenIntegrityLevel, label, 64, out _))
                    return uint.MaxValue;
                var sid = *(nint*)label;
                return *GetSidSubAuthority(sid, (uint)(*GetSidSubAuthorityCount(sid) - 1));
            }
            finally
            {
                Win32.CloseHandle(token);
            }
        }
        finally
        {
            Win32.CloseHandle(process);
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenIntegrityLevel = 25;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SendNotifyMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("advapi32.dll")]
    private static partial byte* GetSidSubAuthorityCount(nint sid);

    [LibraryImport("advapi32.dll")]
    private static partial uint* GetSidSubAuthority(nint sid, uint index);
}
