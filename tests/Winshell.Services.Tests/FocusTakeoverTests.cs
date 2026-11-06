using System.Diagnostics;
using System.Text;
using Winshell.Native;

namespace Winshell.Services.Tests;

/// <summary>
/// Another process holds the foreground and is the last one that sent input. What <see cref="Focus.Take"/> does
/// before its key tap then leaves focus where it was, and the tap is what moves it. A foreground lock timeout of
/// zero never produces that refusal: any process may take the foreground, so the test says so instead of waiting
/// out its patience. The same when the holder sends input and never becomes the foreground.
/// </summary>
[Trait("Category", "Slow")]
public sealed class FocusTakeoverTests
{
    /// <summary>How often Windows may hand over the foreground with no key tap before that is the machine, not a race.</summary>
    private const int GrantsThatMeanNoLock = 3;

    private enum Refusal
    {
        /// <summary>The holder is not yet the foreground process that last sent input.</summary>
        Waiting,

        /// <summary><see cref="Focus.Take"/> without its key tap left the window unfocused.</summary>
        Refused,

        /// <summary>This process took the foreground without a key tap.</summary>
        Granted
    }

    [Fact]
    public async Task Taking_focus_reaches_the_window_only_after_a_key_tap()
    {
        var previous = Win32.GetForegroundWindow();
        var suffix = Guid.NewGuid().ToString("N");
        var stopName = @"Local\WinshellFocusStop" + suffix;
        var steadyName = @"Local\WinshellFocusSteady" + suffix;
        var tappedName = @"Local\WinshellFocusTapped" + suffix;
        var blockedName = @"Local\WinshellFocusBlocked" + suffix;
        var errors = new StringBuilder();
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, stopName);
        using var steady = new EventWaitHandle(false, EventResetMode.ManualReset, steadyName);
        using var tapped = new EventWaitHandle(false, EventResetMode.ManualReset, tappedName);
        using var blocked = new EventWaitHandle(false, EventResetMode.ManualReset, blockedName);
        using var holder = StartHolder(stopName, steadyName, tappedName, blockedName, errors);
        await using var window = await PumpedWindow.Open();
        var passed = false;
        try
        {
            Assert.NotEqual(0, window.Handle);
            var grants = 0;
            Wait.Until(() =>
            {
                // The holder gives up once its taps have not made it the foreground. That is the precondition
                // failing, not a reason to sit out the rest of the patience.
                if (blocked.WaitOne(0))
                    Assert.Fail("Could not establish the precondition: the focus holder sent input and never " +
                                "became the foreground. " + errors);
                var refusal = Observe(holder, steady, tapped, window.Handle, errors);
                if (refusal != Refusal.Granted)
                    return refusal == Refusal.Refused;
                // Each grant is Focus.Take without the key tap. If that keeps working, the tap is not what moved
                // focus, and another wait will not make a refusal appear.
                grants++;
                if (grants == GrantsThatMeanNoLock)
                    Assert.Fail("Could not establish the precondition: Windows gave this process the foreground " +
                                "without a key tap. " + errors);
                return false;
            }, "could not establish the precondition: the focus holder never started");

            // The holder stays put, so a foreground change that lands after SetForegroundWindow returns is visible.
            // Take is the same steps as the refusal check, plus the key tap.
            Focus.Take(window.Handle);
            Wait.Until(() => Win32.GetForegroundWindow() == window.Handle,
                "Focus.Take to give the window the keyboard after Windows had refused it",
                TimeSpan.FromSeconds(5), () =>
                {
                    if (holder.HasExited)
                        Assert.Fail("The focus holder exited early (" + holder.ExitCode + "). " + errors);
                });
            passed = true;
        }
        finally
        {
            stop.Set();
            Wait.Until(() => holder.HasExited, "the focus holder to exit");
            if (previous != 0 && previous != window.Handle && Win32.IsWindow(previous))
                Focus.Take(previous);
            if (passed && holder.ExitCode != 0)
                Assert.Fail("The focus holder did not restore the foreground lock (" + holder.ExitCode + "). " +
                            errors);
        }
    }

    /// <summary>
    /// The holder has sent input and is foreground, and <see cref="Focus.Take"/> without its key tap still leaves
    /// <paramref name="window"/> without the keyboard. A late grant counts as <see cref="Refusal.Granted"/>: the
    /// foreground switch is not always visible in the call that asked for it.
    /// </summary>
    private static Refusal Observe(Process holder, EventWaitHandle steady, EventWaitHandle tapped, nint window,
        StringBuilder errors)
    {
        if (holder.HasExited)
            Assert.Fail("The focus holder exited early (" + holder.ExitCode + "). " + errors);
        if (ForegroundProcess() != (uint)holder.Id || !tapped.WaitOne(0))
            return Refusal.Waiting;
        steady.Set();
        if (ForegroundProcess() != (uint)holder.Id)
        {
            steady.Reset();
            return Refusal.Waiting;
        }

        if (!TakenWithoutTheKey(window))
        {
            // Nothing else signals that Windows declined. A switch that is only late must not look like a refusal,
            // or Focus.Take would be credited for a handover that was already on its way.
            Wait.ForNothingToHappen(TimeSpan.FromSeconds(1));
            if (holder.HasExited)
                Assert.Fail("The focus holder exited early (" + holder.ExitCode + "). " + errors);
        }

        if (Win32.GetForegroundWindow() != window && ForegroundProcess() == (uint)holder.Id)
            return Refusal.Refused;
        steady.Reset();
        tapped.Reset();
        return Win32.GetForegroundWindow() == window ? Refusal.Granted : Refusal.Waiting;
    }

    /// <summary>What <see cref="Focus.Take"/> does before it taps a key, including the raise while joined.</summary>
    private static bool TakenWithoutTheKey(nint window)
    {
        if (Win32.SetForegroundWindow(window) && Win32.GetForegroundWindow() == window)
            return true;
        var foreground = Win32.GetForegroundWindow();
        var current = Win32.GetCurrentThreadId();
        var other = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
        if (other != 0 && other != current && Win32.AttachThreadInput(current, other, true))
            try
            {
                TopLevelWindows.Raise(window);
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

    private static Process StartHolder(string stopName, string steadyName, string tappedName, string blockedName,
        StringBuilder errors)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            HolderScript(stopName, steadyName, tappedName, blockedName)));
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

    private static string HolderScript(string stopName, string steadyName, string tappedName, string blockedName)
    {
        return HolderProgram + "\n[Holder]::Run('" + stopName + "', '" + steadyName + "', '" + tappedName + "', '" +
               blockedName + "')\n" +
               "} catch {\n[Console]::Error.WriteLine($_.Exception.ToString())\nexit 1\n}\n";
    }

    // Windows PowerShell 5 compiles this with the C# 5 compiler. A second process has to be the one that last sent
    // input, or this process would be allowed to take focus without the key tap. The single-quoted program keeps the
    // script valid whatever indentation the C# has.
    private const string HolderProgram = """
                                         $ErrorActionPreference = 'Stop'
                                         $ProgressPreference = 'SilentlyContinue'
                                         try {
                                         Add-Type -TypeDefinition '
                                         using System;
                                         using System.ComponentModel;
                                         using System.Runtime.InteropServices;
                                         using System.Threading;
                                         public static class Holder
                                         {
                                             public static void Run(string stopName, string steadyName, string tappedName, string blockedName)
                                             {
                                                 var stop = EventWaitHandle.OpenExisting(stopName);
                                                 var steady = EventWaitHandle.OpenExisting(steadyName);
                                                 var tapped = EventWaitHandle.OpenExisting(tappedName);
                                                 var blocked = EventWaitHandle.OpenExisting(blockedName);
                                                 Exception error = null;
                                                 var thread = new Thread(delegate()
                                                 {
                                                     try { Window(stop, steady, tapped, blocked); }
                                                     catch (Exception ex) { error = ex; }
                                                 });
                                                 thread.SetApartmentState(ApartmentState.STA);
                                                 thread.IsBackground = true;
                                                 thread.Start();
                                                 thread.Join();
                                                 if (error != null)
                                                     throw error;
                                             }

                                             static void Window(EventWaitHandle stop, EventWaitHandle steady, EventWaitHandle tapped, EventWaitHandle blocked)
                                             {
                                                 // A timeout of zero lets every process take the foreground, so the refusal this test
                                                 // exists for never happens. Ten seconds covers this run. Restore zero on every exit,
                                                 // including a failure to create the window.
                                                 uint previous = 0;
                                                 bool imposed = false;
                                                 IntPtr hwnd = IntPtr.Zero;
                                                 try
                                                 {
                                                     if (!ReadLock(ref previous))
                                                         Console.WriteLine("could not read the foreground lock timeout: " + Marshal.GetLastWin32Error());
                                                     else if (previous == 0)
                                                     {
                                                         imposed = WriteLock(10000);
                                                         if (!imposed)
                                                             Console.WriteLine("could not set the foreground lock timeout: " + Marshal.GetLastWin32Error());
                                                     }
                                                     Console.WriteLine("foreground-lock-timeout=" + previous);
                                                     hwnd = CreateWindowExW(0, "STATIC", "winshell-focus-holder", 0x00CF0000 | 0x10000000,
                                                         0, 0, 80, 40, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
                                                     if (hwnd == IntPtr.Zero)
                                                         throw new Win32Exception(Marshal.GetLastWin32Error());
                                                     ShowWindow(hwnd, 5);
                                                     var attempts = 0;
                                                     while (!stop.WaitOne(0))
                                                     {
                                                         Pump();
                                                         // Tap even when this window is already in front: the test may have cleared the
                                                         // signal after taking focus, and only a new tap makes this process the last input.
                                                         if (!steady.WaitOne(0) && !tapped.WaitOne(0) && !blocked.WaitOne(0))
                                                         {
                                                             Tap();
                                                             if (GetForegroundWindow() != hwnd)
                                                                 SetForegroundWindow(hwnd);
                                                             if (GetForegroundWindow() == hwnd)
                                                             {
                                                                 tapped.Set();
                                                                 attempts = 0;
                                                             }
                                                             else if (++attempts == 100)
                                                                 blocked.Set();
                                                         }
                                                         if (stop.WaitOne(0))
                                                             break;
                                                         var handle = stop.SafeWaitHandle.DangerousGetHandle();
                                                         MsgWaitForMultipleObjects(1, new IntPtr[] { handle }, false, 50, 0x04FF);
                                                     }
                                                 }
                                                 finally
                                                 {
                                                     if (hwnd != IntPtr.Zero)
                                                         DestroyWindow(hwnd);
                                                     if (imposed && !WriteLock(0))
                                                         throw new Win32Exception(Marshal.GetLastWin32Error());
                                                 }
                                             }

                                             static bool ReadLock(ref uint value)
                                             {
                                                 return GetLock(0x2000, 0, ref value, 0);
                                             }

                                             static bool WriteLock(uint value)
                                             {
                                                 return SetLock(0x2001, 0, new IntPtr((int)value), 0);
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

                                             [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
                                             static extern bool GetLock(uint action, uint param, ref uint value, uint winIni);
                                             [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
                                             static extern bool SetLock(uint action, uint param, IntPtr value, uint winIni);
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
