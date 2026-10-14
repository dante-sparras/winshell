using System.Runtime.InteropServices;

namespace Winshell;

/// <summary>
/// Watches mouse buttons while a panel is open, on its own thread.
/// Raw input is delivered after the cursor has already moved, so the pointer does not wait on the bar.
/// The thread and its window are made by the first <see cref="Listen"/>, so a desktop that never opens a panel has neither.
/// </summary>
internal sealed class OutsideClickWatch
{
    private const int RidInput = 0x10000003;
    private const uint ButtonDown = 0x0001 | 0x0004 | 0x0010;
    private const uint WmInput = 0x00FF;
    private static readonly int HeaderBytes = Marshal.SizeOf<RawHeader>();

    private readonly Thread _thread;
    private readonly WndProc _proc;
    private readonly ManualResetEventSlim _ready = new(false);
    private nint _hwnd;
    private nint _buffer;
    private uint _bufferBytes = 64;
    private Action<int, int>? _down;
    private int _listening;
    private int _started;

    internal OutsideClickWatch()
    {
        _proc = OnMessage;
        _buffer = Marshal.AllocHGlobal((int)_bufferBytes);
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "winshell-pointer",
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Reports button-down positions until <see cref="Silence"/>.</summary>
    internal void Listen(Action<int, int> down)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _thread.Start();
        }

        // The window raw input is sent to must exist before it is registered below.
        _ready.Wait();
        _down = down;
        Volatile.Write(ref _listening, 1);
        Register(true);
    }

    /// <summary>Stops reporting. Moves are not delivered to the bar thread either way.</summary>
    internal void Silence()
    {
        Volatile.Write(ref _listening, 0);
        Register(false);
    }

    private void Run()
    {
        try
        {
            var instance = GetModuleHandle(null);
            var windowClass = new WindowClass
            {
                Proc = _proc,
                Instance = instance,
                Name = "WinshellOutsideClick",
            };
            RegisterClass(ref windowClass);
            // Raw input is not delivered to a message-only or never-shown window.
            _hwnd = CreateWindowEx(0x08080080, windowClass.Name, "Winshell", 0x80000000, -32000, -32000, 1, 1, 0, 0, instance, 0);
            if (_hwnd != 0)
            {
                SetLayeredWindowAttributes(_hwnd, 0, 0, 2);
                ShellNative.ShowWindow(_hwnd, 8);
            }

            if (_hwnd == 0)
            {
                Log.Warning("Bar", "outside-click window was not created");
            }
        }
        finally
        {
            _ready.Set();
        }

        if (_hwnd == 0)
        {
            return;
        }

        while (GetMessage(out var message, 0, 0, 0))
        {
            DispatchMessage(ref message);
        }
    }

    private void Register(bool listen)
    {
        if (_hwnd == 0)
        {
            return;
        }

        var device = new RawDevice
        {
            UsagePage = 0x01,
            Usage = 0x02,
            Flags = listen ? 0x00000100u : 0x00000001u,
            Target = listen ? _hwnd : 0,
        };
        if (!RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<RawDevice>()) && listen)
        {
            Log.Warning("Bar", "could not watch presses outside the panel");
        }
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message != WmInput || Volatile.Read(ref _listening) == 0)
        {
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        // Every move of the mouse comes here while a panel is open. A mouse record fits the buffer, so it is read in one call.
        // Only a record that did not fit is asked for its size.
        var size = _bufferBytes;
        var read = GetRawInputData(lParam, RidInput, _buffer, ref size, (uint)HeaderBytes);
        if (read == unchecked((uint)-1))
        {
            size = 0;
            _ = GetRawInputData(lParam, RidInput, 0, ref size, (uint)HeaderBytes);
            if (size <= _bufferBytes)
            {
                return DefWindowProc(hwnd, message, wParam, lParam);
            }

            Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal((int)size);
            _bufferBytes = size;
            read = GetRawInputData(lParam, RidInput, _buffer, ref size, (uint)HeaderBytes);
            if (read == unchecked((uint)-1))
            {
                return DefWindowProc(hwnd, message, wParam, lParam);
            }
        }

        if (read < (uint)HeaderBytes + 6)
        {
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (Marshal.ReadInt32(_buffer) != 0)
        {
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        // usFlags is 2 bytes, then the button union is aligned to 4, so the flags sit at +4.
        var buttons = (ushort)Marshal.ReadInt16(_buffer, HeaderBytes + 4);
        if ((buttons & ButtonDown) == 0 || !GetCursorPos(out var point))
        {
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        try
        {
            _down?.Invoke(point.X, point.Y);
        }
        catch (Exception ex)
        {
            Log.Warning("Bar", "outside press failed", ex);
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Style;
        internal WndProc Proc;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal string? Menu;
        internal string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader
    {
        internal int Type;
        internal int Size;
        internal nint Device;
        internal nint Param;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? module);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out Message message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(ref RawDevice device, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint raw, uint command, nint data, ref uint size, uint header);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);
}
