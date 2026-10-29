namespace Winshell.Platform.Windows.Native;

/// <summary>
/// Hosts one low-level hook on a thread of its own. Windows removes a low-level hook that misses its timeout,
/// without telling anyone, so a busy loop thread must not be the one that serves it. The thread installs the hook,
/// waits in GetMessage, and unhooks on WM_QUIT. The procedure runs on that thread, so it must not touch state that
/// belongs to the loop thread; it posts work to the loop instead.
/// </summary>
internal sealed unsafe class HookThread
{
    private readonly uint _threadId;

    private HookThread(nint handle, Thread thread, uint threadId)
    {
        Handle = handle;
        Thread = thread;
        _threadId = threadId;
    }

    internal nint Handle { get; }

    internal Thread Thread { get; }

    /// <summary>Starts the thread and waits for the hook to be installed. Null if Windows refused the hook.</summary>
    internal static HookThread? Start(int hookId, delegate* unmanaged<int, nint, nint, nint> proc, string name)
    {
        var started = new TaskCompletionSource<(nint Hook, uint ThreadId)>();
        var thread = new Thread(() => Pump(hookId, proc, started)) { IsBackground = true, Name = name };
        thread.Start();
        var (hook, threadId) = started.Task.Result;
        if (hook != 0)
            return new HookThread(hook, thread, threadId);
        thread.Join();
        return null;
    }

    /// <summary>Removes the hook and waits for its thread to end.</summary>
    internal void Stop()
    {
        Win32.PostThreadMessageW(_threadId, Win32.WM_QUIT, 0, 0);
        Thread.Join();
    }

    private static void Pump(int hookId, delegate* unmanaged<int, nint, nint, nint> proc, TaskCompletionSource<(nint Hook, uint ThreadId)> started)
    {
        // Creates the thread's message queue, so the quit message posted by Stop is not lost.
        Win32.PeekMessageW(out _, 0, 0, 0, Win32.PM_NOREMOVE);
        var hook = Win32.SetWindowsHookExW(hookId, proc, Win32.GetModuleHandleW(null), 0);
        started.SetResult((hook, Win32.GetCurrentThreadId()));
        if (hook == 0)
            return;
        while (Win32.GetMessageW(out _, 0, 0, 0) > 0)
        {
        }
        Win32.UnhookWindowsHookEx(hook);
    }
}
