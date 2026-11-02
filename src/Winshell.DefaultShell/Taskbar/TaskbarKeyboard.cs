namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The keyboard on one taskbar. Win+T, or focus landing on a button, turns it on and remembers the window that had
/// the keyboard. Escape gives that window back. Another window coming forward turns it off and gives nothing back,
/// because that window already has it.
/// </summary>
public sealed class TaskbarKeyboard
{
    public bool Active { get; private set; }

    /// <summary>The window to activate when the keyboard leaves, when there was one.</summary>
    public IOpenWindow? ReturnTo { get; private set; }

    /// <summary>
    /// Where Win+T lands. Start is stop 0 and Search is stop 1, so the first app is stop 2. With no app, the next
    /// control after Search, and Start when the chain wraps around to it.
    /// </summary>
    public static int FirstApp(int appCount, int count)
    {
        if (appCount > 0)
            return 2;
        return count > 2 ? 2 : 0;
    }

    /// <summary>The stop an arrow reaches. The ends wrap, as the focus chain does.</summary>
    public static int Move(int index, int delta, int count)
    {
        if (count <= 0)
            return 0;
        var next = index + delta;
        if (next < 0)
            return count - 1;
        if (next >= count)
            return 0;
        return next;
    }

    /// <summary>Turns the keyboard on. A second call keeps the window already remembered.</summary>
    public void Enter(IOpenWindow? active)
    {
        if (Active)
            return;
        Active = true;
        ReturnTo = active;
    }

    /// <summary>Turns the keyboard off and returns the window Escape activates.</summary>
    public IOpenWindow? Leave()
    {
        if (!Active)
            return null;
        var back = ReturnTo;
        Active = false;
        ReturnTo = null;
        return back;
    }

    /// <summary>Another window came forward. Nothing is activated.</summary>
    public void Yield()
    {
        Active = false;
        ReturnTo = null;
    }
}
