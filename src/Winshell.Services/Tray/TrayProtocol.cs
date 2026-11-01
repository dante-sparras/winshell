using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Winshell.Tray;

/// <summary>
/// One <c>Shell_NotifyIcon</c> call, as Shell32 sends it to the taskbar: a <c>NOTIFYICONDATA</c> with 32-bit handles,
/// whatever the bitness of the app.
/// </summary>
internal sealed record NotifyIconCall
{
    public uint Message { get; init; }

    public nint Window { get; init; }

    public uint Id { get; init; }

    public uint Flags { get; init; }

    public uint CallbackMessage { get; init; }

    public nint Icon { get; init; }

    public string Tip { get; init; } = "";

    public uint State { get; init; }

    public uint StateMask { get; init; }

    public string Info { get; init; } = "";

    /// <summary>The balloon timeout, except in <c>NIM_SETVERSION</c>, where it is the interface version.</summary>
    public uint TimeoutOrVersion { get; init; }

    public string InfoTitle { get; init; } = "";

    public uint InfoFlags { get; init; }

    /// <summary>The icon's GUID when the app sent <c>NIF_GUID</c> and a structure long enough to hold it.</summary>
    public Guid Guid { get; init; }
}

/// <summary>A click on a tray icon, as the app receives it.</summary>
internal enum TrayClick
{
    Left,
    Double,
    Middle,
    /// <summary>A right click, which asks the app for its menu.</summary>
    Menu,
}

/// <summary>How Explorer and apps talk about notification-area icons.</summary>
internal static class TrayProtocol
{
    public const uint Version = 3;

    public const uint Version4 = 4;

    /// <summary>The <c>dwData</c> of a <c>WM_COPYDATA</c> that carries a <c>Shell_NotifyIcon</c> call.</summary>
    public const nuint NotifyIconData = 1;

    public const int Signature = 0x34753423;

    public const uint Add = 0, Modify = 1, Delete = 2, SetVersion = 4;

    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_STATE = 0x8, NIF_INFO = 0x10, NIF_GUID = 0x20;

    public const uint NIS_HIDDEN = 0x1;

    // The NOTIFYICONDATA that Shell32 sends, after the signature and the message.
    private const int HeaderSize = 8;
    private const int TipOffset = 24, TipChars = 128;
    private const int StateOffset = 280, StateMaskOffset = 284;
    private const int InfoOffset = 288, InfoChars = 256;
    private const int TimeoutOffset = 800;
    private const int InfoTitleOffset = 804, InfoTitleChars = 64;
    private const int InfoFlagsOffset = 932;
    private const int GuidOffset = 936;

    /// <summary>The longest structure, up to <c>hBalloonIcon</c>.</summary>
    public const int FullSize = 956;

    /// <summary>
    /// Reads a call. Apps built for older Windows send a shorter structure; what they left out reads as zero.
    /// Returns null for anything that is not a call, which the taskbar still gets as it was.
    /// </summary>
    public static NotifyIconCall? Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderSize + TipOffset || BinaryPrimitives.ReadInt32LittleEndian(payload) != Signature)
            return null;
        var message = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        var sent = payload[HeaderSize..];
        Span<byte> nid = stackalloc byte[FullSize];
        nid.Clear();
        sent[..Math.Min(sent.Length, FullSize)].CopyTo(nid);
        var flags = U32(nid, 12);
        return new NotifyIconCall
        {
            Message = message,
            // Handles are 32 bits wide here, and sign-extend the way Windows does.
            Window = BinaryPrimitives.ReadInt32LittleEndian(nid[4..]),
            Id = U32(nid, 8),
            Flags = flags,
            CallbackMessage = U32(nid, 16),
            Icon = BinaryPrimitives.ReadInt32LittleEndian(nid[20..]),
            Tip = Text(nid, TipOffset, TipChars),
            State = U32(nid, StateOffset),
            StateMask = U32(nid, StateMaskOffset),
            Info = Text(nid, InfoOffset, InfoChars),
            TimeoutOrVersion = U32(nid, TimeoutOffset),
            InfoTitle = Text(nid, InfoTitleOffset, InfoTitleChars),
            InfoFlags = U32(nid, InfoFlagsOffset),
            Guid = (flags & NIF_GUID) != 0 && sent.Length >= GuidOffset + 16 ? new Guid(nid.Slice(GuidOffset, 16)) : Guid.Empty,
        };
    }

    private const uint WM_CONTEXTMENU = 0x007B, NIN_SELECT = 0x0400;
    private const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;

    /// <summary>
    /// The messages a click sends, in order: the mouse messages a window would get, and from version 3 on,
    /// <c>NIN_SELECT</c> after each left button release and <c>WM_CONTEXTMENU</c> after a right one.
    /// </summary>
    public static uint[] ClickMessages(TrayClick click, uint version)
    {
        var current = version >= Version;
        return click switch
        {
            TrayClick.Left => current ? [WM_LBUTTONDOWN, WM_LBUTTONUP, NIN_SELECT] : [WM_LBUTTONDOWN, WM_LBUTTONUP],
            TrayClick.Double => current
                ? [WM_LBUTTONDOWN, WM_LBUTTONUP, NIN_SELECT, WM_LBUTTONDBLCLK, WM_LBUTTONUP, NIN_SELECT]
                : [WM_LBUTTONDOWN, WM_LBUTTONUP, WM_LBUTTONDBLCLK, WM_LBUTTONUP],
            TrayClick.Middle => [WM_MBUTTONDOWN, WM_MBUTTONUP],
            _ => current ? [WM_RBUTTONDOWN, WM_RBUTTONUP, WM_CONTEXTMENU] : [WM_RBUTTONDOWN, WM_RBUTTONUP],
        };
    }

    /// <summary>
    /// The <c>wParam</c> and <c>lParam</c> of the icon's callback. Version 4 carries the screen point and the icon
    /// id; older versions carry the icon id and the mouse message.
    /// </summary>
    public static (nint WParam, nint LParam) Callback(uint version, uint iconId, uint message, int x, int y)
    {
        if (version >= Version4)
        {
            var w = (uint)(x & 0xffff) | ((uint)(y & 0xffff) << 16);
            var l = (message & 0xffff) | ((iconId & 0xffff) << 16);
            return ((nint)w, (nint)l);
        }
        return ((nint)iconId, (nint)message);
    }

    private static uint U32(ReadOnlySpan<byte> nid, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(nid[offset..]);

    private static string Text(ReadOnlySpan<byte> nid, int offset, int chars)
    {
        var text = MemoryMarshal.Cast<byte, char>(nid.Slice(offset, chars * 2));
        var end = text.IndexOf('\0');
        return new string(end < 0 ? text : text[..end]);
    }
}
