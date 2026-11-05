using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32.NetworkManagement.WiFi;
using Winshell.Native;
using Winshell.Network;

namespace Winshell.Services.Tests;

public sealed class WifiProfileTests
{
    private const string Secret = "winshell-profile-key-9f3a";
    private const string Marker = "winshell-profile-marker";

    // Tabs are the bytes Windows stores. A raw string would depend on the file keeping them.
    private static readonly string Saved =
        "<?xml version=\"1.0\"?>\n"
        + "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">\n"
        + "\t<!-- <connectionMode>auto</connectionMode> -->\n"
        + "\t<name>Office</name>\n"
        + "\t<SSIDConfig>\n"
        + "\t\t<SSID>\n"
        + "\t\t\t<hex>43616665</hex>\n"
        + "\t\t\t<name>Cafe</name>\n"
        + "\t\t</SSID>\n"
        + "\t\t<nonBroadcast>true</nonBroadcast>\n"
        + "\t</SSIDConfig>\n"
        + "\t<connectionMode note=\"keep\">auto</connectionMode>\n"
        + "\t<connectionType>ESS</connectionType>\n"
        + "\t<MSM>\n"
        + "\t\t<security>\n"
        + "\t\t\t<authEncryption>\n"
        + "\t\t\t\t<authentication>WPA2PSK</authentication>\n"
        + "\t\t\t\t<encryption>AES</encryption>\n"
        + "\t\t\t\t<useOneX>false</useOneX>\n"
        + "\t\t\t</authEncryption>\n"
        + "\t\t\t<sharedKey>\n"
        + "\t\t\t\t<keyType>passPhrase</keyType>\n"
        + "\t\t\t\t<protected>true</protected>\n"
        + "\t\t\t\t<keyMaterial>winshell-profile-key-9f3a</keyMaterial>\n"
        + "\t\t\t</sharedKey>\n"
        + "\t\t</security>\n"
        + "\t</MSM>\n"
        + "\t<MacRandomization xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v3\">\n"
        + "\t\t<enableRandomization>true</enableRandomization>\n"
        + "\t\t<randomizationSeed>keep-me</randomizationSeed>\n"
        + "\t</MacRandomization>\n"
        + "</WLANProfile>\n";

    private static readonly string Manual = Saved.Replace(
        "<connectionMode note=\"keep\">auto</connectionMode>",
        "<connectionMode note=\"keep\">manual</connectionMode>",
        StringComparison.Ordinal);

    private static readonly Guid AdapterId = Guid.Parse("0b0f2811-87b5-48d6-9d7a-7a26ae12ff33");

    [Fact]
    public void Round_trip_changes_only_the_connection_mode()
    {
        Assert.NotEqual(Saved, Manual);
        Assert.True(WifiProfileXml.TryParse(Saved, out var fields, out var error), error);
        Assert.Equal("", error);
        Assert.Equal("Office", fields.Name);
        Assert.Equal("Cafe", fields.Ssid);
        Assert.Equal("auto", fields.ConnectionMode);
        Assert.True(fields.AutoConnect);
        Assert.True(fields.HasPassword);
        Assert.True(fields.Hidden);
        Assert.Equal(WifiSecurityType.Wpa2Psk, fields.Security);
        Assert.DoesNotContain(Secret, fields.ToString());

        Assert.True(WifiProfileXml.TrySetAutoConnect(Saved, true, out var same, out var changed, out error), error);
        Assert.False(changed);
        Assert.Equal(Saved, same);

        Assert.True(WifiProfileXml.TrySetAutoConnect(Saved, false, out var updated, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal("", error);
        Assert.Equal(Manual, updated);
        Assert.Contains("<!-- <connectionMode>auto</connectionMode> -->", updated);
        Assert.Contains(Secret, updated);
        Assert.Contains("randomizationSeed", updated);
        Assert.True(WifiProfileXml.TryParse(updated, out var after, out error), error);
        Assert.False(after.AutoConnect);
        Assert.Equal("manual", after.ConnectionMode);
        Assert.True(after.HasPassword);
        Assert.Equal(fields.Ssid, after.Ssid);
        Assert.Equal(fields.Security, after.Security);
        Assert.Equal(fields.Hidden, after.Hidden);
        Assert.DoesNotContain(Secret, after.ToString());
        Assert.DoesNotContain(Secret, error);
    }

    [Fact]
    public void Clear_password_removes_only_the_key_element()
    {
        var cleared = Saved.Replace("<keyMaterial>" + Secret + "</keyMaterial>", "", StringComparison.Ordinal);
        Assert.NotEqual(Saved, cleared);
        Assert.True(WifiProfileXml.TryClearPassword(Saved, out var updated, out var changed, out var error), error);
        Assert.True(changed);
        Assert.Equal("", error);
        Assert.Equal(cleared, updated);
        Assert.DoesNotContain(Secret, updated);
        Assert.Contains("<protected>true</protected>", updated);
        Assert.Contains("MacRandomization", updated);
        Assert.True(WifiProfileXml.TryParse(updated, out var after, out error), error);
        Assert.False(after.HasPassword);
        Assert.Equal("auto", after.ConnectionMode);
        Assert.Equal("Cafe", after.Ssid);
        Assert.DoesNotContain(Secret, after.ToString());

        const string prefixed = """
            <WLANProfile><name>Office</name><connectionMode>auto</connectionMode><sharedKey><keyMaterial /><p:keyMaterial xmlns:p="urn:p">winshell-profile-key-9f3a</p:keyMaterial><protected>true</protected></sharedKey><Vendor xmlns="urn:vendor">keep</Vendor></WLANProfile>
            """;
        Assert.True(WifiProfileXml.TryParse(prefixed, out var before, out error), error);
        Assert.True(before.HasPassword);
        Assert.DoesNotContain(Secret, before.ToString());
        Assert.True(WifiProfileXml.TryClearPassword(prefixed, out updated, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal(
            """
            <WLANProfile><name>Office</name><connectionMode>auto</connectionMode><sharedKey><protected>true</protected></sharedKey><Vendor xmlns="urn:vendor">keep</Vendor></WLANProfile>
            """,
            updated);
        Assert.DoesNotContain(Secret, updated);

        const string comment = "<WLANProfile><name>Office</name><!-- <keyMaterial>winshell-profile-key-9f3a</keyMaterial> --><connectionMode>auto</connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TryParse(comment, out var commented, out error), error);
        Assert.False(commented.HasPassword);
        Assert.DoesNotContain(Secret, commented.ToString());
        Assert.True(WifiProfileXml.TryClearPassword(comment, out updated, out changed, out error), error);
        Assert.False(changed);
        Assert.Equal(comment, updated);

        var nested = "<WLANProfile><name>Office<nested>" + Secret + "</nested></name><connectionMode>auto</connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TryParse(nested, out var name, out error), error);
        Assert.Equal("Office", name.Name);
        Assert.False(name.HasPassword);
        Assert.DoesNotContain(Secret, name.ToString());
    }

    [Fact]
    public void Hostile_profiles_are_refused_without_a_write()
    {
        AssertNoWrite(new string('a', WifiProfileXml.MaxCharacters + 1), WifiProfileXml.TooLarge);
        AssertNoWrite("<WLANProfile><keyMaterial>" + Secret, WifiProfileXml.Unreadable);
        AssertNoWrite(Deep(40), WifiProfileXml.Unreadable);
        Assert.True(WifiProfileXml.TryParse(Deep(8), out _, out var shallow), shallow);
        Assert.Equal("", shallow);

        var dtd = """
            <?xml version="1.0"?>
            <!DOCTYPE WLANProfile [
            <!ENTITY secret SYSTEM "file:///C:/winshell-profile-marker">
            ]>
            <WLANProfile><name>&secret;</name></WLANProfile>
            """;
        AssertNoWrite(dtd, WifiProfileXml.Unreadable);
        Assert.DoesNotContain(Marker, WifiProfileXml.Unreadable);

        var billion = """
            <!DOCTYPE lolz [
            <!ENTITY lol "lol">
            <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
            <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
            <!ENTITY lol4 "&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;">
            <!ENTITY lol5 "&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;">
            <!ENTITY lol6 "&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;">
            <!ENTITY lol7 "&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;">
            <!ENTITY lol8 "&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;">
            <!ENTITY lol9 "&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;">
            ]>
            <WLANProfile><name>&lol9;</name></WLANProfile>
            """;
        AssertNoWrite(billion, WifiProfileXml.Unreadable);

        var api = new FakeWlan { Xml = dtd };
        var clear = WifiProfileEdit.ClearPassword(api, AdapterId, "Office");
        Assert.Equal(0, api.Sets);
        Assert.Equal(0, api.Connects);
        Assert.Equal(0, api.Deletes);
        Assert.False(clear.Wrote);
        Assert.DoesNotContain(Marker, clear.Error);
        Assert.DoesNotContain(Secret, clear.Error);
    }

    [Theory]
    [InlineData("open", "none", WifiSecurityType.Open)]
    [InlineData("open", "WEP", WifiSecurityType.StaticWep)]
    [InlineData("shared", "none", WifiSecurityType.StaticWep)]
    [InlineData("WPA", "TKIP", WifiSecurityType.WpaEap)]
    [InlineData("WPAPSK", "TKIP", WifiSecurityType.WpaPsk)]
    [InlineData("WPA2", "AES", WifiSecurityType.Wpa2Eap)]
    [InlineData("WPA3ENT", "AES", WifiSecurityType.Wpa2Eap)]
    [InlineData("WPA2PSK", "AES", WifiSecurityType.Wpa2Psk)]
    [InlineData("wpa2psk", "aes", WifiSecurityType.Wpa2Psk)]
    [InlineData("WPA3ENT192", "GCMP256", WifiSecurityType.Wpa3SuiteB192)]
    [InlineData("WPA3SAE", "AES", WifiSecurityType.Sae)]
    [InlineData("WPA3", "AES", WifiSecurityType.Sae)]
    [InlineData("OWE", "AES", WifiSecurityType.Owe)]
    [InlineData("other", "AES", WifiSecurityType.Unknown)]
    public void Authentication_maps_like_the_scan(string authentication, string encryption, WifiSecurityType expected)
    {
        var xml = "<WLANProfile><name>Office</name><SSIDConfig><SSID><name>Cafe</name></SSID></SSIDConfig><connectionMode>manual</connectionMode><MSM><security><authEncryption><authentication>"
            + authentication + "</authentication><encryption>" + encryption + "</encryption></authEncryption></security></MSM></WLANProfile>";
        Assert.True(WifiProfileXml.TryParse(xml, out var fields, out var error), error);
        Assert.Equal(expected, fields.Security);
        Assert.False(fields.AutoConnect);
        Assert.False(fields.HasPassword);
        Assert.DoesNotContain(Secret, fields.ToString());
    }

    [Fact]
    public void Ssid_prefers_the_name_and_rejects_a_bad_hex_value()
    {
        Assert.Equal("Cafe", Ssid("<hex>43616665</hex><name>Cafe</name>"));
        Assert.Equal("Cafe", Ssid("<hex>43616665</hex>"));
        Assert.Equal("Caf\u00e9", Ssid("<hex>436166C3A9</hex>"));
        Assert.Equal("", Ssid("<hex>zzzz</hex>"));
        Assert.Equal("", Ssid("<hex>abc</hex>"));
    }

    [Fact]
    public void A_mode_write_fails_closed_when_the_element_is_not_one_plain_value()
    {
        const string spaced = "<WLANProfile><name>Office</name><connectionMode> auto </connectionMode><Vendor>keep</Vendor></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(spaced, true, out var same, out var changed, out var error), error);
        Assert.False(changed);
        Assert.Equal(spaced, same);
        Assert.True(WifiProfileXml.TrySetAutoConnect(spaced, false, out var manual, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal("<WLANProfile><name>Office</name><connectionMode>manual</connectionMode><Vendor>keep</Vendor></WLANProfile>", manual);

        const string capital = "<WLANProfile><name>Office</name><connectionMode>Auto</connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(capital, true, out same, out changed, out error), error);
        Assert.False(changed);
        Assert.Equal(capital, same);

        const string foo = "<WLANProfile><name>Office</name><connectionMode>foo</connectionMode><Vendor>keep</Vendor></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(foo, false, out manual, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal("<WLANProfile><name>Office</name><connectionMode>manual</connectionMode><Vendor>keep</Vendor></WLANProfile>", manual);

        const string nested = "<WLANProfile><name>Office</name><connectionMode>auto</connectionMode><Vendor><connectionMode>auto</connectionMode><keep>1</keep></Vendor></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(nested, false, out manual, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal("<WLANProfile><name>Office</name><connectionMode>manual</connectionMode><Vendor><connectionMode>auto</connectionMode><keep>1</keep></Vendor></WLANProfile>", manual);

        const string empty = "<WLANProfile><name>Office</name><connectionMode></connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(empty, true, out manual, out changed, out error), error);
        Assert.Equal("<WLANProfile><name>Office</name><connectionMode>auto</connectionMode></WLANProfile>", manual);

        AssertNoWrite("<WLANProfile><name>Office</name><connectionMode>auto</connectionMode><connectionMode>auto</connectionMode></WLANProfile>", WifiProfileXml.Rejected, auto: false);
        AssertNoWrite("<WLANProfile><name>Office</name></WLANProfile>", WifiProfileXml.Rejected, auto: true);
        AssertNoWrite("<WLANProfile><name>Office</name><connectionMode/></WLANProfile>", WifiProfileXml.Rejected, auto: true);
        AssertNoWrite("<WLANProfile><name>Office</name><connectionMode><![CDATA[auto]]></connectionMode></WLANProfile>", WifiProfileXml.Rejected, auto: false);
    }

    [Theory]
    [InlineData(0x6u, 0x2u)]
    [InlineData(0x4u, 0x0u)]
    [InlineData(0x1u, 0x1u)]
    [InlineData(0x3u, 0x3u)]
    [InlineData(0x0u, 0x0u)]
    public void Writes_keep_the_profile_flag_and_drop_a_plaintext_bit(uint reported, uint written)
    {
        var api = new FakeWlan { Xml = Saved, Flags = reported };
        var change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", false);
        Assert.Null(change.Error);
        Assert.True(change.Wrote);
        Assert.Equal(written, api.WrittenFlags);
        Assert.Equal(Manual, api.WrittenXml);
        Assert.Equal(0, api.Connects);
        Assert.Equal(0, api.Deletes);
        Assert.DoesNotContain(Secret, change.Error ?? "");
    }

    [Fact]
    public void A_refused_all_users_write_reports_the_windows_error()
    {
        var denied = new Win32Exception(5).Message;
        var api = new FakeWlan { Xml = Saved, Flags = 0, SetError = 5 };
        var change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", false);
        Assert.True(change.Wrote);
        Assert.Equal(denied, change.Error);
        Assert.Equal(0u, api.WrittenFlags);
        Assert.Contains(Secret, api.WrittenXml);
        Assert.DoesNotContain(Secret, change.Error);
        Assert.Equal(0, api.Connects);
        Assert.Equal(0, api.Deletes);

        api = new FakeWlan { Xml = Saved, GetError = 5 };
        change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", false);
        Assert.False(change.Wrote);
        Assert.Equal(0, api.Sets);
        Assert.Equal(denied, change.Error);
        Assert.DoesNotContain(Secret, change.Error);

        api = new FakeWlan { Xml = Saved };
        change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", true);
        Assert.False(change.Wrote);
        Assert.Equal(0, api.Sets);
        Assert.Null(change.Error);

        const string noKey = "<WLANProfile><name>Office</name><connectionMode>auto</connectionMode></WLANProfile>";
        api = new FakeWlan { Xml = noKey };
        change = WifiProfileEdit.ClearPassword(api, AdapterId, "Office");
        Assert.False(change.Wrote);
        Assert.Equal(0, api.Sets);
        Assert.Null(change.Error);
    }

    [Fact]
    public void Profile_reads_request_no_plaintext_key_and_the_connect_struct_matches_native_layout()
    {
        Assert.Equal(0u, Wlan.ProfileReadFlags());
        Assert.Equal(0u, Wlan.ProfileReadFlags() & 0x4);
        Assert.Equal(0u, Wlan.ProfileWriteFlags(0x4));
        Assert.Equal(0x2u, Wlan.ProfileWriteFlags(0x6));
        Assert.Equal((nint)0, Marshal.OffsetOf<WLAN_CONNECTION_PARAMETERS>(nameof(WLAN_CONNECTION_PARAMETERS.wlanConnectionMode)));
        Assert.Equal((nint)IntPtr.Size, Marshal.OffsetOf<WLAN_CONNECTION_PARAMETERS>(nameof(WLAN_CONNECTION_PARAMETERS.strProfile)));
        Assert.Equal((nint)(IntPtr.Size * 2), Marshal.OffsetOf<WLAN_CONNECTION_PARAMETERS>(nameof(WLAN_CONNECTION_PARAMETERS.pDot11Ssid)));
        Assert.Equal((nint)(IntPtr.Size * 3), Marshal.OffsetOf<WLAN_CONNECTION_PARAMETERS>(nameof(WLAN_CONNECTION_PARAMETERS.pDesiredBssidList)));
        Assert.Equal((nint)(IntPtr.Size * 4), Marshal.OffsetOf<WLAN_CONNECTION_PARAMETERS>(nameof(WLAN_CONNECTION_PARAMETERS.dot11BssType)));
        Assert.Equal((nint)(IntPtr.Size * 4 + 4), Marshal.OffsetOf<WLAN_CONNECTION_PARAMETERS>(nameof(WLAN_CONNECTION_PARAMETERS.dwFlags)));
    }

    [Fact]
    public void A_loaded_profile_matches_its_ssid_and_a_failed_read_matches_its_name()
    {
        var loaded = WifiProfiles.Describe(AdapterId, "Office", Saved, 0);
        Assert.Equal(AdapterId + ":Office", loaded.Id);
        Assert.True(loaded.Loaded);
        Assert.Equal("Cafe", loaded.Ssid);
        Assert.True(loaded.HasPassword);
        Assert.True(loaded.Hidden);
        Assert.Equal("", loaded.Error);
        Assert.True(WifiProfiles.Matches(loaded, "Cafe"));
        Assert.False(WifiProfiles.Matches(loaded, "Office"));
        Assert.DoesNotContain(Secret, loaded.ToString());

        var failed = WifiProfiles.Describe(AdapterId, "Cafe", Saved, 5);
        Assert.False(failed.Loaded);
        Assert.Equal("", failed.Ssid);
        Assert.False(failed.HasPassword);
        Assert.Equal(new Win32Exception(5).Message, failed.Error);
        Assert.DoesNotContain(Secret, failed.Error);
        Assert.DoesNotContain("<", failed.Error);
        Assert.True(WifiProfiles.Matches(failed, "Cafe"));
        Assert.False(WifiProfiles.Matches(failed, "Office"));

        var unread = WifiProfiles.Describe(AdapterId, "Cafe", null, 0);
        Assert.Equal(WifiProfileXml.Unreadable, unread.Error);
        Assert.True(WifiProfiles.Matches(unread, "Cafe"));
    }

    [Fact]
    public async Task Profile_writes_run_one_at_a_time()
    {
        var queue = new ProfileWriteQueue();
        using var hold = new ManualResetEventSlim();
        var firstInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var max = 0;

        var first = queue.RunAsync(() => Enter(firstInside));
        await firstInside.Task;
        var second = queue.RunAsync(() => Enter(secondInside));
        Assert.Equal(1, Volatile.Read(ref inFlight));
        hold.Set();
        Assert.Equal(1, await first);
        await secondInside.Task;
        Assert.Equal(1, await second);
        Assert.Equal(1, max);

        int Enter(TaskCompletionSource inside)
        {
            var now = Interlocked.Increment(ref inFlight);
            int seen;
            do
            {
                seen = Volatile.Read(ref max);
            }
            while (now > seen && Interlocked.CompareExchange(ref max, now, seen) != seen);
            inside.SetResult();
            hold.Wait(TestContext.Current.CancellationToken);
            Interlocked.Decrement(ref inFlight);
            return now;
        }
    }

    private static void AssertNoWrite(string xml, string expected, bool auto = false)
    {
        Assert.False(WifiProfileXml.TrySetAutoConnect(xml, auto, out var updated, out var changed, out var error));
        Assert.False(changed);
        Assert.Equal(expected, error);
        Assert.Equal(xml, updated);
        Assert.DoesNotContain(Secret, error);
        Assert.DoesNotContain(Marker, error);
        var api = new FakeWlan { Xml = xml };
        var change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", auto);
        Assert.Equal(0, api.Sets);
        Assert.Equal(0, api.Connects);
        Assert.Equal(0, api.Deletes);
        Assert.False(change.Wrote);
        Assert.Equal(expected, change.Error);
        Assert.DoesNotContain(Secret, change.Error);
        Assert.DoesNotContain(Marker, change.Error);
    }

    private static string Deep(int levels)
    {
        var builder = new StringBuilder("<WLANProfile><name>Office</name>");
        for (var i = 0; i < levels; i++)
            builder.Append("<e>");
        builder.Append('x');
        for (var i = 0; i < levels; i++)
            builder.Append("</e>");
        builder.Append("</WLANProfile>");
        return builder.ToString();
    }

    private static string Ssid(string inner)
    {
        var xml = "<WLANProfile><name>Office</name><SSIDConfig><SSID>" + inner + "</SSID></SSIDConfig><connectionMode>auto</connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TryParse(xml, out var fields, out var error), error);
        Assert.True(fields.Name.Length > 0);
        return fields.Ssid;
    }

    private sealed class FakeWlan : IWlanProfileApi
    {
        public string? Xml { get; init; }
        public uint Flags { get; init; }
        public uint GetError { get; init; }
        public uint SetError { get; init; }
        public string? WrittenXml { get; private set; }
        public uint WrittenFlags { get; private set; }
        public int Sets { get; private set; }
        public int Connects { get; private set; }
        public int Deletes { get; private set; }

        public uint Get(Guid adapter, string profileName, out string? xml, out uint flags)
        {
            xml = GetError == 0 ? Xml : null;
            flags = Flags;
            return GetError;
        }

        public uint Set(Guid adapter, string xml, uint flags, out uint reason)
        {
            Sets++;
            WrittenXml = xml;
            WrittenFlags = flags;
            reason = 0;
            return SetError;
        }

        public uint Connect(Guid adapter, string profileName, bool hidden)
        {
            Connects++;
            return 0;
        }

        public uint Delete(Guid adapter, string profileName)
        {
            Deletes++;
            return 0;
        }
    }
}
