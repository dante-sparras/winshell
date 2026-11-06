using Winshell.Native;

namespace Winshell.Tray;

/// <summary>Sends an app the clicks on its notification area icon, the way Explorer sends them.</summary>
/// <param name="loop">The loop the pointer is put back on.</param>
/// <param name="backend">The apps' windows, processes and the pointer.</param>
internal sealed class TrayClicker(IEventLoop loop, ITrayBackend backend) : IDisposable
{
    private readonly uint? _shellIntegrity = backend.IntegrityOf((uint)Environment.ProcessId);
    private readonly TrayCursor _cursor = new(loop, () => backend.Cursor, backend.MoveCursor);

    public void Dispose()
    {
        _cursor.Dispose();
    }

    public void Click(TrayIcon icon, TrayClick click, (int X, int Y)? at)
    {
        var pid = backend.ProcessOf(icon.Window);
        if (pid == 0 || icon.CallbackMessage == 0)
            return;
        if (!MayClick(_shellIntegrity, backend.IntegrityOf(pid)))
            return;
        var point = at ?? backend.Cursor;
        // Apps read the cursor when they open a menu, and many ignore the point in the message.
        if (at is { } target)
            _cursor.Place(target);
        backend.LetAppTakeForeground(icon.Window, pid);
        foreach (var message in TrayProtocol.ClickMessages(click, icon.Version))
        {
            var (wParam, lParam) = TrayProtocol.Callback(icon.Version, icon.Id, message, point.X, point.Y);
            backend.Notify(icon.Window, icon.CallbackMessage, wParam, lParam);
        }
    }

    /// <summary>
    /// Whether a click may reach an app at this integrity level. Any app may add an icon here, naming any window and
    /// callback, and nothing says which app sent it. Explorer clicks icons with an unelevated user's rights, so
    /// Windows keeps its clicks from an elevated app unless that app lets them in. An elevated shell would not be
    /// kept out, so it clicks no app above medium integrity, nor one whose level it cannot read.
    /// </summary>
    internal static bool MayClick(uint? shell, uint? app)
    {
        return shell <= ProcessIntegrity.Medium || app <= ProcessIntegrity.Medium;
    }
}
