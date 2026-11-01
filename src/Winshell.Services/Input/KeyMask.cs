using System.Runtime.InteropServices;

namespace Winshell.Input;

/// <summary>
/// One down and up of an unassigned virtual key. Sent while the Windows key is still down, it marks that press as
/// used so Start does not open, without leaving the Windows key logically held. The hook ignores this injection.
/// </summary>
internal static partial class KeyMask
{
    /// <summary>Unassigned virtual key. The same value AutoHotkey uses for its menu mask.</summary>
    public const ushort VirtualKey = 0xE8;

    /// <summary>Tag on <c>dwExtraInfo</c> so the low-level hook does not treat the mask as a user key.</summary>
    public const nint ExtraInfo = 0x57154E4B;

    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;

    public static void Tap()
    {
        var stroke = new Input[2];
        stroke[0] = Key(up: false);
        stroke[1] = Key(up: true);
        _ = SendInput(2, stroke, Marshal.SizeOf<Input>());
    }

    internal static bool IsOurs(nint extraInfo) => extraInfo == ExtraInfo;

    internal static MaskEvent[] Stroke() =>
    [
        new(VirtualKey, false, ExtraInfo),
        new(VirtualKey, true, ExtraInfo),
    ];

    internal static int InputSize => Marshal.SizeOf<Input>();

    private static Input Key(bool up) => new()
    {
        type = InputKeyboard,
        U = new InputUnion
        {
            ki = new KeybdInput
            {
                wVk = VirtualKey,
                dwFlags = up ? KeyUp : 0,
                dwExtraInfo = ExtraInfo,
            },
        },
    };

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int bytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput mi;

        [FieldOffset(0)]
        public KeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}

internal readonly record struct MaskEvent(ushort VirtualKey, bool Up, nint ExtraInfo);
