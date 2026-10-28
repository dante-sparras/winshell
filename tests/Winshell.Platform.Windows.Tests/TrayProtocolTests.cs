using System.Buffers.Binary;
using System.Text;
using Winshell.Platform.Windows.Services.Tray;

namespace Winshell.Platform.Windows.Tests;

public class TrayProtocolTests
{
    [Fact]
    public void Version_4_callbacks_carry_the_point_and_the_icon_id()
    {
        var (w, l) = TrayProtocol.Callback(TrayProtocol.Version4, 7, 0x0205, 20, 40);
        Assert.Equal((nint)(20u | (40u << 16)), w);
        Assert.Equal((nint)(0x0205u | (7u << 16)), l);
    }

    [Fact]
    public void Older_callbacks_carry_the_icon_id_and_the_message()
    {
        var (w, l) = TrayProtocol.Callback(TrayProtocol.Version, 7, 0x0205, 20, 40);
        Assert.Equal((nint)7, w);
        Assert.Equal((nint)0x0205, l);
    }

    [Fact]
    public void A_point_left_of_the_origin_keeps_its_low_16_bits()
    {
        var (w, _) = TrayProtocol.Callback(TrayProtocol.Version4, 1, 0x007B, -8, 100);
        Assert.Equal((nint)(0xfff8u | (100u << 16)), w);
    }

    [Theory]
    [InlineData(TrayClick.Left, 0u, new uint[] { 0x0201, 0x0202 })]
    [InlineData(TrayClick.Left, 4u, new uint[] { 0x0201, 0x0202, 0x0400 })]
    [InlineData(TrayClick.Double, 0u, new uint[] { 0x0201, 0x0202, 0x0203, 0x0202 })]
    [InlineData(TrayClick.Double, 3u, new uint[] { 0x0201, 0x0202, 0x0400, 0x0203, 0x0202, 0x0400 })]
    [InlineData(TrayClick.Middle, 4u, new uint[] { 0x0207, 0x0208 })]
    [InlineData(TrayClick.Menu, 0u, new uint[] { 0x0204, 0x0205 })]
    [InlineData(TrayClick.Menu, 3u, new uint[] { 0x0204, 0x0205, 0x007B })]
    internal void Clicks_send_what_explorer_sends(TrayClick click, uint version, uint[] messages) =>
        Assert.Equal(messages, TrayProtocol.ClickMessages(click, version));

    [Fact]
    public void A_full_structure_reads_every_field()
    {
        var guid = Guid.NewGuid();
        var call = TrayProtocol.Read(TrayCall.Build(TrayProtocol.Modify, unchecked((int)0x80001234), 9,
            TrayProtocol.NIF_MESSAGE | TrayProtocol.NIF_TIP | TrayProtocol.NIF_STATE | TrayProtocol.NIF_INFO | TrayProtocol.NIF_GUID,
            callback: 0x8001, icon: 0x0042, tip: "Sync\nUp to date", state: 1, stateMask: 1, info: "Done", timeoutOrVersion: 10000,
            infoTitle: "Backup", infoFlags: 2, guid: guid));
        Assert.NotNull(call);
        Assert.Equal(TrayProtocol.Modify, call.Message);
        // Handles sign-extend, as Windows does for 32-bit handle values.
        Assert.Equal(unchecked((nint)(int)0x80001234), call.Window);
        Assert.Equal(9u, call.Id);
        Assert.Equal(0x8001u, call.CallbackMessage);
        Assert.Equal((nint)0x42, call.Icon);
        Assert.Equal("Sync\nUp to date", call.Tip);
        Assert.Equal(1u, call.State);
        Assert.Equal(1u, call.StateMask);
        Assert.Equal("Done", call.Info);
        Assert.Equal(10000u, call.TimeoutOrVersion);
        Assert.Equal("Backup", call.InfoTitle);
        Assert.Equal(2u, call.InfoFlags);
        Assert.Equal(guid, call.Guid);
    }

    [Fact]
    public void A_structure_from_an_older_app_reads_what_it_sent()
    {
        // NOTIFYICONDATAW_V1 ends after a 64-character tip; the GUID flag cannot point past it.
        var call = TrayProtocol.Read(TrayCall.Build(TrayProtocol.Add, 0x100, 1, TrayProtocol.NIF_TIP | TrayProtocol.NIF_GUID,
            tip: "Old app", guid: Guid.NewGuid(), size: 24 + 64 * 2));
        Assert.NotNull(call);
        Assert.Equal("Old app", call.Tip);
        Assert.Equal(Guid.Empty, call.Guid);
        Assert.Equal(0u, call.TimeoutOrVersion);
    }

    [Fact]
    public void A_longer_structure_from_a_newer_windows_still_reads()
    {
        var payload = TrayCall.Build(TrayProtocol.Add, 0x100, 1, TrayProtocol.NIF_TIP, tip: "Future", size: TrayProtocol.FullSize + 64);
        Assert.Equal("Future", TrayProtocol.Read(payload)?.Tip);
    }

    [Fact]
    public void Anything_else_is_not_a_call()
    {
        Assert.Null(TrayProtocol.Read(new byte[16]));
        var payload = TrayCall.Build(TrayProtocol.Add, 0x100, 1, 0);
        payload[0] ^= 0xff;
        Assert.Null(TrayProtocol.Read(payload));
    }
}

/// <summary>Builds the bytes Shell32 sends for a <c>Shell_NotifyIcon</c> call, from the SDK's field offsets.</summary>
internal static class TrayCall
{
    public static byte[] Build(
        uint message,
        int window,
        uint id,
        uint flags,
        uint callback = 0,
        int icon = 0,
        string tip = "",
        uint state = 0,
        uint stateMask = 0,
        string info = "",
        uint timeoutOrVersion = 0,
        string infoTitle = "",
        uint infoFlags = 0,
        Guid? guid = null,
        int size = TrayProtocol.FullSize)
    {
        var nid = new byte[Math.Max(size, TrayProtocol.FullSize)];
        BinaryPrimitives.WriteInt32LittleEndian(nid, size);
        BinaryPrimitives.WriteInt32LittleEndian(nid.AsSpan(4), window);
        BinaryPrimitives.WriteUInt32LittleEndian(nid.AsSpan(8), id);
        BinaryPrimitives.WriteUInt32LittleEndian(nid.AsSpan(12), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(nid.AsSpan(16), callback);
        BinaryPrimitives.WriteInt32LittleEndian(nid.AsSpan(20), icon);
        Encoding.Unicode.GetBytes(tip, nid.AsSpan(24));
        BinaryPrimitives.WriteUInt32LittleEndian(nid.AsSpan(280), state);
        BinaryPrimitives.WriteUInt32LittleEndian(nid.AsSpan(284), stateMask);
        Encoding.Unicode.GetBytes(info, nid.AsSpan(288));
        BinaryPrimitives.WriteUInt32LittleEndian(nid.AsSpan(800), timeoutOrVersion);
        Encoding.Unicode.GetBytes(infoTitle, nid.AsSpan(804));
        BinaryPrimitives.WriteUInt32LittleEndian(nid.AsSpan(932), infoFlags);
        (guid ?? Guid.Empty).TryWriteBytes(nid.AsSpan(936));
        var payload = new byte[8 + size];
        BinaryPrimitives.WriteInt32LittleEndian(payload, TrayProtocol.Signature);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), message);
        nid.AsSpan(0, size).CopyTo(payload.AsSpan(8));
        return payload;
    }
}
