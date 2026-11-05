using Winshell.Diagnostics;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// The native window behind a shell window, and its life. It makes the window with its three parts,
/// <see cref="Placement"/>, <see cref="Presentation"/> and <see cref="Messages"/>, and releases the window, its place
/// and its content when its client disposes it or Windows destroys it. A window that throws while handling a message
/// stops, and its client makes another.
/// </summary>
internal sealed class HostWindow : IDisposable
{
    private const string ClassName = DoctorScreens.WinshellClass;

    private readonly ShellPlatform _platform;
    private readonly NativeWindow _window;
    private bool _failed;

    public HostWindow(ShellPlatform platform, WindowKind kind, IHostClient client, Action dismissMenu)
    {
        _platform = platform;
        Kind = kind;
        Client = client;
        Presentation = new HostPresentation(this, platform);
        Focus = new WindowFocus(kind, new HostFocusEffects(this, platform));
        Messages = new HostMessages(this, platform, dismissMenu);
        var (style, exStyle) = Styles(kind);
        _window = NativeWindow.Create(ClassName, style, exStyle, OnWindowMessage);
        Handle = _window.Handle;
        // The handler gets no message until Create returns, and the placement's constructor sends and pumps none, so
        // every part exists before the first message.
        Placement = new HostPlacement(this, platform);
        // A window that could not be finished is destroyed here; its client never gets it to dispose.
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
    internal IHostClient Client { get; }

    internal HostPlacement Placement { get; }

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
            WindowKind.Floating => (Win32.WS_OVERLAPPEDWINDOW | Win32.WS_CLIPCHILDREN,
                Win32.WS_EX_NOREDIRECTIONBITMAP | Win32.WS_EX_APPWINDOW | WindowFocus.StartingExStyle(kind)),
            _ => (Win32.WS_POPUP,
                Win32.WS_EX_NOREDIRECTIONBITMAP | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST |
                WindowFocus.StartingExStyle(kind))
        };
    }

    private nint? OnWindowMessage(WindowMessage message)
    {
        // A window that threw handles nothing more; its client is making another.
        if (_failed)
            return null;
        try
        {
            return Messages.OnMessage(message);
        }
        catch (Exception e)
        {
            Fail(e);
            return null;
        }
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
