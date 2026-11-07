using Windows.Win32.UI.WindowsAndMessaging;
using Winshell.Diagnostics;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// The native window behind a shell window, and its life. It makes the window's parts, <see cref="Placement"/>, the
/// <see cref="Placer"/> its kind needs, <see cref="Presentation"/>, <see cref="Focus"/> and <see cref="Messages"/>,
/// before the window itself, and releases the window, its place and its content when its client disposes it or
/// Windows destroys it. A window that throws while handling a message stops, and its client makes another.
/// </summary>
internal sealed class HostWindow : IDisposable
{
    private const string ClassName = DoctorScreens.WinshellClass;

    private readonly ShellPlatform _platform;
    private readonly NativeWindow _window;
    private bool _failed;

    public HostWindow(ShellPlatform platform, WindowKind kind, ShellWindow client)
    {
        _platform = platform;
        Kind = kind;
        Client = client;
        Placement = new HostPlacement(this, platform);
        Placer = kind switch
        {
            WindowKind.Panel => new PanelEdge(new HostPanelDesktop(this, platform)),
            WindowKind.Popup => new PopupPlacer(this, platform),
            _ => new FloatingFrame(this, platform)
        };
        Presentation = new HostPresentation(this, platform);
        Focus = new WindowFocus(kind, new HostFocusEffects(this, platform));
        Messages = new HostMessages(this, platform);
        var (style, exStyle) = Styles(kind);
        _window = NativeWindow.Create(ClassName, style, exStyle, OnWindowMessage, Fail);
        Handle = _window.Handle;
        try
        {
            Placement.Scale = Win32.GetDpiForWindow(Handle) / 96.0;
            Presentation.Attach();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public nint Handle { get; private set; }

    public WindowKind Kind { get; }

    /// <summary>The shell window this native window shows.</summary>
    internal ShellWindow Client { get; }

    internal HostPlacement Placement { get; }

    /// <summary>Where the window's kind puts it: a panel's edge, a popup's place, or a floating window's frame.</summary>
    internal IWindowPlacer Placer { get; }

    internal HostPresentation Presentation { get; }

    internal HostMessages Messages { get; }

    /// <summary>Whether the window takes the keyboard, and what follows from it.</summary>
    internal WindowFocus Focus { get; }

    /// <summary>Whether the window has started letting go of its parts. <see cref="Handle"/> turns 0 once it has let go of the window too.</summary>
    internal bool Released { get; private set; }

    internal static (uint Style, uint ExStyle) Styles(WindowKind kind)
    {
        return kind switch
        {
            WindowKind.Floating => ((uint)(WINDOW_STYLE.WS_OVERLAPPEDWINDOW | WINDOW_STYLE.WS_CLIPCHILDREN),
                (uint)WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP | (uint)WINDOW_EX_STYLE.WS_EX_APPWINDOW |
                WindowFocus.StartingExStyle(kind)),
            _ => ((uint)WINDOW_STYLE.WS_POPUP,
                (uint)WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP | (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
                (uint)WINDOW_EX_STYLE.WS_EX_TOPMOST |
                WindowFocus.StartingExStyle(kind))
        };
    }

    private nint? OnWindowMessage(WindowMessage message)
    {
        return _failed ? null : Messages.OnMessage(message);
    }

    /// <summary>
    /// The window threw while handling a message. It stops handling them, and its client is told on the loop, outside
    /// this message, to dispose it and make another.
    /// </summary>
    private void Fail(Exception error)
    {
        if (_failed)
            return;
        _failed = true;
        _platform.Loop.Post(() =>
        {
            if (Handle != 0)
                Client.OnFailed(error);
        });
    }

    public void Dispose()
    {
        Cleanup(true);
    }

    /// <summary>Windows is destroying the window, so it lets go of it without destroying it, and tells its client on the loop.</summary>
    internal void NoteExternalDestroy()
    {
        if (Released)
            return;
        Cleanup(false);
        _platform.Loop.Post(Client.OnBackingDestroyed);
    }

    private void Cleanup(bool destroyWindow)
    {
        if (Released)
            return;
        Released = true;
        Placement.Release();
        Presentation.Dispose();
        Handle = 0;
        _window.Release(destroyWindow);
        _platform.Forget(this);
    }
}
