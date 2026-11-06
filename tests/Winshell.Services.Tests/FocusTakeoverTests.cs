using System.Diagnostics;
using System.Text;
using Winshell.Native;

namespace Winshell.Services.Tests;

/// <summary>
/// Another process holds the foreground and is the last one that sent input. Joining its thread then leaves focus
/// where it was, and the key tap is what moves it.
/// </summary>
[Trait("Category", "Slow")]
public sealed class FocusTakeoverTests
{
    [Fact(Skip = "Fails on most CI runs until #1499 finds why the window never gains focus there.")]
    public async Task Taking_focus_reaches_the_window_only_after_a_key_tap()
    {
        var previous = Win32.GetForegroundWindow();
        var suffix = Guid.NewGuid().ToString("N");
        var stopName = @"Local\WinshellFocusStop" + suffix;
        var steadyName = @"Local\WinshellFocusSteady" + suffix;
        var errors = new StringBuilder();
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, stopName);
        using var steady = new EventWaitHandle(false, EventResetMode.ManualReset, steadyName);
        using var holder = StartHolder(stopName, steadyName, errors);
        await using var window = await PumpedWindow.Open();
        try
        {
            Assert.NotEqual(0, window.Handle);
            Wait.Until(() => TookOnlyAfterTheTap(holder, steady, window.Handle, errors),
                "the window to gain focus only after a key tap");
        }
        finally
        {
            stop.Set();
            try
            {
                Wait.Until(() => holder.HasExited, "the focus holder to exit");
            }
            finally
            {
                if (!holder.HasExited)
                    holder.Kill(true);
                if (previous != 0 && previous != window.Handle && Win32.IsWindow(previous))
                    Focus.Take(previous);
            }
        }
    }

    /// <summary>
    /// Joining the foreground thread leaves <paramref name="window"/> unfocused, and taking focus then gives it the
    /// keyboard. A miss starts the holder again: this process just sent input, so a retry means nothing until the
    /// holder is the last one that did.
    /// </summary>
    private static bool TookOnlyAfterTheTap(Process holder, EventWaitHandle steady, nint window, StringBuilder errors)
    {
        if (!Refused(holder, steady, window, errors))
            return false;
        if (Focus.Take(window) && Win32.GetForegroundWindow() == window)
            return true;
        steady.Reset();
        return false;
    }

    /// <summary>
    /// The holder is foreground, and asking plus joining its thread still leaves <paramref name="window"/> without
    /// the keyboard. That is the refusal the key tap is for.
    /// </summary>
    private static bool Refused(Process holder, EventWaitHandle steady, nint window, StringBuilder errors)
    {
        if (holder.HasExited)
            Assert.Fail("The focus holder exited early (" + holder.ExitCode + "). " + errors);
        if (ForegroundProcess() != (uint)holder.Id)
            return false;
        steady.Set();
        if (ForegroundProcess() != (uint)holder.Id)
        {
            steady.Reset();
            return false;
        }

        if (JoinedWithoutAKey(window))
        {
            steady.Reset();
            return false;
        }

        return Win32.GetForegroundWindow() != window;
    }

    /// <summary>What the three call sites did before the key tap lived in one place.</summary>
    private static bool JoinedWithoutAKey(nint window)
    {
        if (Win32.SetForegroundWindow(window) && Win32.GetForegroundWindow() == window)
            return true;
        var foreground = Win32.GetForegroundWindow();
        var current = Win32.GetCurrentThreadId();
        var other = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
        if (other != 0 && other != current && Win32.AttachThreadInput(current, other, true))
            try
            {
                Win32.SetForegroundWindow(window);
            }
            finally
            {
                Win32.AttachThreadInput(current, other, false);
            }

        return Win32.GetForegroundWindow() == window;
    }

    private static uint ForegroundProcess()
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground == 0)
            return 0;
        Win32.GetWindowThreadProcessId(foreground, out var process);
        return process;
    }

    private static Process StartHolder(string stopName, string steadyName, StringBuilder errors)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(HolderScript(stopName, steadyName)));
        var holder = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        holder.OutputDataReceived += (_, e) => Append(errors, e.Data);
        holder.ErrorDataReceived += (_, e) => Append(errors, e.Data);
        Assert.True(holder.Start());
        holder.BeginOutputReadLine();
        holder.BeginErrorReadLine();
        return holder;
    }

    private static void Append(StringBuilder errors, string? line)
    {
        if (line is null)
            return;
        lock (errors)
        {
            errors.AppendLine(line);
        }
    }

    private static string HolderScript(string stopName, string steadyName)
    {
        return HolderProgram + "\n[Holder]::Run('" + stopName + "', '" + steadyName + "')\n" +
               "} catch {\n[Console]::Error.WriteLine($_.Exception.ToString())\nexit 1\n}\n";
    }

    // Windows PowerShell 5 compiles this with the C# 5 compiler. A second process has to be the one that last sent
    // input, or this process would be allowed to take focus without the key tap. The single-quoted program keeps the
    // script valid whatever indentation the C# has.
    private const string HolderProgram = """
                                         $ErrorActionPreference = 'Stop'
                                         try {
                                         Add-Type -TypeDefinition '
                                         using System;
                                         using System.ComponentModel;
                                         using System.Runtime.InteropServices;
                                         using System.Threading;
                                         public static class Holder
                                         {
                                             public static void Run(string stopName, string steadyName)
                                             {
                                                 var stop = EventWaitHandle.OpenExisting(stopName);
                                                 var steady = EventWaitHandle.OpenExisting(steadyName);
                                                 var thread = new Thread(delegate() { Window(stop, steady); });
                                                 thread.SetApartmentState(ApartmentState.STA);
                                                 thread.IsBackground = true;
                                                 thread.Start();
                                                 thread.Join();
                                             }

                                             static void Window(EventWaitHandle stop, EventWaitHandle steady)
                                             {
                                                 var hwnd = CreateWindowExW(0, "STATIC", "winshell-focus-holder", 0x00CF0000 | 0x10000000,
                                                     0, 0, 80, 40, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
                                                 if (hwnd == IntPtr.Zero)
                                                     throw new Win32Exception(Marshal.GetLastWin32Error());
                                                 ShowWindow(hwnd, 5);
                                                 try
                                                 {
                                                     while (!stop.WaitOne(0))
                                                     {
                                                         Pump();
                                                         if (!steady.WaitOne(0) && GetForegroundWindow() != hwnd)
                                                         {
                                                             Tap();
                                                             SetForegroundWindow(hwnd);
                                                         }
                                                         if (stop.WaitOne(0))
                                                             break;
                                                         var handle = stop.SafeWaitHandle.DangerousGetHandle();
                                                         MsgWaitForMultipleObjects(1, new IntPtr[] { handle }, false, 50, 0x04FF);
                                                     }
                                                 }
                                                 finally
                                                 {
                                                     DestroyWindow(hwnd);
                                                 }
                                             }

                                             static void Pump()
                                             {
                                                 MSG msg;
                                                 while (PeekMessage(out msg, IntPtr.Zero, 0, 0, 1))
                                                 {
                                                     TranslateMessage(ref msg);
                                                     DispatchMessage(ref msg);
                                                 }
                                             }

                                             static void Tap()
                                             {
                                                 INPUT[] keys = new INPUT[2];
                                                 keys[0].type = 1;
                                                 keys[0].U.ki.wVk = 0xE8;
                                                 keys[0].U.ki.extra = new IntPtr(0x57154E4B);
                                                 keys[1].type = 1;
                                                 keys[1].U.ki.wVk = 0xE8;
                                                 keys[1].U.ki.dwFlags = 2;
                                                 keys[1].U.ki.extra = new IntPtr(0x57154E4B);
                                                 if (SendInput(2, keys, Marshal.SizeOf(typeof(INPUT))) != 2)
                                                     throw new Win32Exception(Marshal.GetLastWin32Error());
                                             }

                                             [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
                                             [DllImport("user32.dll", SetLastError = true)] static extern bool SetForegroundWindow(IntPtr hwnd);
                                             [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
                                             [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
                                             [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
                                             [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
                                             [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
                                             [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
                                             [DllImport("user32.dll")] static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint ms, uint wake);
                                             [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                                             static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
                                                 int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
                                             [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);

                                             [StructLayout(LayoutKind.Sequential)] struct MSG
                                             {
                                                 public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
                                                 public uint time; public int x; public int y;
                                             }
                                             [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION U; }
                                             [StructLayout(LayoutKind.Explicit)] struct INPUTUNION
                                             {
                                                 [FieldOffset(0)] public MOUSEINPUT mi;
                                                 [FieldOffset(0)] public KEYBDINPUT ki;
                                             }
                                             [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT
                                             {
                                                 public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra;
                                             }
                                             [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT
                                             {
                                                 public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra;
                                             }
                                         }
                                         '
                                         """;

    /// <summary>A visible window whose thread pumps, so it can be given the keyboard.</summary>
    private sealed class PumpedWindow : IAsyncDisposable
    {
        private readonly Thread _thread;
        private readonly TaskCompletionSource<nint> _created = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private uint _threadId;

        private PumpedWindow()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "focus-target" };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public nint Handle { get; private set; }

        public static async Task<PumpedWindow> Open()
        {
            var window = new PumpedWindow();
            window._thread.Start();
            window.Handle = await window._created.Task.WaitAsync(TestContext.Current.CancellationToken);
            return window;
        }

        public async ValueTask DisposeAsync()
        {
            if (_threadId != 0)
                Win32.PostThreadMessageW(_threadId, Win32.WM_QUIT, 0, 0);
            await Task.Run(() => _thread.Join()).WaitAsync(TestContext.Current.CancellationToken);
        }

        private void Run()
        {
            try
            {
                var hwnd = Win32.CreateWindowExW(0, "STATIC", "winshell-focus-target",
                    Win32.WS_OVERLAPPEDWINDOW | Win32.WS_VISIBLE, 0, 80, 80, 40, 0, 0, Win32.GetModuleHandleW(null), 0);
                if (hwnd != 0)
                    Win32.ShowWindow(hwnd, Win32.SW_SHOWNOACTIVATE);
                Win32.PeekMessageW(out _, 0, 0, 0, 0);
                _threadId = Win32.GetCurrentThreadId();
                _created.SetResult(hwnd);
                while (Win32.GetMessageW(out var msg, 0, 0, 0) > 0)
                {
                    Win32.TranslateMessage(msg);
                    Win32.DispatchMessageW(msg);
                }

                if (hwnd != 0)
                    Win32.DestroyWindow(hwnd);
            }
            catch (Exception error)
            {
                _created.TrySetException(error);
            }
        }
    }
}
